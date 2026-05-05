# OpenTelemetry Collector Plan

## Current State (Stove.Net)

Stove.Net currently captures telemetry by **injecting itself as middleware** into the application pipeline:

| Component | How It Works | Where It Lives |
|-----------|-------------|----------------|
| `StoveLoggerProvider` | Registers as an `ILoggerProvider` in the app's DI container. Captures all `ILogger` output and emits `StoveEntry` events. | `Stove.Net.Xunit` |
| `StoveBodyCaptureMiddleware` | Injected via `IStartupFilter` early in the ASP.NET pipeline. Captures HTTP request/response bodies and emits `StoveSnapshot` events. | `Stove.Net.Xunit` |
| `InProcessTraceCollector` | Listens to `System.Diagnostics.Activity` completions (ASP.NET Core, HttpClient, EF Core, Npgsql, etc.) and converts them to `StoveSpan` events. | `Stove.Net.Core` |

### Problems with This Approach
1. **Invasive**: Requires modifying the application's DI container and middleware pipeline.
2. **Coupled to hosting**: Only works when Stove controls the app startup (via `StoveFixture`/`WebApplicationFactory`).
3. **Incomplete**: Only captures what we explicitly instrument — misses traces from libraries we don't hook into.
4. **Complex correlation**: Test ID propagation relies on `AsyncLocal` + `Activity.Baggage` + custom HTTP headers — multiple mechanisms that must all work together.
5. **Framework coupling**: The capture components live in `Stove.Net.Xunit`, tying them to xUnit.

## How Kotlin Stove Does It

Kotlin Stove takes a fundamentally different approach: **it runs an OTLP-compatible gRPC server** that receives OpenTelemetry data from the application.

### Architecture
```
┌─────────────────────────────────┐
│  Application Under Test         │
│  (instrumented with OTel SDK)   │
│                                 │
│  Traces ──┐                     │
│  Logs   ──┼── OTel Exporter ────┼──── OTLP gRPC ────┐
│  Metrics ─┘                     │                    │
└─────────────────────────────────┘                    │
                                                       ▼
                                          ┌────────────────────────┐
                                          │  Stove OTLP Receiver   │
                                          │  (gRPC server on 4317) │
                                          │                        │
                                          │  Collects spans/logs   │
                                          │  Maps traceId→testId   │
                                          │  Provides query API    │
                                          └────────────┬───────────┘
                                                       │
                                                       ▼
                                          ┌────────────────────────┐
                                          │  Dashboard / Reporter  │
                                          │  (per-test trace tree, │
                                          │   span visualization)  │
                                          └────────────────────────┘
```

### Key Implementation Details (from Kotlin Stove source)

1. **`OTLPSpanReceiver`** (stove-tracing): Starts a gRPC server implementing `TraceServiceGrpc` on port 4317 (configurable via `STOVE_TRACING_PORT` env var).

2. **`StoveTraceCollector`**: Thread-safe storage mapping `traceId → List<SpanInfo>` and `traceId → testId`. When a span arrives, it's stored and correlated to the active test.

3. **`TracingSystem`**: The test-facing API. Creates a new trace context per test step, registers it with the collector, and provides query/assertion APIs (`shouldContainSpan`, `shouldNotHaveFailedSpans`, `renderTree`).

4. **Intelligent polling**: After a test step, polls for spans with 50ms intervals, returns when first spans arrive, then waits an additional 200ms for straggler spans. 2-second timeout by default.

5. **Fallback correlation**: If traceId has no spans, searches for traces containing the testId as an attribute, or falls back to the most recent trace.

### Why This Is Better
- **Non-invasive**: Application doesn't need modification — it just needs standard OTel SDK configuration.
- **Complete data**: Captures everything the OTel SDK captures — all auto-instrumented libraries (HTTP, DB, messaging, gRPC, etc.) automatically.
- **Standard protocol**: Uses OTLP gRPC — the industry standard for telemetry export.
- **Decoupled from hosting**: Works regardless of how the app is started.
- **Simpler correlation**: The test creates a trace context, the app propagates it via standard W3C headers, and spans arrive at the collector tagged with that traceId.

## Proposed Approach for Stove.Net

### Can We Do This in .NET? **Yes.**

