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
public class MongoDbSystem(MongoDbSystemOptions options) : IPluggedSystem, IExposesConfiguration
{
    private MongoDbContainer? _container;
    private string? _connectionString;
    private MongoClient? _client;

    /// <summary>
    /// The container's connection string, available after RunAsync().
    /// </summary>
    public string ConnectionString => _connectionString
                                      ?? throw new InvalidOperationException(
                                          "MongoDB container is not started yet.");

    /// <summary>
    /// The underlying MongoClient. Use for full access to MongoDB's driver API.
    /// Available after RunAsync().
    /// </summary>
    public MongoClient Client => _client
                                 ?? throw new InvalidOperationException(
                                     "MongoDB container is not started yet.");

    /// <summary>
    /// The default database configured via options. Available after RunAsync().
    /// </summary>
    public IMongoDatabase Database => Client.GetDatabase(options.DatabaseName);

    public async Task RunAsync()
    {
        _container = new MongoDbBuilder(new DockerImage("mongo:7"))
            .Build();

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
        {
            return
            [
                new KeyValuePair<string, string>("MongoDb:ConnectionString", _connectionString)
            ];
        }

        return [];
    }

    private IMongoCollection<T> GetCollection<T>(string collection) =>
        Database.GetCollection<T>(collection);

    // --- Insert ---

    /// <summary>
    /// Insert a single document into a collection.
    /// </summary>
    public async Task<MongoDbSystem> InsertAsync<T>(string collection, T document)
    {
        await GetCollection<T>(collection).InsertOneAsync(document);
        return this;
    }

    /// <summary>
    /// Insert multiple documents into a collection.
    /// </summary>
    public async Task<MongoDbSystem> InsertManyAsync<T>(string collection, IEnumerable<T> documents)
    {
        await GetCollection<T>(collection).InsertManyAsync(documents);
        return this;
    }

    // --- Query ---

    /// <summary>
    /// Find documents matching a filter and validate them.
    /// </summary>
    public async Task<MongoDbSystem> ShouldFind<T>(
        string collection,
        Expression<Func<T, bool>> filter,
        Action<List<T>> validate)
    {
        var results = await GetCollection<T>(collection)
            .Find(filter)
            .ToListAsync();

        validate(results);
        return this;
    }

    /// <summary>
    /// Find all documents in a collection and validate them.
    /// </summary>
    public async Task<MongoDbSystem> ShouldFindAll<T>(
        string collection,
        Action<List<T>> validate)
    {
        var results = await GetCollection<T>(collection)
            .Find(_ => true)
            .ToListAsync();

        validate(results);
        return this;
    }

    /// <summary>
    /// Assert that a document matching the filter exists in the collection.
    /// </summary>
    public async Task<MongoDbSystem> ShouldExist<T>(
        string collection,
        Expression<Func<T, bool>> filter)
    {
        var count = await GetCollection<T>(collection).CountDocumentsAsync(filter);
        if (count == 0)
            throw new InvalidOperationException(
                $"Expected a document matching the filter to exist in '{collection}', but none were found.");
        return this;
    }

    /// <summary>
    /// Assert that no document matching the filter exists in the collection.
    /// </summary>
    public async Task<MongoDbSystem> ShouldNotExist<T>(
        string collection,
        Expression<Func<T, bool>> filter)
    {
        var count = await GetCollection<T>(collection).CountDocumentsAsync(filter);
        if (count > 0)
            throw new InvalidOperationException(
                $"Expected no documents matching the filter in '{collection}', but found {count}.");
        return this;
    }

    /// <summary>
    /// Count documents matching a filter and validate the count.
    /// </summary>
    public async Task<MongoDbSystem> ShouldCount<T>(
        string collection,
        Expression<Func<T, bool>> filter,
        Action<long> validate)
    {
        var count = await GetCollection<T>(collection).CountDocumentsAsync(filter);
        validate(count);
        return this;
    }

    // --- Delete ---

    /// <summary>
    /// Delete documents matching a filter.
    /// </summary>
    public async Task<MongoDbSystem> DeleteAsync<T>(
        string collection,
        Expression<Func<T, bool>> filter)
    {
        await GetCollection<T>(collection).DeleteManyAsync(filter);
        return this;
    }

    /// <summary>
    /// Drop an entire collection.
    /// </summary>
    public async Task<MongoDbSystem> DropCollectionAsync(string collection)
    {
        await Database.DropCollectionAsync(collection);
        return this;
    }

    public async ValueTask DisposeAsync()
    {
        _client?.Dispose();

        if (_container != null)
            await _container.DisposeAsync();
    }
}
