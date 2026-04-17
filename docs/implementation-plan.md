# Stove.Net — Implementation Plan

## Problem Statement

Create a .NET port of [Trendyol/stove](https://github.com/Trendyol/stove), a Kotlin e2e testing framework that orchestrates Testcontainers with a fluent DSL. The goal is to enable .NET teams (including QA) to write and maintain end-to-end component tests with behavior-descriptive, chainable extension methods — without needing to maintain a separate Kotlin codebase.

## Design Decisions (Confirmed)

| Decision | Choice |
|---|---|
| Test framework | xUnit v3 (IAsyncLifetime, MTP runner, ValueTask) |
| App hosting | WebApplicationFactory\<T\> in-process (container adapter later) |
| Initial components | PostgreSQL + HTTP client |
| Assertions | Framework-agnostic — NO FluentAssertions; xUnit built-in Assert.* or users bring their own |
| Package structure | Multi-package from v0.1 (Core, PostgreSql, Http, Xunit) |
| .NET version | .NET 10 (current project target) |
| HTTP API style | All verbs return `HttpClientSystem` for chaining; data extraction via validate callback closures |
| HttpClient wiring | Explicit — users call `SetHttpClient()` themselves, no magic auto-injection |
| xUnit version | v3 only — no legacy v2 support |

## Architecture Overview

Stove.Net mirrors the original Stove's plugin architecture:

```
Stove.Net.Core          — Orchestrator, PluggedSystem interface, DSL base, lifecycle
Stove.Net.Http          — HttpClientSystem (wraps HttpClient from WebApplicationFactory)
Stove.Net.PostgreSql    — PostgreSqlSystem (wraps Testcontainers.PostgreSql)
Stove.Net.Xunit         — xUnit integration (IAsyncLifetime fixture base)
```

### Core Abstractions (in Stove.Net.Core)

```csharp
// The central orchestrator — holds registered systems, manages lifecycle
public class Stove : IAsyncDisposable { ... }

// Every component (Postgres, HTTP, Kafka, etc.) implements this
public interface IPluggedSystem : IAsyncDisposable
{
    Task RunAsync();           // Start containers / clients
    Task CleanupAsync();       // Between-test cleanup
}

// Systems that expose config to the app (e.g., connection strings)
public interface IExposesConfiguration
{
    IEnumerable<KeyValuePair<string, string>> Configuration();
}

// Systems that need access to the app's DI container after startup
public interface IAfterRunAware
{
    Task AfterRunAsync(IServiceProvider serviceProvider);
}
```

### Target End-User DSL

```csharp
// === SETUP (once per test suite in a shared fixture) ===
public class MyFixture : StoveFixture<Program>
{
    protected override StoveBuilder Configure(StoveBuilder builder)
        => builder
            .WithHttpClient()
            .WithPostgreSql(opts =>
            {
                opts.ConfigureExposedConfiguration = cs => new[]
                {
                    new KeyValuePair<string, string>("ConnectionStrings:Default", cs)
                };
            });

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        Stove.GetSystem<HttpClientSystem>().SetHttpClient(CreateClient());
    }
}

// === TEST ===
[Fact]
public async Task Should_create_order()
{
    await _fixture.Stove.Validate(async s =>
    {
        await s.Http(async http =>
        {
            await http.PostAsync<Order>("/orders", createOrderRequest, order =>
            {
                Assert.NotNull(order);
            });
        });

        await s.PostgreSql(async pg =>
        {
            await pg.ShouldQuery<Order>(
                "SELECT * FROM orders WHERE user_id = @userId",
                new { userId },
                results => { Assert.Single(results); }
            );
        });
    });
}
```

## Versioning & Milestones

### v0.1 — Foundation + Core DSL ✅ DONE
> Goal: A working e2e test that boots an ASP.NET app, starts a Postgres container, and validates an HTTP call + DB state.

- ✅ **Stove.Net.Core** — Stove orchestrator, IPluggedSystem, lifecycle management, builder DSL
- ✅ **Stove.Net.Http** — HTTP system wrapping HttpClient (GET/POST/PUT/DELETE/PATCH with typed + raw overloads, all chainable)
- ✅ **Stove.Net.PostgreSql** — PostgreSQL system using Testcontainers.PostgreSql (ShouldQuery, ShouldExecute, ShouldQueryScalar)
- ✅ **Stove.Net.Xunit** — StoveFixture base class implementing IAsyncLifetime (xUnit v3, ValueTask)
- ✅ **Test projects** — Per-system smoke tests (Http, PostgreSql) + full integration tests (Http+PostgreSql)
- ✅ **Example app** — Minimal ASP.NET + EF Core Orders API

### v0.2 — Polish & DI Bridge ✅ DONE
> Goal: Enable `using<TService>()` pattern and cleanup/migration support.
> Note: Most of v0.2 is covered by WebApplicationFactory's built-in DI access.

- ✅ DI bridge — `StoveFixture.Services` exposes the app's `IServiceProvider` directly; no separate `Using<T>()` needed
- ✅ Migration support — `PostgreSqlSystemOptions.MigrationSql` for raw SQL; fixtures can call `db.Database.EnsureCreatedAsync()`
- ✅ Cleanup hooks — `IPluggedSystem.CleanupAsync()` + `PostgreSqlSystemOptions.Cleanup` callback
- ✅ Configuration exposure — `IExposesConfiguration` + `CollectConfiguration()` auto-injects container connection strings into app config
- ✅ Web host customization — `ConfigureWebHost(IWebHostBuilder)` virtual hook for service replacement (e.g., swap DB provider)

### v0.3 — More Components (In Progress)
> Goal: Expand supported infrastructure.

- ✅ **Stove.Net.Kafka** — Publish/consume Kafka messages (Testcontainers.Kafka + Confluent.Kafka)
- ✅ **Stove.Net.Redis** — Redis assertions (Testcontainers.Redis + StackExchange.Redis)
- **Stove.Net.WireMock** — External API mocking via WireMock.Net
- **Stove.Net.MongoDb** — MongoDB support

### v0.4 — Observability & DX
> Goal: Make test failures easy to diagnose.

- Execution timeline/report on failure (like Stove's STOVE TEST EXECUTION REPORT)
- Structured logging of all DSL operations
- Better error messages when systems aren't registered

### v0.5 — Advanced Hosting
> Goal: Support running the app-under-test as a Docker container.

- Container runner adapter (alternative to WebApplicationFactory)
- Black-box testing mode (HTTP-only, no DI access)

### Future
- Dashboard (port of Stove Dashboard)
- NUnit / MSTest adapters
- gRPC system
- Elasticsearch, Couchbase, MSSQL systems
- NuGet publishing & CI/CD
- Documentation site

## Implementation Details for v0.1

### Solution Structure (Current)

```
Stove.Net.sln
├── docs/
│   └── implementation-plan.md
├── src/
│   ├── Stove.Net.Core/
│   │   ├── StoveInstance.cs                — Central orchestrator
│   │   ├── StoveBuilder.cs                 — Fluent builder (.WithXxx methods)
│   │   ├── IPluggedSystem.cs               — System interface
│   │   ├── IExposesConfiguration.cs        — Config exposure interface
│   │   ├── IAfterRunAware.cs               — Post-startup hook interface
│   │   ├── ValidationDsl.cs                — The stove.Validate() entry point
│   │   └── Exceptions/
│   │       └── SystemNotRegisteredException.cs
│   ├── Stove.Net.Http/
│   │   ├── HttpClientSystem.cs             — HTTP verbs (GET/POST/PUT/DELETE/PATCH, raw + typed)
│   │   └── StoveHttpExtensions.cs          — .WithHttpClient() + .Http() extensions
│   ├── Stove.Net.PostgreSql/
│   │   ├── PostgreSqlSystem.cs             — DB query/execute assertions
│   │   ├── PostgreSqlSystemOptions.cs      — Configuration options
│   │   └── StovePostgreSqlExtensions.cs    — .WithPostgreSql() + .PostgreSql() extensions
│   └── Stove.Net.Xunit/
│       └── StoveFixture.cs                 — IAsyncLifetime + WebApplicationFactory + CreateClient()
├── tests/
│   ├── Stove.Net.Tests.ExampleApp/         — Minimal ASP.NET API + EF Core
│   │   ├── Program.cs
│   │   ├── OrdersController.cs
│   │   ├── Order.cs, CreateOrderRequest.cs, AppDbContext.cs
│   ├── Stove.Net.Tests.Http/               — HTTP-only smoke tests (InMemory DB, no containers)
│   │   ├── Setup/HttpOnlyFixture.cs
│   │   └── Tests/HttpTests.cs
│   ├── Stove.Net.Tests.PostgreSql/         — PostgreSQL-only smoke tests (container, no app)
│   │   ├── Setup/PostgreSqlOnlyFixture.cs
│   │   └── Tests/PostgreSqlTests.cs
│   └── Stove.Net.Tests.Integration/        — Full e2e tests (HTTP + PostgreSQL container)
│       ├── Setup/IntegrationFixture.cs
│       └── Tests/OrderTests.cs
```

### Key NuGet Dependencies

| Package | Project | Purpose |
|---|---|---|
| `Testcontainers.PostgreSql` 4.11.0 | Stove.Net.PostgreSql | Postgres container |
| `Npgsql` 10.0.2 | Stove.Net.PostgreSql | DB queries |
| `Microsoft.AspNetCore.Mvc.Testing` 10.0.6 | Stove.Net.Xunit | WebApplicationFactory |
| `xunit.v3.extensibility.core` 3.2.2 | Stove.Net.Xunit | xUnit v3 integration (library) |
| `xunit.v3` 3.2.2 | Test projects | xUnit v3 runner (test exes) |
| `xunit.runner.visualstudio` 3.1.5 | Test projects | Rider/VS test discovery |

### Chainable Extension Method Pattern

Every system method returns the system itself for chaining:

```csharp
await s.Http(async http =>
{
    await http
        .PostAsync<Order>("/orders", body, order => Assert.NotNull(order))
        .Result  // Task<HttpClientSystem>
        ;
    // Or step-by-step:
    await http.PostAsync("/orders", body);
    await http.GetAsync<Order>("/orders/1", order => Assert.Equal("CONFIRMED", order.Status));
    await http.DeleteAsync("/orders/1");
});
```

### How It Maps to Stove Kotlin

| Stove (Kotlin) | Stove.Net (C#) |
|---|---|
| `Stove().with { }` | `Stove.Create().WithXxx().RunAsync()` |
| `PluggedSystem` | `IPluggedSystem` |
| `ExposesConfiguration` | `IExposesConfiguration` |
| `AfterRunAwareWithContext<T>` | `IAfterRunAware` (uses IServiceProvider) |
| `@StoveDsl` extensions | Extension methods on StoveBuilder |
| `stove { }` validation block | `stove.Validate(async s => { })` |
| `postgresql { shouldQuery<T>() }` | `s.PostgreSql(async pg => pg.ShouldQuery<T>())` |
| `http { get<T>() }` | `s.Http(async http => http.GetAsync<T>())` |
| `using<TService> { }` | `fixture.Services.GetRequiredService<T>()` |
| `bridge()` | Built into WebApplicationFactory integration |

## Notes & Considerations

1. **Attribution**: README and NuGet metadata should credit Trendyol/stove as the original inspiration, linking to the Kotlin repo.
2. **Naming**: "Stove.Net" mirrors the .NET convention. NuGet package IDs: `Stove.Net.Core`, `Stove.Net.PostgreSql`, etc.
3. **C# vs Kotlin DSL gap**: Kotlin has receiver lambdas and infix functions that make DSLs very natural. C# compensates with extension methods, lambda callbacks, and the builder pattern. The DSL won't be identical but should feel idiomatic to C# developers.
4. **Async-first**: All operations are async (Task-based) since container startup, HTTP calls, and DB queries are inherently async.
5. **QA-friendly**: The fluent chainable API and behavior-descriptive method names (`ShouldQuery`, `PostAsync<T>`) are designed to be readable by QA without deep C# knowledge.
6. **Testcontainers.DotNet**: Already mature with PostgreSQL, Kafka, Redis, MongoDB modules — we wrap rather than reinvent.
