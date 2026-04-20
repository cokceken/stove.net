# Dashboard Integration: Kotlin Stove vs Stove.Net

## Overview

The Stove Dashboard provides real-time observability into integration test runs. It receives structured events over gRPC from the test framework, enabling a central UI to visualize test lifecycle, trace propagation, system snapshots, and recorded interactions.

Both Kotlin Stove and Stove.Net implement a **DashboardSystem** that:

1. Connects to a gRPC dashboard server (default `localhost:4041`)
2. Emits events through a buffered, background channel
3. Auto-disables after consecutive failures to avoid blocking tests
4. Covers the full test lifecycle: run → test → entries/spans/snapshots → test end → run end

---

## Architecture

```
┌──────────────────────┐        gRPC (SendEvent)        ┌─────────────────┐
│   Stove Test Run     │ ─────────────────────────────▶  │  Dashboard UI   │
│                      │                                 │                 │
│  DashboardSystem     │   RunStarted / RunEnded         │  Visualizes:    │
│    ├─ Emitter        │   TestStarted / TestEnded       │  - Test runs    │
│    │  (Channel+Task) │   EntryRecorded                 │  - Traces       │
│    │                 │   SpanRecorded                  │  - Snapshots    │
│    └─ Listeners      │   Snapshot                      │  - Entries      │
│       ├─ ReportEvent │                                 └─────────────────┘
│       └─ SpanEvent   │
└──────────────────────┘
```

### Proto Contract

```protobuf
service DashboardEventService {
  rpc StreamEvents(stream DashboardEvent) returns (EventAck);
  rpc SendEvent(DashboardEvent) returns (EventAck);
}
```

Both implementations use the **unary `SendEvent` RPC**. The `StreamEvents` bidirectional streaming RPC exists in the proto but is only available in Kotlin.

### Event Types

| Event | Purpose |
|---|---|
| `RunStartedEvent` | Marks the beginning of a test run |
| `RunEndedEvent` | Marks the end of a test run with summary stats |
| `TestStartedEvent` | Fired when an individual test begins |
| `TestEndedEvent` | Fired when an individual test completes |
| `EntryRecordedEvent` | Records a system interaction (publish, consume, stub, etc.) |
| `SpanRecordedEvent` | Records an OpenTelemetry/tracing span |
| `SnapshotEvent` | Captures point-in-time state of a system |

---

## Kotlin Stove (Reference Implementation)

### DashboardSystem

`DashboardSystem` implements `PluggedSystem`, `RunAware`, `ReportEventListener`, and `SpanEventListener`.

**Lifecycle:**

1. **`run()`** — Creates a gRPC channel to `localhost:4041`, instantiates `DashboardEmitter` with a buffered `Channel<DashboardEvent>`, registers itself as both `ReportEventListener` and `SpanEventListener`, then emits `RunStartedEvent`.
2. **During tests** — Emits `TestStartedEvent`, `EntryRecordedEvent`, `SpanRecordedEvent`, and `SnapshotEvent` as they occur.
3. **On test end** — Collects snapshots from **all** systems implementing the `Reports` interface, emits a `SnapshotEvent` for each, then emits `TestEndedEvent`.
4. **On stop** — Finalizes any open tests, emits `RunEndedEvent`, unregisters listeners, closes the emitter (30-second drain timeout).

### DashboardEmitter

- **Channel**: Unbounded `Channel<DashboardEvent>` — never drops events.
- **Background coroutine**: Runs on `Dispatchers.IO` with a `SupervisorJob`, draining events and sending them via unary gRPC.
- **Auto-disable**: Uses an `AtomicInteger` failure counter and `AtomicBoolean` disabled flag. After **5 consecutive failures**, the emitter disables itself. On any success, the counter resets to 0.
- **Shutdown sequence**: Close channel → wait up to 30 seconds for drain → cancel coroutine scope → gRPC channel shutdown with 5-second grace period.

### Key Event Fields

| Event | Key Fields |
|---|---|
| RunStarted | `timestamp`, `app_name`, `systems[]`, `stove_version` |
| RunEnded | `timestamp`, `total_tests`, `passed`, `failed`, `duration_ms` |
| TestStarted | `test_id`, `test_name`, `spec_name`, `timestamp`, `test_path[]` |
| TestEnded | `test_id`, `status`, `duration_ms`, `error`, `timestamp` |
| EntryRecorded | `test_id`, `timestamp`, `system`, `action`, `result`, `input`, `output`, `metadata` map, `expected`, `actual`, `error`, `trace_id` |
| SpanRecorded | `trace_id`, `span_id`, `parent_span_id`, `operation_name`, `service_name`, `start_time_nanos`, `end_time_nanos`, `status`, `attributes` map, `ExceptionInfo` |
| Snapshot | `test_id`, `system`, `state_json`, `summary` |

### Tracing Integration

```
OTel Java Agent ──▶ OTLP Spans ──▶ OTLPSpanReceiver ──▶ StoveTraceCollector
                                                              │
                                                              ▼
                                                      SpanEventListener
                                                              │
                                                              ▼
                                                       DashboardSystem
                                                              │
                                                              ▼
                                                      SpanRecordedEvent
```

