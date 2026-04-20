using System.Linq.Expressions;
using DotNet.Testcontainers.Images;
using MongoDB.Driver;
using Stove.Net.Core;
using Testcontainers.MongoDb;

namespace Stove.Net.MongoDb;

/// <summary>
/// MongoDB system using Testcontainers. Manages a MongoDB container
/// and provides insert/find/assertion methods for e2e testing.
/// </summary>
public class MongoDbSystem(MongoDbSystemOptions options)
    : IPluggedSystem, IExposesConfiguration, IStoveReportingSystem
{
    private const string SystemName = "MongoDb";
    private MongoDbContainer? _container;
    private string? _connectionString;
    private MongoClient? _client;
    private IStoveEventEmitter? _emitter;

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
        try
        {
            await GetCollection<T>(collection).InsertOneAsync(document);
            Emit("Insert", collection, "ok");
        }
        catch (Exception ex) when (EmitFailure("Insert", collection, ex)) { }
        return this;
    }

    public async Task<MongoDbSystem> InsertManyAsync<T>(string collection, IEnumerable<T> documents)
    {
        try
        {
            var list = documents.ToList();
            await GetCollection<T>(collection).InsertManyAsync(list);
            Emit("InsertMany", collection, $"{list.Count} doc(s)");
        }
        catch (Exception ex) when (EmitFailure("InsertMany", collection, ex)) { }
        return this;
    }

    // --- Query ---

    public async Task<MongoDbSystem> ShouldFind<T>(
        string collection, Expression<Func<T, bool>> filter, Action<List<T>> validate)
    {
        try
        {
            var results = await GetCollection<T>(collection).Find(filter).ToListAsync();
            validate(results);
            Emit("ShouldFind", collection, $"{results.Count} doc(s)");
        }
        catch (Exception ex) when (EmitFailure("ShouldFind", collection, ex)) { }
        return this;
    }

    public async Task<MongoDbSystem> ShouldFindAll<T>(string collection, Action<List<T>> validate)
    {
        try
        {
            var results = await GetCollection<T>(collection).Find(_ => true).ToListAsync();
            validate(results);
            Emit("ShouldFindAll", collection, $"{results.Count} doc(s)");
        }
        catch (Exception ex) when (EmitFailure("ShouldFindAll", collection, ex)) { }
        return this;
    }

    public async Task<MongoDbSystem> ShouldExist<T>(string collection, Expression<Func<T, bool>> filter)
    {
        try
        {
            var count = await GetCollection<T>(collection).CountDocumentsAsync(filter);
            if (count == 0)
                throw new InvalidOperationException(
                    $"Expected a document matching the filter to exist in '{collection}', but none were found.");
            Emit("ShouldExist", collection, $"{count} doc(s)");
        }
        catch (Exception ex) when (EmitFailure("ShouldExist", collection, ex)) { }
        return this;
    }

    public async Task<MongoDbSystem> ShouldNotExist<T>(string collection, Expression<Func<T, bool>> filter)
    {
        try
        {
            var count = await GetCollection<T>(collection).CountDocumentsAsync(filter);
            if (count > 0)
                throw new InvalidOperationException(
                    $"Expected no documents matching the filter in '{collection}', but found {count}.");
            Emit("ShouldNotExist", collection, "0 doc(s)");
        }
        catch (Exception ex) when (EmitFailure("ShouldNotExist", collection, ex)) { }
        return this;
    }

    public async Task<MongoDbSystem> ShouldCount<T>(
        string collection, Expression<Func<T, bool>> filter, Action<long> validate)
    {
        try
        {
            var count = await GetCollection<T>(collection).CountDocumentsAsync(filter);
            validate(count);
            Emit("ShouldCount", collection, $"{count}");
        }
        catch (Exception ex) when (EmitFailure("ShouldCount", collection, ex)) { }
        return this;
    }

    // --- Delete ---

    public async Task<MongoDbSystem> DeleteAsync<T>(string collection, Expression<Func<T, bool>> filter)
    {
        try
        {
            var result = await GetCollection<T>(collection).DeleteManyAsync(filter);
            Emit("Delete", collection, $"{result.DeletedCount} deleted");
        }
        catch (Exception ex) when (EmitFailure("Delete", collection, ex)) { }
        return this;
    }

    public async Task<MongoDbSystem> DropCollectionAsync(string collection)
    {
        try
        {
            await Database.DropCollectionAsync(collection);
            Emit("DropCollection", collection, "ok");
        }
        catch (Exception ex) when (EmitFailure("DropCollection", collection, ex)) { }
        return this;
    }

    public async ValueTask DisposeAsync()
    {
        _client?.Dispose();
        if (_container != null) await _container.DisposeAsync();
    }

    private void Emit(string action, string? input, string? output)
        => _emitter?.Emit(new StoveEntry
        {
            TestId = _emitter.CurrentTestId, System = SystemName, Action = action,
            Result = EntryResult.Success, Input = input, Output = output
        });

    private bool EmitFailure(string action, string? input, Exception ex)
    {
        _emitter?.Emit(new StoveEntry
        {
            TestId = _emitter.CurrentTestId, System = SystemName, Action = action,
            Result = EntryResult.Failed, Input = input, Error = ex.Message
        });
        return false;
    }
}
