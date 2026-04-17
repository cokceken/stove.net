using DotNet.Testcontainers.Images;
using Npgsql;
using Stove.Net.Core;
using Testcontainers.PostgreSql;

namespace Stove.Net.PostgreSql;

/// <summary>
/// PostgreSQL system using Testcontainers. Manages a PostgreSQL container
/// and provides query/execute assertion methods.
/// </summary>
public class PostgreSqlSystem(PostgreSqlSystemOptions options) : IPluggedSystem, IExposesConfiguration
{
    private PostgreSqlContainer? _container;
    private string? _connectionString;

    /// <summary>
    /// The container's connection string, available after RunAsync().
    /// </summary>
    public string ConnectionString => _connectionString
                                      ?? throw new InvalidOperationException(
                                          "PostgreSQL container is not started yet.");

    public async Task RunAsync()
    {
        _container = new PostgreSqlBuilder(new DockerImage("postgres:16-alpine"))
            .Build();

        await _container.StartAsync();
        _connectionString = _container.GetConnectionString();

        if (options.MigrationSql.Count > 0)
        {
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();

            foreach (var sql in options.MigrationSql)
            {
                await using var cmd = new NpgsqlCommand(sql, conn);
                await cmd.ExecuteNonQueryAsync();
            }
        }
    }

    public async Task CleanupAsync()
    {
        if (options.Cleanup != null && _connectionString != null)
        {
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();
            await options.Cleanup(conn);
        }
    }

    public IEnumerable<KeyValuePair<string, string>> Configuration()
    {
        if (options.ConfigureExposedConfiguration != null && _connectionString != null)
            return options.ConfigureExposedConfiguration(_connectionString);

        if (_connectionString != null)
        {
            return new[]
            {
                new KeyValuePair<string, string>("ConnectionStrings:DefaultConnection", _connectionString)
            };
        }

        return Enumerable.Empty<KeyValuePair<string, string>>();
    }

    // --- Assertion methods ---

    /// <summary>
    /// Execute a query and validate the results using a mapper and assertion callback.
    /// </summary>
    public async Task<PostgreSqlSystem> ShouldQuery<T>(
        string sql,
        Func<NpgsqlDataReader, T> mapper,
        Action<List<T>> validate,
        object? parameters = null)
    {
        await using var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand(sql, conn);
        if (parameters != null)
            AddParameters(cmd, parameters);

        await using var reader = await cmd.ExecuteReaderAsync();
        var results = new List<T>();

        while (await reader.ReadAsync())
            results.Add(mapper(reader));

        validate(results);
        return this;
    }

    /// <summary>
    /// Execute a SQL statement and optionally validate the number of affected rows.
    /// </summary>
    public async Task<PostgreSqlSystem> ShouldExecute(
        string sql,
        Action<int>? validateAffectedRows = null,
        object? parameters = null)
    {
        await using var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand(sql, conn);
        if (parameters != null)
            AddParameters(cmd, parameters);

        var affected = await cmd.ExecuteNonQueryAsync();
        validateAffectedRows?.Invoke(affected);

        return this;
    }

    /// <summary>
    /// Execute a query and validate the scalar result.
    /// </summary>
    public async Task<PostgreSqlSystem> ShouldQueryScalar<T>(
        string sql,
        Action<T?> validate,
        object? parameters = null)
    {
        await using var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand(sql, conn);
        if (parameters != null)
            AddParameters(cmd, parameters);

        var result = await cmd.ExecuteScalarAsync();
        validate(result is T typed ? typed : default);

        return this;
    }

    private static void AddParameters(NpgsqlCommand cmd, object parameters)
    {
        foreach (var prop in parameters.GetType().GetProperties())
        {
            cmd.Parameters.AddWithValue($"@{prop.Name}", prop.GetValue(parameters) ?? DBNull.Value);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_container != null)
            await _container.DisposeAsync();
    }
}