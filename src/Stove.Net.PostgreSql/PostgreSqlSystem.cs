using System.Text.Json;
using DotNet.Testcontainers.Images;
using Npgsql;
using Stove.Net.Core;
using Stove.Net.Core.Reporting;
using Testcontainers.PostgreSql;

namespace Stove.Net.PostgreSql;

/// <summary>
/// PostgreSQL system using Testcontainers. Manages a PostgreSQL container
/// and provides query/execute assertion methods.
/// </summary>
public class PostgreSqlSystem(PostgreSqlSystemOptions options)
    : IPluggedSystem, IExposesConfiguration, IStoveReportingSystem, IReportsState, ICollectsLogs
{
    private const string SystemName = "PostgreSql";
    private PostgreSqlContainer? _container;
    private string? _connectionString;
    private IStoveEventEmitter? _emitter;
    private int _operationCount;
    private int _failedCount;

    public void SetEmitter(IStoveEventEmitter emitter) => _emitter = emitter;

    /// <summary>The container's connection string, available after RunAsync().</summary>
    public string ConnectionString => _connectionString
                                      ?? throw new InvalidOperationException(
                                          "PostgreSQL container is not started yet.");

    public async Task RunAsync()
    {
        _container = new PostgreSqlBuilder(new DockerImage("postgres:16-alpine"))
            .WithDatabase("stove_test")
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
            return [new KeyValuePair<string, string>("ConnectionStrings:DefaultConnection", _connectionString)];
        }

        return [];
    }

    // --- Assertion methods ---

    /// <summary>Execute a query and validate the results using a mapper and assertion callback.</summary>
    public async Task<PostgreSqlSystem> ShouldQuery<T>(
        string sql,
        Func<NpgsqlDataReader, T> mapper,
        Action<List<T>> validate,
        object? parameters = null)
    {
        try
        {
            await using var conn = new NpgsqlConnection(ConnectionString);
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand(sql, conn);
            if (parameters != null) AddParameters(cmd, parameters);

            await using var reader = await cmd.ExecuteReaderAsync();
            var results = new List<T>();
            while (await reader.ReadAsync()) results.Add(mapper(reader));

            validate(results);
            Emit("ShouldQuery", sql, $"{results.Count} row(s)");
        }
        catch (Exception ex)
        {
            EmitFailure("ShouldQuery", sql, ex);
            throw;
        }

        return this;
    }

    /// <summary>Execute a SQL statement and optionally validate the number of affected rows.</summary>
    public async Task<PostgreSqlSystem> ShouldExecute(
        string sql,
        Action<int>? validateAffectedRows = null,
        object? parameters = null)
    {
        try
        {
            await using var conn = new NpgsqlConnection(ConnectionString);
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand(sql, conn);
            if (parameters != null) AddParameters(cmd, parameters);

            var affected = await cmd.ExecuteNonQueryAsync();
            validateAffectedRows?.Invoke(affected);
            Emit("ShouldExecute", sql, $"{affected} row(s) affected");
        }
        catch (Exception ex)
        {
            EmitFailure("ShouldExecute", sql, ex);
            throw;
        }

        return this;
    }

    /// <summary>Execute a query and validate the scalar result.</summary>
    public async Task<PostgreSqlSystem> ShouldQueryScalar<T>(
        string sql,
        Action<T?> validate,
        object? parameters = null)
    {
        try
        {
            await using var conn = new NpgsqlConnection(ConnectionString);
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand(sql, conn);
            if (parameters != null) AddParameters(cmd, parameters);

            var result = await cmd.ExecuteScalarAsync();
            var typed = result is T t ? t : default;
            validate(typed);
            Emit("ShouldQueryScalar", sql, typed?.ToString());
        }
        catch (Exception ex)
        {
            EmitFailure("ShouldQueryScalar", sql, ex);
            throw;
        }

        return this;
    }

    private static void AddParameters(NpgsqlCommand cmd, object parameters)
    {
        foreach (var prop in parameters.GetType().GetProperties())
            cmd.Parameters.AddWithValue($"@{prop.Name}", prop.GetValue(parameters) ?? DBNull.Value);
    }

    // --- Fault Injection ---

    /// <summary>
    /// Simulate a slow query by executing pg_sleep inside the database.
    /// </summary>
    public async Task<PostgreSqlSystem> SimulateSlowQuery(TimeSpan duration)
    {
        await using var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand($"SELECT pg_sleep({duration.TotalSeconds})", conn);
        cmd.CommandTimeout = (int)duration.TotalSeconds + 30;
        await cmd.ExecuteNonQueryAsync();
        return this;
    }

    /// <summary>Toggle read-only mode on the test database.</summary>
    public async Task<PostgreSqlSystem> SetReadOnly(bool readOnly)
    {
        var builder = new NpgsqlConnectionStringBuilder(ConnectionString);
        var dbName = builder.Database;

        builder.Database = "postgres";
        await using var conn = new NpgsqlConnection(builder.ConnectionString);
        await conn.OpenAsync();

        var mode = readOnly ? "on" : "off";
        await using var cmd = new NpgsqlCommand(
            $"ALTER DATABASE \"{dbName}\" SET default_transaction_read_only = {mode}", conn);
        await cmd.ExecuteNonQueryAsync();
        NpgsqlConnection.ClearAllPools();
        return this;
    }

    // --- ICollectsLogs ---

    public async Task<IReadOnlyList<ContainerLogEntry>> GetLogsSinceAsync(DateTimeOffset since)
    {
        if (_container == null) return [];

        var (stdout, stderr) = await _container.GetLogsAsync(since.UtcDateTime);

        var entries = new List<ContainerLogEntry>();
        var containerId = _container.Id[..12];

        if (!string.IsNullOrWhiteSpace(stdout))
        {
            foreach (var line in stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                entries.Add(new ContainerLogEntry(
                    DateTimeOffset.UtcNow, SystemName, containerId,
                    line.Trim(), "stdout"));
            }
        }

        if (!string.IsNullOrWhiteSpace(stderr))
        {
            foreach (var line in stderr.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                entries.Add(new ContainerLogEntry(
                    DateTimeOffset.UtcNow, SystemName, containerId,
                    line.Trim(), "stderr"));
            }
        }

        return entries;
    }

    public async ValueTask DisposeAsync()
    {
        if (_container != null)
            await _container.DisposeAsync();
    }

    // --- Emit helpers ---

    public StoveSnapshot Report() => new()
    {
        System = SystemName,
        StateJson = JsonSerializer.Serialize(new { operationCount = _operationCount, failedCount = _failedCount }),
        Summary = $"{_operationCount} operation(s), {_failedCount} failed"
    };

    private void Emit(string action, string? input, string? output)
    {
        Interlocked.Increment(ref _operationCount);
        if (_emitter == null) return;
        var metadata = new Dictionary<string, string> { ["db.system"] = "postgresql" };
        if (input != null) metadata["db.statement"] = input.Length > 200 ? input[..200] + "…" : input;
        _emitter.ReportSuccess(SystemName, action, input: input, output: output, metadata: metadata);
    }

    private void EmitFailure(string action, string? input, Exception ex)
    {
        Interlocked.Increment(ref _failedCount);
        if (_emitter == null) return;
        var metadata = new Dictionary<string, string> { ["db.system"] = "postgresql" };
        if (input != null) metadata["db.statement"] = input.Length > 200 ? input[..200] + "…" : input;
        _emitter.ReportFailure(SystemName, action, ex, input: input, metadata: metadata);
    }
}