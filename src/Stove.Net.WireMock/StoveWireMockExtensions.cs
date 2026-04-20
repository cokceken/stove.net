using Stove.Net.Core;

namespace Stove.Net.WireMock;

/// <summary>
/// Extension methods to register and access the WireMock system.
/// </summary>
public static class StoveWireMockExtensions
{
    extension(StoveBuilder builder)
    {
        /// <summary>
        /// Register a WireMock system (in-process HTTP mock server) in the Stove builder.
        /// </summary>
        public StoveBuilder WithWireMock(Action<WireMockSystemOptions>? configure = null) =>
            builder.WithWireMock(SystemKey.DefaultName, configure);

        /// <summary>
        /// Register a named WireMock system. Use when you need to mock multiple external services.
        /// </summary>
        public StoveBuilder WithWireMock(string name,
            Action<WireMockSystemOptions>? configure = null)
        {
            var options = new WireMockSystemOptions();
            configure?.Invoke(options);
            builder.WithSystem(new WireMockSystem(options), name);
            return builder;
        }
    }

    extension(ValidationDsl dsl)
    {
        /// <summary>
        /// Access the default WireMock system in a validation block.
        /// </summary>
        public async Task WireMock(Func<WireMockSystem, Task> validation)
        {
            await validation(dsl.Get<WireMockSystem>());
        }

        /// <summary>
        /// Access a named WireMock system in a validation block.
        /// </summary>
        public async Task WireMock(string name, Func<WireMockSystem, Task> validation)
        {
            await validation(dsl.Get<WireMockSystem>(name));
        }
    }
}