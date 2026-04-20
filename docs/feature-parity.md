# Feature Parity: Kotlin Stove vs Stove.Net

> **Last updated:** July 2025
>
> This document tracks feature parity between [Kotlin Stove](https://github.com/Trendyol/stove) plugins and their Stove.Net counterparts. Use it to identify gaps, plan roadmap priorities, and guide contributions.

---

## Plugin Overview

| Plugin | Kotlin Stove | Stove.Net | Parity |
|--------|:------------:|:---------:|:------:|
| HTTP | ✅ | ✅ | ⚠️ Partial |
| PostgreSQL | ✅ | ✅ | ⚠️ Partial |
| Redis | ✅ | ✅ | ⚠️ Partial |
| Kafka | ✅ | ✅ | ⚠️ Partial |
| WireMock | ✅ | ✅ | ⚠️ Partial |
| MongoDB | ✅ | ✅ | ⚠️ Partial |
| Elasticsearch | ✅ | ❌ | ❌ Missing |
| Couchbase | ✅ | ❌ | ❌ Missing |
| Cassandra | ✅ | ❌ | ❌ Missing |
| MySQL | ✅ | ❌ | ❌ Missing |
| MSSQL | ✅ | ❌ | ❌ Missing |
| gRPC | ✅ | ❌ | ❌ Missing |
| gRPC Mock | ✅ | ❌ | ❌ Missing |

**Legend:** ✅ Parity — ⚠️ Partial — ❌ Missing

---

## Per-Plugin Comparison

### HTTP

| | Kotlin (`stove-http`) | .NET (`Stove.Net.Http`) |
|---|---|---|
| **Underlying client** | Ktor HTTP client + Jackson | `System.Net.Http.HttpClient` (from `WebApplicationFactory`) |
| **Extension point** | DSL on `TestSystem` | `.WithHttpClient()` on `StoveBuilder` |

| Feature | Kotlin Stove | Stove.Net | Status |
|---------|:-----------:|:---------:|:------:|
| GET with typed response | `getResponse<T>`, `get<T>`, `getMany<T>` | `GetAsync<T>` | ⚠️ Partial |
| GET bodiless | `getBodilessResponse` | — | ❌ Missing |
| POST with typed response | `postAndExpectJson<T>`, `postAndExpectBody<T>` | `PostAsync<T>` | ⚠️ Partial |
| POST bodiless | `postAndExpectBodilessResponse` | — | ❌ Missing |
| PUT with typed response | `putAndExpectJson<T>`, `putAndExpectBody<T>` | `PutAsync<T>` | ⚠️ Partial |
| PUT bodiless | `putAndExpectBodilessResponse` | — | ❌ Missing |
| PATCH with typed response | `patchAndExpectJson<T>`, `patchAndExpectBody<T>` | `PatchAsync<T>` | ⚠️ Partial |
| PATCH bodiless | `patchAndExpectBodilessResponse` | — | ❌ Missing |
| DELETE with typed response | `deleteAndExpectJson<T>` | `DeleteAsync<T>` | ✅ Parity |
| DELETE bodiless | `deleteAndExpectBodilessResponse` | — | ❌ Missing |
| HEAD | `headAndExpectBodilessResponse` | — | ❌ Missing |
| Generic send with HTTP method | — | `SendAsync` | ✅ .NET only |
| Raw `HttpResponseMessage` validation | — | All methods have overload | ✅ .NET only |
| Custom headers | ✅ | `Dictionary<string, string>` | ✅ Parity |
| Multipart upload | `postMultipartAndExpectResponse<T>` | — | ❌ Missing |
| NDJSON streaming | `readJsonStream<T>` | — | ❌ Missing |
| Bearer token injection | ✅ | — | ❌ Missing |
| Query parameter encoding helper | ✅ | — | ❌ Missing |
| WebSocket send/receive | ✅ text + binary | — | ❌ Missing |
| WebSocket collect with timeout | `collectTexts`, `collectBinaries` | — | ❌ Missing |
| Client configuration / factory | `configureClient`, `createClient` | — | ❌ Missing |
| Content converter config | ✅ Jackson | — | ❌ Missing |
| Timeout config | ✅ | — | ❌ Missing |

**Key gaps:** WebSocket support, multipart upload, NDJSON streaming, bearer token helper, bodiless response variants, HEAD method, query parameter encoding.

---

### PostgreSQL

| | Kotlin (`stove-postgres`) | .NET (`Stove.Net.PostgreSql`) |
|---|---|---|
| **Underlying client** | JDBC / Kotlin coroutines | Npgsql |
| **Container** | `PostgresqlContainerOptions` (registry, image, tag, substitute) | Testcontainers `PostgreSqlBuilder` |

| Feature | Kotlin Stove | Stove.Net | Status |
|---------|:-----------:|:---------:|:------:|
| Query with typed result | `shouldQuery<T>(query, params, mapper, assertion)` | `ShouldQuery<T>(sql, map, validate)` | ⚠️ Partial |
| Parameterized queries | ✅ `parameters` arg | — | ❌ Missing |
| Execute raw SQL | `shouldExecute(sql, params)` | `ShouldExecute(sql)` | ⚠️ Partial |
| Scalar query | — | `ShouldQueryScalar<T>(sql, validate)` | ✅ .NET only |
| Row-level mapping | `rowMapper` lambda | Uses `ReadAsJsonAsync` | ⚠️ Partial |
| NativeSqlOperations | `execute` → affected rows, `select<T>` → `List<T>` | — | ❌ Missing |
| Fault injection: pause/unpause | `pause()`, `unpause()` | — | ❌ Missing |
| Fault injection: slow query | — | `SimulateSlowQuery(duration)` | ✅ .NET only |
| Fault injection: read-only mode | — | `SetReadOnly(bool)` | ✅ .NET only |
| Provided instance support | ✅ | — | ❌ Missing |
| Migration support | `DatabaseMigration<PostgresMigrationContext>` | — | ❌ Missing |
| Config exposure | `RelationalDatabaseExposedConfiguration` | `ConfigureExposedConfiguration` callback | ✅ Parity |
| Cleanup lambda | ✅ | — | ❌ Missing |
| Container reuse | ✅ | — | ❌ Missing |
| Custom container options | Registry, tag, compatible substitute | Basic image config | ⚠️ Partial |

**Key gaps:** Parameterized queries, provided instance support, migration support, pause/unpause fault injection, NativeSqlOperations, container reuse.

---

### Redis

| | Kotlin (`stove-redis`) | .NET (`Stove.Net.Redis`) |
|---|---|---|
| **Underlying client** | Lettuce | StackExchange.Redis (`IDatabase`) |
| **Container** | `RedisContainerOptions` | Testcontainers `RedisBuilder` |

| Feature | Kotlin Stove | Stove.Net | Status |
|---------|:-----------:|:---------:|:------:|
| Set string value | `client().set(key, value)` | `SetAsync(key, value)` | ✅ Parity |
| Set typed object | `client().set(key, serialize(obj))` | `SetAsync<T>(key, obj)` | ✅ Parity |
| Get string value | `client().get(key)` | `GetAsync(key)` | ✅ Parity |
| Get typed object | Manual deserialization | `GetAsync<T>(key)` | ✅ Parity |
| Key existence check | `client().exists(key)` | `ShouldExist(key)`, `ShouldNotExist(key)` | ✅ Parity |
| Delete key | `client().del(key)` | `DeleteAsync(key)` | ✅ Parity |
| Hash set | Via client API | `HashSetAsync(key, field, value)` | ✅ Parity |
| Hash get all | Via client API | `HashGetAllAsync(key, validate)` | ✅ Parity |
| TTL / expiry operations | `client().setex()`, `client().ttl()` | — | ❌ Missing |
| Direct client access | `client()` → full Lettuce API | — | ❌ Missing |
| Pub/Sub | Via client API | — | ❌ Missing |
| Database selection | ✅ config option | — | ❌ Missing |
| Password configuration | ✅ | — | ❌ Missing |
| Fault injection: pause/unpause | `pause()`, `unpause()` | — | ❌ Missing |
| Provided instance support | ✅ | — | ❌ Missing |
| Migration support | ✅ | — | ❌ Missing |
| Cleanup lambda | ✅ | — | ❌ Missing |

**Key gaps:** No TTL/expiry methods, no direct client access, no pub/sub, no pause/unpause, no database selection, no provided instance support.

---

### Kafka

| | Kotlin (`stove-kafka`) | .NET (`Stove.Net.Kafka`) |
|---|---|---|
| **Underlying client** | gRPC bridge intercepting Kafka traffic | Confluent.Kafka |
| **Container** | Embedded Kafka or TestContainers | TestContainers |

| Feature | Kotlin Stove | Stove.Net | Status |
|---------|:-----------:|:---------:|:------:|
| Publish message | `publish(topic, message, key, headers, partition, testCase)` | `PublishAsync<T>(topic, key, value, headers)` | ⚠️ Partial |
| Assert consumed | `shouldBeConsumed<T>(atLeastIn, condition)` | — | ❌ Missing |
| Assert published | `shouldBePublished<T>(atLeastIn, condition)` | `ShouldBePublished<T>(topic, validate, timeout)` | ⚠️ Partial |
| Assert published (raw) | — | `ShouldBePublished(topic, validate, timeout)` | ✅ .NET only |
| Assert failed | `shouldBeFailed<T>(atLeastIn, condition)` | — | ❌ Missing |
| Assert retried | `shouldBeRetried<T>(atLeastIn, condition)` | — | ❌ Missing |
| Message metadata in assertions | `ObservedMessage<T>` (topic, key, headers, offset, partition) | — | ❌ Missing |
| Topic management | `createTopics()`, `deleteTopics()` | — | ❌ Missing |
| Per-test isolation | `X-Stove-Test-Id` header | — | ❌ Missing |
| Trace header injection | `TRACEPARENT`, `STOVE_TEST_ID` | — | ❌ Missing |
| Error/retry topic suffixes | `TopicSuffixes` | — | ❌ Missing |
| Embedded Kafka option | ✅ | — | ❌ Missing |
| SASL/SSL security config | ✅ | — | ❌ Missing |
| gRPC bridge server | ✅ | — | ❌ Missing |
| System snapshot | ✅ | — | ❌ Missing |
| Message history storage | Type-keyed sink (consumed/published/failed/retried) | `ConcurrentDictionary` with type-keyed storage | ⚠️ Partial |
| Background consumer | gRPC interceptor | Background `Task` polling all topics | ✅ Parity |

**Key gaps:** No `shouldBeConsumed`/`shouldBeFailed`/`shouldBeRetried`, no message metadata in assertions, no topic management, no per-test isolation, no SASL/SSL, no embedded Kafka.

---

### WireMock

| | Kotlin (`stove-wiremock`) | .NET (`Stove.Net.WireMock`) |
|---|---|---|
| **Underlying library** | WireMock Java | WireMock.Net (`WireMockServer`) |

| Feature | Kotlin Stove | Stove.Net | Status |
|---------|:-----------:|:---------:|:------:|
| Mock GET | `mockGet(url, status, body, headers)` | `MockEndpoint(path, "GET", ...)` | ✅ Parity |
| Mock POST | `mockPost(...)` | `MockEndpoint(path, "POST", ...)` | ✅ Parity |
| Mock PUT | `mockPut(...)` | `MockEndpoint(path, "PUT", ...)` | ✅ Parity |
| Mock PATCH | `mockPatch(...)` | `MockEndpoint(path, "PATCH", ...)` | ✅ Parity |
| Mock DELETE | `mockDelete(...)` | `MockEndpoint(path, "DELETE", ...)` | ✅ Parity |
| Mock HEAD | `mockHead(...)` | — | ❌ Missing |
| Assert received | `verify(count, requestMatcher)` | `ShouldHaveReceived(path)` | ⚠️ Partial |
| Assert by method | — | `ShouldHaveReceivedGet/Post/Put/Delete/Patch(path)` | ✅ .NET only |
| Assert body content | — | `ShouldHaveReceivedBody(path, method, validate, expectedCount)` | ✅ .NET only |
| Advanced `MappingBuilder` config | `mockGetConfigure`, `mockPostConfigure`, etc. | — | ❌ Missing |
| Partial body matching | `mockPostContaining`, `mockPutContaining`, `mockPatchContaining` | — | ❌ Missing |
| Scenarios | `behaviourFor(name, configure)` with `ScenarioContext` | — | ❌ Missing |
| Verify with count | `verify(count, matcher)` | — | ❌ Missing |
| Per-test stub isolation | ✅ auto-remove after match | — | ❌ Missing |
| Stub cache | Caffeine-based | — | ❌ Missing |
| Reset / delete all stubs | `reset()`, `deleteAllStubs()` | — | ❌ Missing |
| Request/response logging | ✅ | — | ❌ Missing |
| Vacuum cleaner extension | `WireMockVacuumCleaner` | — | ❌ Missing |
| System snapshot | ✅ | — | ❌ Missing |

**Key gaps:** No advanced matchers, no scenarios, no verify with count, no per-test stub isolation, no cleanup methods, no request logging.

---

### MongoDB

| | Kotlin (`stove-mongodb`) | .NET (`Stove.Net.MongoDb`) |
|---|---|---|
| **Underlying client** | MongoDB Kotlin Coroutine Client (Jackson serde) | MongoDB.Driver (C# driver, LINQ expressions) |
| **Container** | Custom container options | Testcontainers `MongoDbBuilder` |

| Feature | Kotlin Stove | Stove.Net | Status |
|---------|:-----------:|:---------:|:------:|
| Query with assertion | `shouldQuery<T>(query, collection, assertion)` | `ShouldFind<T>(filter, validate, collectionName)` | ✅ Parity |
| Find all | — | `ShouldFindAll<T>(validate, collectionName)` | ✅ .NET only |
| Get by ObjectId | `shouldGet<T>(objectId, collection, assertion)` | — | ❌ Missing |
| Exist / not exist | — | `ShouldExist<T>`, `ShouldNotExist<T>` | ✅ .NET only |
| Count with expected | — | `ShouldCount<T>(filter, expected, collectionName)` | ✅ .NET only |
| Save document | `save<T>(instance, objectId, collection)` | `InsertAsync<T>(document, collectionName)` | ⚠️ Partial |
| Save with custom ObjectId | ✅ | — | ❌ Missing |
| Insert many | — | `InsertManyAsync<T>(documents, collectionName)` | ✅ .NET only |
| Delete document | `shouldDelete(objectId, collection)` | `DeleteAsync<T>(filter, collectionName)` | ✅ Parity |
| Drop collection | — | `DropCollectionAsync(collectionName)` | ✅ .NET only |
| Direct client access | `client()` | — | ❌ Missing |
| JSON query syntax | `{"field": "value"}` | — (uses LINQ expressions) | ❌ Missing |
| Custom serde / ObjectId handling | Jackson + `JsonWriterSettings` | — | ❌ Missing |
| Provided instance support | ✅ | — | ❌ Missing |
| Migration support | ✅ | — | ❌ Missing |
| Cleanup lambda | ✅ | — | ❌ Missing |

**Key gaps:** No get by ObjectId, no save with custom ObjectId, no direct client access, no JSON query syntax, no provided instance support, no migration support.

---

## Missing Plugins

The following Kotlin Stove plugins have **no Stove.Net equivalent**:

| Plugin | Kotlin Capability Summary |
|--------|--------------------------|
| **Elasticsearch** | Full CRUD operations, Query DSL support, index management, TLS configuration |
| **Couchbase** | N1QL queries, multi-collection support, document expiration |
| **Cassandra** | CQL queries, bound statements, keyspace management |
| **MySQL** | Full RDBMS support (same API surface as PostgreSQL plugin) |
| **MSSQL** | Full RDBMS support (same API surface as PostgreSQL plugin) |
| **gRPC** | Channel creation, stub access, server/client/bidirectional streaming |
| **gRPC Mock** | Mock gRPC servers for testing without real services |

---

## Cross-Cutting Feature Gaps

These are framework-level capabilities present in Kotlin Stove but absent from Stove.Net:

| Feature | Description | Impact |
|---------|-------------|--------|
| **Provided instance support** | Connect to existing infrastructure instead of spinning up containers | High — enables hybrid testing with shared environments |
| **Migration support** | Run setup scripts (SQL, seed data) before tests execute | High — critical for realistic test data |
| **Container reuse** | Reuse containers across test runs to speed up iteration | Medium — significant DX improvement |
| **System snapshots** | All systems can report their current state for debugging | Medium — aids test failure diagnosis |
| **Per-test isolation** | Test ID headers in Kafka/WireMock for parallel test safety | High — required for parallel test execution |
| **Embedded runtimes** | Embedded Kafka as alternative to containers | Low — containers are acceptable for most teams |
| **Cleanup lambdas** | Configurable cleanup hooks between tests | Medium — ensures test independence |

---

## Priority Recommendations

### P0 — High Impact, Broad Applicability

1. **Parameterized queries for PostgreSQL** — Prevents SQL injection in tests and aligns with Kotlin API. Straightforward to add via Npgsql parameters.
2. **`shouldBeConsumed` for Kafka** — The most common Kafka assertion pattern. Without it, teams can only verify messages were *published*, not *consumed and processed*.
3. **Migration support (cross-cutting)** — Enables SQL seed scripts, schema setup, and realistic test data across PostgreSQL (and future RDBMS plugins).

### P1 — Important for Feature Completeness

4. **WebSocket support for HTTP** — Required for apps using SignalR or raw WebSocket endpoints.
5. **Pause/unpause fault injection (cross-cutting)** — Kotlin supports this on PostgreSQL, Redis, and Kafka containers. Essential for resilience testing.
6. **Per-test isolation for Kafka** — Without test ID headers, parallel Kafka tests can interfere with each other.
7. **WireMock scenarios and advanced matchers** — Needed for testing stateful HTTP interactions and complex request matching.
8. **Provided instance support (cross-cutting)** — Allows connecting to shared infrastructure in CI/CD or staging environments.

### P2 — New Plugins

9. **Elasticsearch plugin** — Widely used in .NET applications for search and analytics.
10. **MSSQL plugin** — SQL Server is the most common RDBMS in the .NET ecosystem; high demand expected.
11. **MySQL plugin** — Commonly paired with .NET microservices.

### P3 — Nice to Have

12. **Container reuse (cross-cutting)** — Speeds up local development loops.
13. **gRPC plugin** — Growing adoption in .NET via `Grpc.Net.Client`.
14. **Multipart upload for HTTP** — Needed for file-upload-heavy applications.
15. **System snapshots (cross-cutting)** — Useful for debugging but not blocking.