- **TracingSystem** registers `DashboardSystem` as a `SpanEventListener`.
- All OTLP spans flow through `OTLPSpanReceiver` → `StoveTraceCollector` → `SpanEventListener` → `DashboardSystem` → `SpanRecordedEvent`.
- **Trace correlation** via test ID headers: `X-Stove-Test-Id` custom header and W3C `baggage` propagation.
- Each test gets its own trace context via `TraceContext` backed by `InheritableThreadLocal`, which also integrates with coroutine context elements.

### Reporting / Snapshots

- Systems implement the `Reports` interface, which requires a `report(): SystemSnapshot` method.
- `SystemSnapshot` contains system-specific state (e.g., messages consumed, stubs registered, HTTP calls recorded).
- At **test end**, `DashboardSystem` iterates over all plugged systems, calls `report()` on those implementing `Reports`, and emits a `SnapshotEvent` for each.

---

## Stove.Net (Our Implementation)

### DashboardSystem

`DashboardSystem` implements `IPluggedSystem` and `IStoveEventListener`.

**Lifecycle:**

1. **Constructor** — `DashboardEmitter` is created eagerly (fixing an earlier ordering bug where late initialization could miss early events).
2. **On run** — Connects to gRPC endpoint, emits `RunStartedEvent`.
3. **During tests** — Emits `TestStartedEvent`, `EntryRecordedEvent`, `SpanRecordedEvent`, and `SnapshotEvent`.
4. **On test end** — Emits `TestEndedEvent`. *(Does not currently collect snapshots from systems.)*
5. **On stop** — Emits `RunEndedEvent`, shuts down emitter with drain.

### DashboardEmitter

- **Channel**: Unbounded `Channel<DashboardEvent>` (System.Threading.Channels) — never drops events.
- **Background drain**: A `Task` running on the ThreadPool continuously reads from the channel and sends via unary gRPC.
- **Auto-disable**: Configurable `MaxConsecutiveFailures` (default 5). Same pattern as Kotlin — counter increments on failure, resets on success, disables after threshold.
- **Drain timeout**: Configurable (default 30 seconds). On shutdown, the channel is completed and the drain task is given the timeout to flush remaining events.

### Tracing Integration

```
Application ──▶ System.Diagnostics.Activity ──▶ InProcessTraceCollector
                                                         │
                                                         ▼
                                                   StoveSpan
                                                         │
                                                         ▼
                                                  DashboardSystem
                                                         │
                                                         ▼
                                                 SpanRecordedEvent
```

- Uses `System.Diagnostics.Activity` and `ActivityListener` (the .NET equivalent of OTel spans).
- `InProcessTraceCollector` captures `Activity` instances, converts them to `StoveSpan`, and forwards them to the dashboard.
- Trace propagation uses `Activity.Current` (the .NET analog to `InheritableThreadLocal` / coroutine context), which propagates across async/await boundaries automatically.

### What Works Today

- ✅ Full event lifecycle: `RunStarted` → `TestStarted` → entries/spans → `TestEnded` → `RunEnded`
- ✅ gRPC integration with shared proto contract
- ✅ Auto-disable on consecutive failures (configurable threshold)
- ✅ Configurable drain timeout
- ✅ Trace propagation via `System.Diagnostics.Activity`
- ✅ `InProcessTraceCollector` captures activities → `StoveSpan` → dashboard
- ✅ Background channel-based emit (non-blocking to tests)

---

## Feature Comparison

| Feature | Kotlin Stove | Stove.Net | Notes |
|---|:---:|:---:|---|
| **Event Lifecycle** | ✅ | ✅ | Both emit all 7 event types |
| **Unary gRPC (`SendEvent`)** | ✅ | ✅ | Identical behavior |
| **Streaming gRPC (`StreamEvents`)** | ✅ | ❌ | Kotlin supports bidirectional streaming |
| **Unbounded event channel** | ✅ | ✅ | Kotlin: `Channel`, .NET: `System.Threading.Channels` |
| **Background drain** | ✅ | ✅ | Kotlin: coroutine on `Dispatchers.IO`, .NET: `Task` on ThreadPool |
| **Auto-disable on failure** | ✅ | ✅ | Both default to 5 consecutive failures |
| **Drain timeout** | ✅ (30s) | ✅ (30s) | Both configurable |
| **`Reports` interface** | ✅ | ❌ | Systems in Kotlin can report their state |
| **Auto snapshot collection** | ✅ | ❌ | Kotlin collects from all `Reports` systems at test end |
| **`test_path` in TestStarted** | ✅ | ❌ | Kotlin populates hierarchical test path |
| **W3C baggage propagation** | ✅ | ❌ | Kotlin propagates test ID via W3C baggage |
| **Per-test ID headers** | ✅ | ❌ | `X-Stove-Test-Id` in Kafka/WireMock/HTTP |
| **Trace context model** | `InheritableThreadLocal` + coroutine context | `Activity.Current` (async-aware) | Functionally equivalent |
| **OTel integration** | Java Agent via Gradle plugin | `ActivityListener` | Different mechanisms, same goal |
| **Emitter initialization** | Lazy (on `run()`) | Eager (constructor) | .NET fixed ordering bug with eager init |
| **Supervisor isolation** | ✅ (`SupervisorJob`) | ✅ (independent `Task`) | Both isolate emitter failures from tests |

