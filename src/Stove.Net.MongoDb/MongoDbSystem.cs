using System.Linq.Expressions;
using System.Text.Json;
using DotNet.Testcontainers.Images;
using MongoDB.Driver;
using Stove.Net.Core;
using Stove.Net.Core.Reporting;
using Testcontainers.MongoDb;

namespace Stove.Net.MongoDb;

/// <summary>
/// MongoDB system using Testcontainers. Manages a MongoDB container
/// and provides insert/find/assertion methods for e2e testing.
/// </summary>
public class MongoDbSystem(MongoDbSystemOptions options)
    : IPluggedSystem, IExposesConfiguration, IStoveReportingSystem, IReportsState
{
    private const string SystemName = "MongoDb";
    private MongoDbContainer? _container;
    private string? _connectionString;
    private MongoClient? _client;
    private IStoveEventEmitter? _emitter;
    private int _operationCount;
    private int _failedCount;

    public void SetEmitter(IStoveEventEmitter emitter) => _emitter = emitter;

    public string ConnectionString => _connectionString
                                      ?? throw new InvalidOperationException("MongoDB container is not started yet.");

    public MongoClient Client => _client
                                 ?? throw new InvalidOperationException("MongoDB container is not started yet.");

    public IMongoDatabase Database => Client.GetDatabase(options.DatabaseName);

    public async Task RunAsync()
    {
        _container = new MongoDbBuilder(new DockerImage("mongo:7")).Build();
        await _container.StartAsync();
        _connectionString = _container.GetConnectionString();
        _client = new MongoClient(_connectionString);
    }

    public async Task CleanupAsync()
    {
        if (options.Cleanup != null)
            await options.Cleanup();
    }

    public IEnumerable<KeyValuePair<string, string>> Configuration()
    {
        if (options.ConfigureExposedConfiguration != null && _connectionString != null)
            return options.ConfigureExposedConfiguration(_connectionString);

        if (_connectionString != null)
            return [new KeyValuePair<string, string>("MongoDb:ConnectionString", _connectionString)];

        return [];
    }

    private IMongoCollection<T> GetCollection<T>(string collection) =>
        Database.GetCollection<T>(collection);

    // --- Insert ---

    public async Task<MongoDbSystem> InsertAsync<T>(string collection, T document)
    {
        var start = DateTimeOffset.UtcNow;
        try
        {
            await GetCollection<T>(collection).InsertOneAsync(document);
            Emit("Insert", collection, "ok", start);
        }
        catch (Exception ex) when (EmitFailure("Insert", collection, ex, start)) { }
        return this;
    }

    public async Task<MongoDbSystem> InsertManyAsync<T>(string collection, IEnumerable<T> documents)
    {
        var start = DateTimeOffset.UtcNow;
        try
        {
            var list = documents.ToList();
            await GetCollection<T>(collection).InsertManyAsync(list);
            Emit("InsertMany", collection, $"{list.Count} doc(s)", start);
        }
        catch (Exception ex) when (EmitFailure("InsertMany", collection, ex, start)) { }
        return this;
    }

    // --- Query ---

    public async Task<MongoDbSystem> ShouldFind<T>(
        string collection, Expression<Func<T, bool>> filter, Action<List<T>> validate)
    {
        var start = DateTimeOffset.UtcNow;
        try
        {
            var results = await GetCollection<T>(collection).Find(filter).ToListAsync();
            validate(results);
            Emit("ShouldFind", collection, $"{results.Count} doc(s)", start);
        }
        catch (Exception ex) when (EmitFailure("ShouldFind", collection, ex, start)) { }
        return this;
    }

    public async Task<MongoDbSystem> ShouldFindAll<T>(string collection, Action<List<T>> validate)
    {
        var start = DateTimeOffset.UtcNow;
        try
        {
            var results = await GetCollection<T>(collection).Find(_ => true).ToListAsync();
            validate(results);
            Emit("ShouldFindAll", collection, $"{results.Count} doc(s)", start);
        }
        catch (Exception ex) when (EmitFailure("ShouldFindAll", collection, ex, start)) { }
        return this;
    }

    public async Task<MongoDbSystem> ShouldExist<T>(string collection, Expression<Func<T, bool>> filter)
    {
        var start = DateTimeOffset.UtcNow;
        try
        {
            var count = await GetCollection<T>(collection).CountDocumentsAsync(filter);
            if (count == 0)
                throw new InvalidOperationException(
                    $"Expected a document matching the filter to exist in '{collection}', but none were found.");
            Emit("ShouldExist", collection, $"{count} doc(s)", start);
        }
        catch (Exception ex) when (EmitFailure("ShouldExist", collection, ex, start)) { }
        return this;
    }

    public async Task<MongoDbSystem> ShouldNotExist<T>(string collection, Expression<Func<T, bool>> filter)
    {
        var start = DateTimeOffset.UtcNow;
        try
        {
            var count = await GetCollection<T>(collection).CountDocumentsAsync(filter);
            if (count > 0)
                throw new InvalidOperationException(
                    $"Expected no documents matching the filter in '{collection}', but found {count}.");
            Emit("ShouldNotExist", collection, "0 doc(s)", start);
        }
        catch (Exception ex) when (EmitFailure("ShouldNotExist", collection, ex, start)) { }
        return this;
    }

    public async Task<MongoDbSystem> ShouldCount<T>(
        string collection, Expression<Func<T, bool>> filter, Action<long> validate)
    {
        var start = DateTimeOffset.UtcNow;
        try
        {
            var count = await GetCollection<T>(collection).CountDocumentsAsync(filter);
            validate(count);
            Emit("ShouldCount", collection, $"{count}", start);
        }
        catch (Exception ex) when (EmitFailure("ShouldCount", collection, ex, start)) { }
        return this;
    }

    // --- Delete ---

    public async Task<MongoDbSystem> DeleteAsync<T>(string collection, Expression<Func<T, bool>> filter)
    {
        var start = DateTimeOffset.UtcNow;
        try
        {
            var result = await GetCollection<T>(collection).DeleteManyAsync(filter);
            Emit("Delete", collection, $"{result.DeletedCount} deleted", start);
        }
        catch (Exception ex) when (EmitFailure("Delete", collection, ex, start)) { }
        return this;
    }

    public async Task<MongoDbSystem> DropCollectionAsync(string collection)
    {
        var start = DateTimeOffset.UtcNow;
        try
        {
            await Database.DropCollectionAsync(collection);
            Emit("DropCollection", collection, "ok", start);
        }
        catch (Exception ex) when (EmitFailure("DropCollection", collection, ex, start)) { }
        return this;
    }

    public async ValueTask DisposeAsync()
    {
        _client?.Dispose();
        if (_container != null) await _container.DisposeAsync();
    }

    public StoveSnapshot Report() => new()
    {
        System = SystemName,
        StateJson = JsonSerializer.Serialize(new { operationCount = _operationCount, failedCount = _failedCount, database = options.DatabaseName }),
        Summary = $"{_operationCount} operation(s), {_failedCount} failed"
    };

    private void Emit(string action, string? input, string? output, DateTimeOffset start)
    {
        Interlocked.Increment(ref _operationCount);
        if (_emitter == null) return;
        var traceId = _emitter.CurrentTraceId;
        var metadata = new Dictionary<string, string>
        {
            ["db.system"] = "mongodb",
            ["db.operation"] = action
        };
        if (input != null) metadata["db.collection"] = input;
        _emitter.Emit(new StoveEntry
        {
            TestId = _emitter.CurrentTestId, TraceId = traceId,
            System = SystemName, Action = action,
            Result = EntryResult.Success, Input = input, Output = output,
            Metadata = metadata
        });
        _emitter.EmitSpan(new StoveSpan
        {
            TraceId = traceId, SpanId = StoveSpan.NewSpanId(),
            ParentSpanId = _emitter.CurrentSpanId,
            OperationName = action, ServiceName = SystemName,
            Start = start, End = DateTimeOffset.UtcNow, Status = "OK"
        });
    }

    private bool EmitFailure(string action, string? input, Exception ex, DateTimeOffset start)
    {
        Interlocked.Increment(ref _failedCount);
        if (_emitter != null)
        {
            var traceId = _emitter.CurrentTraceId;
            var metadata = new Dictionary<string, string>
            {
                ["db.system"] = "mongodb",
                ["db.operation"] = action
            };
            if (input != null) metadata["db.collection"] = input;
            _emitter.Emit(new StoveEntry
            {
                TestId = _emitter.CurrentTestId, TraceId = traceId,
                System = SystemName, Action = action,
                Result = EntryResult.Failed, Input = input, Error = ex.Message,
                Metadata = metadata
            });
            _emitter.EmitSpan(new StoveSpan
            {
                TraceId = traceId, SpanId = StoveSpan.NewSpanId(),
                ParentSpanId = _emitter.CurrentSpanId,
                OperationName = action, ServiceName = SystemName,
                Start = start, End = DateTimeOffset.UtcNow, Status = "ERROR",
                Exception = new StoveExceptionInfo(ex.GetType().Name, ex.Message,
                    ex.StackTrace?.Split('\n') ?? [])
            });
        }
        return false;
    }
}
