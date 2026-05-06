# Testing Framework Approach

## Current State (Stove.Net)

Stove.Net is currently **coupled to xUnit v3** through the `Stove.Net.Xunit` project, which provides:

- **`StoveFixture<TProgram>`**: Extends `WebApplicationFactory<TProgram>` + implements xUnit's `IAsyncLifetime`. Boots the ASP.NET app in-process, configures Stove systems, and auto-injects log/body/trace capture into the app pipeline.
- **`StoveTestBase<TFixture>`**: Abstract test base class implementing xUnit's `IAsyncLifetime`. Tracks test ID and pass/fail status, calls `Stove.NotifyTestStarted/NotifyTestEnded`, and extracts test metadata from xUnit's `TestContext`.
- **`StoveLoggerProvider` / `StoveLogger`**: Captures `ILogger` output by injecting into the app's logging pipeline.
- **`StoveBodyCaptureMiddleware`**: ASP.NET Core middleware injected via `IStartupFilter` to capture HTTP request/response bodies.

This creates tight coupling: users **must** use xUnit v3 to use Stove.Net.

## How Kotlin Stove Does It

Kotlin Stove is **completely framework-agnostic**:

1. **Core has zero test framework dependencies.** The main entry point is a simple suspending function:
   ```kotlin
   suspend fun stove(validation: suspend ValidationDsl.() -> Unit) {
     check(Stove.instanceInitialized()) { ... }
     validation(ValidationDsl(instance))
   }
   ```

2. **Users instantiate Stove themselves**, typically in a setup method of whatever framework they use:
   ```kotlin
   // In Kotest's AbstractProjectConfig, JUnit's @BeforeAll, or any other framework
   Stove()
     .with { httpClient { ... }; postgresql { ... }; springBoot(...) }
     .run()
   ```

3. **Optional test framework extensions** (separate packages) provide lifecycle hooks:
   - `stove-extensions-kotest`: Implements Kotest's `TestCaseExtension` to wrap each test with `StoveReporter.withTestContext()`.
   - `stove-extensions-junit`: Implements JUnit's `BeforeEachCallback`, `AfterEachCallback` to manage test context.
   - These are **optional** — users can skip them and manage lifecycle themselves.

4. **Framework starters** (Spring, Ktor, Micronaut, Quarkus) implement `ApplicationUnderTest<TContext>` — an interface that abstracts how the app is started/stopped. The core never knows which framework is in use.

## Proposed Approach for Stove.Net

### Goal
Make Stove.Net framework-agnostic. Users should be able to use **any** .NET testing framework (xUnit, NUnit, MSTest, TUnit, or none).

### Design

#### 1. Core Provides Everything Needed
Move the essential functionality currently in `Stove.Net.Xunit` into `Stove.Net.Core` or a thin hosting layer:

- `StoveInstance` already exists in Core — it's the orchestrator.
- `StoveBuilder` already exists in Core — it's the fluent configurator.
- `ValidationDsl` already exists in Core — it's the test DSL.

What needs to move out of Xunit:
- **Log capture** (`StoveLoggerProvider`) → becomes a Core concern or is replaced by OTel (see [otel-plan.md](./otel-plan.md)).
- **Body capture** (`StoveBodyCaptureMiddleware`) → becomes a Core/HTTP concern, configured via `StoveBuilder`.
- **Trace capture** (`InProcessTraceCollector`) → becomes a Core concern or is replaced by OTel.

#### 2. Users Instantiate Stove Directly
Instead of extending `StoveFixture<TProgram>`, users create a `StoveInstance` directly:

```csharp
// In any test framework's setup — xUnit, NUnit, MSTest, or raw code
var stove = await new StoveBuilder()
    .WithWebApplication<Program>(app =>
    {
        // Configure WebApplicationFactory or any hosting approach
    })
    .WithOtlpTracing()
    .WithHttpClient()
    .WithPostgreSql(opts => { ... })
    .WithKafka(opts => { ... })
    .RunAsync();

// In tests
await stove.Validate(async s =>
{
    await s.Http(async http =>
    {
        await http.PostAsync<Order>("/api/orders", body, order =>
        {
            Assert.Equal("Confirmed", order.Status); // Use any assertion library
        });
    });
});

// In teardown
await stove.DisposeAsync();
```

#### 3. Optional Test Framework Extensions (Future)
Provide lightweight, optional packages that hook into framework lifecycles for convenience:

- `Stove.Net.Extensions.Xunit`: Auto-wraps each test with `NotifyTestStarted/NotifyTestEnded` using xUnit v3's `IAsyncLifetime`.
- `Stove.Net.Extensions.NUnit`: Uses NUnit's `[SetUp]`/`[TearDown]` attributes.
- `Stove.Net.Extensions.MSTest`: Uses MSTest's `[TestInitialize]`/`[TestCleanup]`.

These are **optional** — users who don't need per-test lifecycle tracking can skip them entirely.

#### 4. WebApplicationFactory Stays as a Hosting Option
`WebApplicationFactory<TProgram>` is not an xUnit concept — it's from `Microsoft.AspNetCore.Mvc.Testing`. It can be used independently:

```csharp
// User creates their own factory
var factory = new WebApplicationFactory<Program>()
    .WithWebHostBuilder(builder => { ... });

var stove = await new StoveBuilder()
    .WithHttpClient()
    .RunAsync();

// Set up the HTTP client from the factory
stove.GetSystem<HttpClientSystem>().SetHttpClient(factory.CreateClient());
```

### What Happens to `Stove.Net.Xunit`

**Phase 1 (Current Sprint)**: Document this approach (this file). No code changes yet.

**Phase 2**: Refactor by moving reusable components out of `Stove.Net.Xunit` into Core:
- Log/body/trace capture → Core (or replaced by OTel)
- WebApplicationFactory integration → new `Stove.Net.AspNetCore` or stays in Core

**Phase 3**: `Stove.Net.Xunit` becomes a thin optional extension providing only lifecycle hooks, or is removed entirely.

### Benefits
- **Broader adoption**: Works with any testing framework.
- **Simpler mental model**: Users don't need to learn Stove-specific base classes.
- **Less maintenance**: One core library instead of per-framework packages.
- **Aligns with Kotlin Stove**: Proven approach that works well in practice.

### Risks & Considerations
- **Per-test lifecycle tracking**: Without framework integration, users must manually call `NotifyTestStarted/NotifyTestEnded`. This is acceptable — it's what Kotlin Stove does (with optional extensions for automation).
- **Log/body capture injection**: Currently done via `StoveFixture<TProgram>` which controls the app pipeline. We need a way to configure this without the fixture. The `StoveBuilder.WithWebApplication()` approach solves this.
- **Breaking change**: Existing tests using `StoveFixture` will need migration. Since we haven't released yet, this is acceptable.
