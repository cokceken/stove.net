using Stove.Net.Core;

namespace Stove.Net.PostgreSql;

/// <summary>
/// Extension methods to register and access the PostgreSQL system.
/// </summary>
public static class StovePostgreSqlExtensions
{
    extension(StoveBuilder builder)
    {
        /// <summary>
        /// Register a PostgreSQL system (with Testcontainers) in the Stove builder.
        /// </summary>
        public StoveBuilder WithPostgreSql(Action<PostgreSqlSystemOptions>? configure = null) =>
            builder.WithPostgreSql(SystemKey.DefaultName, configure);

        /// <summary>
        /// Register a named PostgreSQL system. Use when you need multiple database instances.
        /// </summary>
        public StoveBuilder WithPostgreSql(string name,
            Action<PostgreSqlSystemOptions>? configure = null)
        {
            var options = new PostgreSqlSystemOptions();
            configure?.Invoke(options);
            builder.WithSystem(new PostgreSqlSystem(options), name);
            return builder;
        }
    }


    extension(ValidationDsl dsl)
    {
        /// <summary>
        /// Access the default PostgreSQL system in a validation block.
        /// </summary>
        public async Task PostgreSql(Func<PostgreSqlSystem, Task> validation)
        {
            await validation(dsl.Get<PostgreSqlSystem>());
        }

        /// <summary>
        /// Access a named PostgreSQL system in a validation block.
        /// </summary>
        public async Task PostgreSql(string name, Func<PostgreSqlSystem, Task> validation)
        {
            await validation(dsl.Get<PostgreSqlSystem>(name));
        }
    }
}