.NET has excellent OpenTelemetry support:
- **`OpenTelemetry.Proto`** NuGet package: Provides the protobuf definitions for OTLP.
- **`Grpc.AspNetCore.Server`** or **`Grpc.Core`**: For hosting the gRPC service.
- **`OpenTelemetry` .NET SDK**: Applications can be configured to export traces/logs/metrics via OTLP.
- **`System.Diagnostics.Activity`**: .NET's native tracing API, fully integrated with OTel SDK.

### Design

#### 1. New Project: `Stove.Net.Tracing` (or extend `Stove.Net.Core`)

**OTLP Receiver** — a gRPC server that implements the OTLP trace/log services:

```csharp
public class OtlpSpanReceiver : IAsyncDisposable
{
    private readonly int _port;
    private readonly IStoveTraceCollector _collector;
    private GrpcServer _server;

    public async Task StartAsync()
    {
        // Start gRPC server on _port (default 4317)
        // Implement TraceService.Export RPC
        // Parse incoming ExportTraceServiceRequest
        // Extract ResourceSpans → ScopeSpans → Spans
        // Convert to StoveSpan and store in _collector
    }
}
```

**Trace Collector** — thread-safe storage and correlation:

```csharp
public class StoveTraceCollector : IStoveTraceCollector
{
    // traceId → List<StoveSpan>
    private readonly ConcurrentDictionary<string, ConcurrentBag<StoveSpan>> _spans = new();
    // traceId → testId
    private readonly ConcurrentDictionary<string, string> _traceToTest = new();

    public void RegisterTrace(string traceId, string testId) { ... }
    public void RecordSpan(StoveSpan span) { ... }
    public IReadOnlyList<StoveSpan> GetSpansForTrace(string traceId) { ... }
    public IReadOnlyList<StoveSpan> GetSpansForTest(string testId) { ... }
}
```

**Tracing System** — test-facing API registered as an `IPluggedSystem`:

```csharp
public class TracingSystem : IPluggedSystem
{
    public async Task<TraceContext> StartNewTrace(string? testId = null)
    {
        var ctx = TraceContext.Create(testId);
        _collector.RegisterTrace(ctx.TraceId, testId);
        return ctx;
    }

    // Assertion APIs
    public async Task ShouldContainSpan(string operationName, TimeSpan? timeout = null) { ... }
    public async Task ShouldNotHaveFailedSpans() { ... }
    public string RenderTree(string traceId) { ... }
}
```

#### 2. Application Configuration

Users configure their app to export to Stove's receiver. This can be done in several ways:

**Option A: Environment variable (like Kotlin Stove)**
```csharp
// Stove sets OTEL_EXPORTER_OTLP_ENDPOINT before starting the app
// The app's OTel SDK picks it up automatically
builder.WithTracing(opts =>
{
    opts.Port = 4317; // Stove starts OTLP receiver here
    opts.SetEnvironmentVariable = true; // Sets OTEL_EXPORTER_OTLP_ENDPOINT
});
```

**Option B: Explicit app configuration**
```csharp
// In the app's startup (or test configuration)
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddNpgsqlInstrumentation()
        .AddOtlpExporter(opts =>
        {
            opts.Endpoint = new Uri("http://localhost:4317");
            opts.Protocol = OtlpExportProtocol.Grpc;
        }));
```

**Option C: Stove injects the configuration (preferred)**
```csharp
// StoveBuilder configures the app's OTel SDK automatically when using WithWebApplication
builder.WithWebApplication<Program>(app =>
{
    app.ConfigureServices((ctx, services) =>
    {
        services.AddOpenTelemetry()
            .WithTracing(tracing => tracing
                .AddOtlpExporter(opts =>
                {
                    opts.Endpoint = stoveOtlpEndpoint;
                }));
    });
});
```

#### 3. How Kotlin Stove Handles HTTP Body Capture

**Key finding**: Kotlin Stove does **not** use OTel for HTTP body capture. It uses a dual approach:

1. **HTTP bodies are captured client-side by `HttpSystem`**: When the test makes an HTTP call via `HttpSystem.get()`, `post()`, etc., the system captures the request body (input) and response body (output) directly and emits them as `ReportEntry` events. These flow to the dashboard as `EntryRecordedEvent` messages with `input` and `output` fields.

2. **OTel is only for server-side traces/spans**: The OTLP receiver captures `Activity`/span data — operation names, durations, status, attributes — but not request/response bodies.

