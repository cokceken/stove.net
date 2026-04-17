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

        var system = new PostgreSqlSystem(options);
        builder.WithSystem(system);
        return builder;
    }

    /// <summary>
    /// Access the PostgreSQL system in a validation block.
    /// </summary>
    public static async Task PostgreSql(
        this ValidationDsl dsl,
        Func<PostgreSqlSystem, Task> validation)
    {
        var system = dsl.Get<PostgreSqlSystem>();
        await validation(system);
    }
}
