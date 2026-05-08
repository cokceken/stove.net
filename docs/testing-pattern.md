# Stove.Net — Recommended Test Pattern

## Overview

Stove.Net is a .NET port of [Kotlin Stove](https://github.com/Trendyol/stove) for end-to-end testing with real containers. It combines `WebApplicationFactory` with [Testcontainers](https://dotnet.testcontainers.org/) so your tests run against actual PostgreSQL, WireMock, and other systems — no mocks, no fakes.

## Architecture

| Concept | Role |
|---|---|
| `StoveBuilder` | Fluent API to configure which systems (HTTP, PostgreSQL, WireMock, Tracing) the test suite needs. |
| `StoveInstance` | Orchestrates container lifecycle — start, configuration collection, cleanup, and disposal. |
| `ValidationDsl` | Provides test-time access to each system via `Validate(async s => { ... })`. |
| Systems | Pluggable components: `HttpClientSystem`, `PostgreSqlSystem`, `WireMockSystem`, `TracingSystem`. |

## Fixture Pattern

Use xUnit's `IClassFixture<T>` + `IAsyncLifetime` so containers start once per test class.

```csharp
// Setup/MyFixture.cs
public class MyFixture : IAsyncLifetime
{
    private WebApplicationFactory<Program>? _factory;
    private StoveInstance? _stove;

    // Optional: capture app logs in failure reports
    public StoveLogCapture LogCapture { get; } = new();

    public StoveInstance Stove => _stove ?? throw new InvalidOperationException("Not initialized");

    public async ValueTask InitializeAsync()
    {
        var builder = StoveBuilder.Create()
            .WithHttpClient()
            .WithPostgreSql(opts =>
            {
                opts.ConfigureExposedConfiguration = connectionString =>
                [
                    new KeyValuePair<string, string>("ConnectionStrings:DefaultConnection", connectionString)
                ];
            })
            .WithWireMock(opts =>
            {
                opts.ConfigureExposedConfiguration = url =>
                [
                    new KeyValuePair<string, string>("ExternalApis:NotificationUrl", url)
                ];
            });

        _stove = await builder.RunAsync();
        var stoveConfig = _stove.CollectConfiguration().ToList();

        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(webBuilder =>
            {
                webBuilder.ConfigureAppConfiguration((_, config) =>
                {
                    config.AddInMemoryCollection(stoveConfig!);
                });
                // Optional: capture SUT logs for failure reports
                webBuilder.ConfigureLogging(logging =>
                {
                    logging.AddProvider(LogCapture.CreateProvider());
                });
            });

        _stove.GetSystem<HttpClientSystem>().SetHttpClient(_factory.CreateClient());

        using var scope = _factory.Services.CreateScope();
        await _stove.NotifyAfterRunAsync(scope.ServiceProvider);
    }

    public async ValueTask DisposeAsync()
    {
        if (_factory != null) await _factory.DisposeAsync();
        if (_stove != null) await _stove.DisposeAsync();
    }
}
```

**Key steps:**

1. **Build** — register systems via `StoveBuilder`.
2. **Run** — `RunAsync()` starts containers and returns a `StoveInstance`.
3. **Collect config** — `CollectConfiguration()` gathers connection strings / URLs exposed by each system.
4. **Wire into SUT** — pass config to `WebApplicationFactory` via `AddInMemoryCollection`.
5. **Set HTTP client** — give the factory-created `HttpClient` to Stove's `HttpClientSystem`.
6. **Notify** — `NotifyAfterRunAsync` lets systems run post-startup hooks (migrations, seed data, etc.).

## Test Class Pattern

```csharp
public class OrderTests(MyFixture fixture) : IClassFixture<MyFixture>
{
    [Fact]
    public async Task Should_create_order()
    {
        await fixture.Stove.Validate(async s =>
        {
            await s.Http(async http =>
            {
                await http.PostAsync<Order>("/api/orders",
                    body: new CreateOrderRequest("Widget", 5),
                    validate: order => Assert.Equal("Confirmed", order.Status));
            });

            await s.PostgreSql(async pg =>
            {
                await pg.ShouldQuery("SELECT * FROM orders WHERE product_name = 'Widget'",
                    mapper: reader => new Order { /* ... */ },
                    validate: results => Assert.Single(results));
            });

            await s.WireMock(async wm =>
            {
                wm.ShouldHaveReceived("/api/notifications", "POST");
            });
        });
    }
}
```

## Failure Reports

`StoveReporter` is auto-registered and produces a console report on test failure containing:

| Section | What it shows |
|---|---|
| **Action timeline** | Chronological list of HTTP calls, DB queries, and WireMock stub matches. |
| **System snapshots** | Current DB state, registered WireMock stubs, and unmatched requests. |
| **Application logs** | SUT logs captured via `StoveLogCapture` (if configured in the fixture). |
| **Container logs** | Raw logs from Testcontainers for each running system. |

No extra setup is needed — just configure `StoveLogCapture` in the fixture if you want application-level logs included.

## Comparison with Kotlin Stove

| Kotlin Stove | Stove.Net | Notes |
|---|---|---|
| `runner { }` lambda | `WebApplicationFactory<Program>` | .NET's built-in in-process test host. |
| `--key=value` args | `AddInMemoryCollection(...)` | Configuration injected via `IConfiguration`. |
| `ExposesConfiguration` | `IExposesConfiguration` | Same concept — systems expose their runtime config. |
| `StoveKotestExtension` | `StoveReporter` (auto-registered) | Failure reporting; no manual registration needed in .NET. |