This means **`StoveBodyCaptureMiddleware` is unnecessary**. Instead:
- Body capture happens at the `HttpClientSystem` level (client-side, in the test process).
- No middleware injection into the app pipeline needed.
- The app doesn't need to be modified at all.

#### 4. What This Replaces

| Current Component | Replacement | Notes |
|-------------------|------------|-------|
| `StoveLoggerProvider` | OTel log exporter | App's logs flow through OTel SDK → OTLP → Stove receiver |
| `StoveBodyCaptureMiddleware` | **Removed** | Body capture moves to `HttpClientSystem` (client-side). The system captures request/response bodies when making HTTP calls in tests, same as Kotlin Stove's `HttpSystem.report()`. |
| `InProcessTraceCollector` | OTLP receiver | All `Activity` data flows through OTel SDK → OTLP → Stove receiver |
| `Activity.Baggage` correlation | W3C `traceparent` propagation | Standard OTel context propagation — cleaner and more reliable |

#### 4. Integration with Dashboard

The OTLP receiver feeds directly into the existing event model:
- Incoming OTLP spans → convert to `StoveSpan` → emit to `IStoveEventListener`s → Dashboard receives them.
- Incoming OTLP logs → convert to `StoveEntry` → emit to listeners.
- The existing `DashboardSystem` continues to work unchanged — it just receives events from a different source.

#### 5. Test-to-Trace Correlation

```
Test starts
  → Stove creates TraceContext (traceId + rootSpanId)
  → Registers traceId → testId mapping in collector
  → Sets Activity with custom traceId
  → HTTP calls propagate traceparent header (W3C standard)
  → App receives request with traceparent
  → App's OTel SDK uses that traceId for all spans
  → Spans exported via OTLP to Stove receiver
  → Receiver correlates spans to test via traceId → testId mapping
```

This is exactly how Kotlin Stove does it, and it's cleaner than the current AsyncLocal + Baggage + Header approach.

### Implementation Phases

**Phase 1: OTLP Receiver**
- Create `Stove.Net.Tracing` project (or add to Core).
- Implement `OtlpSpanReceiver` (gRPC server receiving `ExportTraceServiceRequest`).
- Implement `StoveTraceCollector` (span storage + correlation).
- Add `TracingSystem` as an `IPluggedSystem`.

**Phase 2: App Integration**
- Add `WithTracing()` to `StoveBuilder`.
- Auto-set `OTEL_EXPORTER_OTLP_ENDPOINT` environment variable.
- Test with a sample app that has OTel SDK configured.

**Phase 3: Log Collection via OTel**
- Extend OTLP receiver to handle `ExportLogsServiceRequest`.
- Convert OTLP log records to `StoveEntry` events.
- Optionally replace `StoveLoggerProvider` with OTel log export.

**Phase 4: Remove In-Process Capture**
- Remove `StoveLoggerProvider` (replaced by OTel logs).
- Remove `InProcessTraceCollector` (replaced by OTLP receiver).
- Remove `StoveBodyCaptureMiddleware` (body capture moves to `HttpClientSystem` client-side).

### Dependencies

```xml
<!-- New NuGet packages needed -->
<PackageReference Include="OpenTelemetry.Proto" Version="..." />
<PackageReference Include="Grpc.AspNetCore.Server" Version="..." />
<!-- Or for standalone gRPC server: -->
<PackageReference Include="Grpc.Core" Version="..." />
```

### Risks & Considerations

1. **HTTP body capture (resolved)**: Kotlin Stove captures bodies client-side in `HttpSystem` (not via OTel). We'll do the same — `HttpClientSystem` captures request/response bodies when tests make HTTP calls. No middleware needed.

2. **App must have OTel SDK**: Unlike the current approach where Stove injects everything, the app needs to have `OpenTelemetry` configured. This is increasingly common in .NET apps, but it's an additional requirement. We can mitigate this by auto-injecting OTel SDK configuration when Stove controls the app startup.

3. **Port conflicts**: The OTLP receiver needs a port (default 4317). Must handle port-in-use scenarios gracefully — pick a random available port and pass it via env var.

4. **Span arrival timing**: Spans are exported asynchronously by the OTel SDK. There's a delay between the app processing a request and spans arriving at the collector. Kotlin Stove handles this with intelligent polling (50ms intervals + 200ms straggler wait). We should implement the same.

5. **Batch exporter flush**: The OTel SDK batches span exports by default. In tests, we may need to configure shorter batch intervals or use a simple exporter for faster span delivery.
