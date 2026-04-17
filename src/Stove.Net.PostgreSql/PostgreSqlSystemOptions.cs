using Npgsql;

namespace Stove.Net.PostgreSql;

/// <summary>
/// Configuration options for the PostgreSQL system.
/// </summary>
public class PostgreSqlSystemOptions
{
    /// <summary>
    /// Optional cleanup action to run between tests (e.g., truncate tables).
    /// Receives an NpgsqlConnection.
    /// </summary>
    public Func<NpgsqlConnection, Task>? Cleanup { get; set; }

    /// <summary>
    /// Maps the exposed container configuration to application config keys.
    /// Receives the container connection string and returns key-value pairs
    /// to inject into the application's configuration.
    /// </summary>
    public Func<string, IEnumerable<KeyValuePair<string, string>>>? ConfigureExposedConfiguration { get; set; }

    /// <summary>
    /// Optional SQL statements to run after the container starts (e.g., CREATE TABLE).
    /// </summary>
    public List<string> MigrationSql { get; } = new();

    /// <summary>
    /// Add migration SQL to execute after container startup.
    /// </summary>
    public PostgreSqlSystemOptions WithMigration(string sql)
    {
        MigrationSql.Add(sql);
        return this;
    }
}