---

## Gaps and Improvement Opportunities

### 1. No `Reports` Interface for Systems

**Impact**: High — Snapshots are a core dashboard feature.

In Kotlin, systems implement `Reports` with a `report(): SystemSnapshot` method. At test end, `DashboardSystem` automatically iterates all plugged systems, collects snapshots, and emits them. In Stove.Net, there is no equivalent interface, so snapshot events are never emitted automatically.

**Recommendation**: Define an `IReportsState` interface (or similar) that systems can implement:

```csharp
public interface IReportsState
{
    SystemSnapshot Report();
}

public record SystemSnapshot(string System, string StateJson, string Summary);
```

### 2. No Automatic Snapshot Collection at Test End

**Impact**: High — Directly depends on Gap 1.

Even if systems implemented a reporting interface, `DashboardSystem` does not currently enumerate systems and collect snapshots at test end.

**Recommendation**: After implementing `IReportsState`, update the test-end handler in `DashboardSystem` to:
1. Iterate all `IPluggedSystem` instances
2. Check which implement `IReportsState`
3. Call `Report()` on each
4. Emit a `SnapshotEvent` per system

### 3. No Per-Test ID Header Propagation

**Impact**: Medium — Required for trace correlation in multi-system tests.

Kotlin injects `X-Stove-Test-Id` into Kafka headers, WireMock requests, and HTTP calls. This enables the dashboard to correlate all interactions to a specific test. Stove.Net does not propagate test IDs through system interactions.

**Recommendation**: Use `Activity.Baggage` to carry the test ID and inject it into outgoing requests. Each system component (Kafka, WireMock, HTTP) should read the current test ID and include it in its interactions.

### 4. `test_path` Not Populated in TestStartedEvent

**Impact**: Low — Cosmetic, but useful for hierarchical test visualization.

Kotlin populates `test_path[]` with the hierarchical path of the test (e.g., `["FeatureSpec", "when user logs in", "should return token"]`). Stove.Net leaves this empty.

**Recommendation**: Extract test hierarchy from the xUnit/NUnit test context and populate `test_path` in `TestStartedEvent`.

### 5. No Streaming RPC Support

**Impact**: Low — Unary `SendEvent` works correctly and is what Kotlin actually uses in practice.

The proto defines `StreamEvents` for bidirectional streaming, but neither implementation uses it as the primary transport. Kotlin has the capability; Stove.Net does not.

**Recommendation**: Defer. Unary RPCs are simpler, easier to debug, and sufficient for current throughput. Consider streaming only if event volume becomes a bottleneck.

### 6. No W3C Baggage Propagation

**Impact**: Medium — Limits cross-service trace correlation.

Kotlin uses W3C `baggage` header to propagate the test ID across service boundaries. Stove.Net relies on `Activity.Current` which does not automatically inject baggage into outgoing HTTP/gRPC calls.

**Recommendation**: Add a `DelegatingHandler` (for `HttpClient`) and interceptors (for gRPC) that inject `baggage` with the current test ID. .NET's `Activity` already supports baggage items — they just need to be propagated.

### 7. Trace Context Sophistication

**Impact**: Low — .NET's `Activity` model is functionally equivalent.

Kotlin uses `InheritableThreadLocal` combined with coroutine context elements to track the current test's trace context. .NET's `Activity.Current` flows automatically across `async`/`await` boundaries, providing equivalent functionality through a different mechanism.

**Recommendation**: No action needed. The approaches are idiomatic to their respective platforms.

---

## Recommendations: Next Steps

### Phase 1: Snapshot Support (High Impact)

1. Define `IReportsState` interface in the core abstractions
2. Implement `IReportsState` on key systems (Kafka, WireMock, EntityFramework, etc.)
3. Update `DashboardSystem` to collect and emit snapshots at test end
4. Write tests validating snapshot emission

### Phase 2: Test ID Correlation (Medium Impact)

1. Add test ID to `Activity.Baggage` at test start
2. Create HTTP `DelegatingHandler` that injects `X-Stove-Test-Id` header
3. Update Kafka producer to include test ID in message headers
4. Update WireMock request matching to include test ID
5. Populate `test_path` from test framework metadata

### Phase 3: Polish (Low Impact)

1. Evaluate streaming RPC if event throughput becomes a concern
2. Add W3C baggage propagation for cross-service scenarios
3. Add dashboard connection health metrics / logging

---

## Summary

Stove.Net's dashboard integration covers the core event lifecycle and gRPC transport faithfully. The primary gaps are around **snapshot reporting** (systems cannot report their state) and **test ID correlation** (test IDs are not propagated through system interactions). Addressing these two areas would bring Stove.Net to feature parity with the Kotlin reference implementation for dashboard integration.
