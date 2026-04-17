# Stove.Net — Implementation Plan

## Problem Statement

Create a .NET port of [Trendyol/stove](https://github.com/Trendyol/stove), a Kotlin e2e testing framework that orchestrates Testcontainers with a fluent DSL. The goal is to enable .NET teams (including QA) to write and maintain end-to-end component tests with behavior-descriptive, chainable extension methods — without needing to maintain a separate Kotlin codebase.

## Design Decisions (Confirmed)

| Decision | Choice |
|---|---|
| Test framework | xUnit (IAsyncLifetime for lifecycle) |
| App hosting | WebApplicationFactory\<T\> in-process (container adapter later) |
| Initial components | PostgreSQL + HTTP client |
| Assertions | Framework-agnostic (xUnit built-in Assert, or users bring Shouldly, etc.) |
| Package structure | Multi-package from v0.1 (Core, PostgreSql, Http) |
| .NET version | .NET 10 (current project target) |

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
await Stove.Create()
    .WithHttpClient(opts => opts.BaseUrl = "http://localhost:5000")
    .WithPostgreSql(opts => {
        opts.Cleanup = async db => await db.ExecuteAsync("TRUNCATE orders, users");
    })
    .WithWebApplication<Program>()
    .RunAsync();

// === TEST ===
[Fact]
public async Task Should_create_order()
{
    await stove.Validate(async s =>
    {
        await s.Http(async http =>
        {
            await http.PostAndExpect<Order>("/orders", createOrderRequest, response =>
            {
                Assert.Equal(HttpStatusCode.Created, response.StatusCode);
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

### v0.1 — Foundation + Core DSL
> Goal: A working e2e test that boots an ASP.NET app, starts a Postgres container, and validates an HTTP call + DB state.

- **Stove.Net.Core** — Stove orchestrator, IPluggedSystem, lifecycle management, builder DSL
- **Stove.Net.Http** — HTTP system wrapping HttpClient (GET/POST/PUT/DELETE with typed responses)
- **Stove.Net.PostgreSql** — PostgreSQL system using Testcontainers.PostgreSql (shouldQuery, shouldExecute)
- **Stove.Net.Xunit** — StoveFixture base class implementing IAsyncLifetime
- **Example project** — A minimal ASP.NET + EF Core app with a Stove.Net test suite

### v0.2 — Polish & DI Bridge
> Goal: Enable `using<TService>()` pattern and cleanup/migration support.

- Bridge system — access app's DI container from tests (`using<TService>(svc => ...)`)
- Migration support — run SQL/EF migrations before tests
- Cleanup hooks — per-test and per-suite cleanup strategies
- Configuration exposure — auto-inject container connection strings into app config

### v0.3 — More Components
> Goal: Expand supported infrastructure.

- **Stove.Net.Kafka** — Publish/consume Kafka messages (Testcontainers.Kafka)
- **Stove.Net.Redis** — Redis assertions (Testcontainers.Redis)
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

### Solution Structure

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
│   │   ├── HttpClientSystem.cs             — HTTP operations (get, post, put, delete)
│   │   ├── HttpClientSystemOptions.cs      — Configuration options
│   │   └── StoveHttpExtensions.cs          — .WithHttpClient() + .Http() extensions
│   ├── Stove.Net.PostgreSql/
│   │   ├── PostgreSqlSystem.cs             — DB query/execute assertions
│   │   ├── PostgreSqlSystemOptions.cs      — Configuration options
│   │   └── StovePostgreSqlExtensions.cs    — .WithPostgreSql() + .PostgreSql() extensions
│   └── Stove.Net.Xunit/
│       └── StoveFixture.cs                 — IAsyncLifetime + WebApplicationFactory integration
├── tests/
│   ├── Stove.Net.Tests.ExampleApp/         — Minimal ASP.NET API + EF Core (separate project)
│   │   ├── Program.cs
│   │   ├── OrdersController.cs
│   │   ├── Order.cs
│   │   ├── CreateOrderRequest.cs
│   │   └── AppDbContext.cs
│   └── Stove.Net.Tests.Example/            — E2E test project
│       ├── Setup/
│       │   └── ExampleStoveFixture.cs      — Fixture configuration
│       └── Tests/
│           └── OrderTests.cs               — Example e2e tests
```

### Key NuGet Dependencies

| Package | Project | Purpose |
|---|---|---|
| `Testcontainers.PostgreSql` | Stove.Net.PostgreSql | Postgres container |
| `Npgsql` | Stove.Net.PostgreSql | DB queries |
| `Microsoft.AspNetCore.Mvc.Testing` | Stove.Net.Xunit | WebApplicationFactory |
| `xunit` | Stove.Net.Xunit | Test framework integration |

### Chainable Extension Method Pattern

Every system method returns the system itself for chaining:

```csharp
await s.Http(async http =>
{
    await http
        .Post("/orders", body)
        .Get<Order>("/orders/1", order => Assert.Equal("CONFIRMED", order.Status))
        .Delete("/orders/1");
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
| `http { get<T>() }` | `s.Http(async http => http.Get<T>())` |
| `using<TService> { }` | `s.Using<TService>(svc => { })` (v0.2) |
| `bridge()` | Built into WebApplicationFactory integration |

## Notes & Considerations

1. **Attribution**: README and NuGet metadata should credit Trendyol/stove as the original inspiration, linking to the Kotlin repo.
2. **Naming**: "Stove.Net" mirrors the .NET convention. NuGet package IDs: `Stove.Net.Core`, `Stove.Net.PostgreSql`, etc.
3. **C# vs Kotlin DSL gap**: Kotlin has receiver lambdas and infix functions that make DSLs very natural. C# compensates with extension methods, lambda callbacks, and the builder pattern. The DSL won't be identical but should feel idiomatic to C# developers.
4. **Async-first**: All operations are async (Task-based) since container startup, HTTP calls, and DB queries are inherently async.
5. **QA-friendly**: The fluent chainable API and behavior-descriptive method names (`ShouldQuery`, `PostAndExpect`) are designed to be readable by QA without deep C# knowledge.
6. **Testcontainers.DotNet**: Already mature with PostgreSQL, Kafka, Redis, MongoDB modules — we wrap rather than reinvent.
