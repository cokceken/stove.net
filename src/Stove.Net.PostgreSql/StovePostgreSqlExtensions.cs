using Stove.Net.Core;

namespace Stove.Net.PostgreSql;

/// <summary>
/// Extension methods to register and access the PostgreSQL system.
/// </summary>
public static class StovePostgreSqlExtensions
{
    /// <summary>
    /// Register a PostgreSQL system (with Testcontainers) in the Stove builder.
    /// </summary>
    public static StoveBuilder WithPostgreSql(
        this StoveBuilder builder,
        Action<PostgreSqlSystemOptions>? configure = null)
    {
        var options = new PostgreSqlSystemOptions();
        configure?.Invoke(options);
        builder.WithSystem(new PostgreSqlSystem(options));
        return builder;
    }

    /// <summary>
    /// Register a named PostgreSQL system. Use when you need multiple database instances.
    /// </summary>
    public static StoveBuilder WithPostgreSql(
        this StoveBuilder builder,
        string name,
        Action<PostgreSqlSystemOptions>? configure = null)
    {
        var options = new PostgreSqlSystemOptions();
        configure?.Invoke(options);
        builder.WithSystem(new PostgreSqlSystem(options), name);
        return builder;
    }

    /// <summary>
    /// Access the default PostgreSQL system in a validation block.
    /// </summary>
    public static async Task PostgreSql(this ValidationDsl dsl, Func<PostgreSqlSystem, Task> validation)
    {
        await validation(dsl.Get<PostgreSqlSystem>());
    }

    /// <summary>
    /// Access a named PostgreSQL system in a validation block.
    /// </summary>
    public static async Task PostgreSql(this ValidationDsl dsl, string name, Func<PostgreSqlSystem, Task> validation)
    {
        await validation(dsl.Get<PostgreSqlSystem>(name));
    }
}
