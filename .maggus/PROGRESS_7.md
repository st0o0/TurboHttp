# PROGRESS_7.md — Engine Wiring Audit

**Date:** 2026-03-15
**Task:** TASK-AUD-001 — Read and Document the Full Engine Wiring

---

## Engine.cs — Full Wiring Map

`Engine.cs` has two main paths: **Extended Pipeline** (production+test) and **Engine Core** (protocol demux).

---

### Path 1: `BuildExtendedPipeline` (always used)

This wraps every request/response with business logic stages:

**Request chain (top → engine):**

| Stage | Class | Notes |
|-------|-------|-------|
| RequestEnricherStage | `Streams/Stages/RequestEnricherStage.cs` | Applies BaseAddress, DefaultHeaders, DefaultVersion |
| MergePreferred (redirect feedback) | Akka built-in | Accepts re-queued redirect requests |
| CookieInjectionStage | `Streams/Stages/CookieInjectionStage.cs` | Injects cookies from shared CookieJar |
| MergePreferred (retry feedback) | Akka built-in | Accepts re-queued retry requests |
| CacheLookupStage | `Streams/Stages/CacheLookupStage.cs` | Out0=cache miss→engine, Out1=cache hit→merge |

**Engine core** (see below)

**Response chain (engine → output):**

| Stage | Class | Notes |
|-------|-------|-------|
| DecompressionStage | `Streams/Stages/DecompressionStage.cs` | gzip/deflate/brotli |
| CookieStorageStage | `Streams/Stages/CookieStorageStage.cs` | Persists Set-Cookie into CookieJar |
| CacheStorageStage | `Streams/Stages/CacheStorageStage.cs` | Stores cacheable responses |
| RetryStage | `Streams/Stages/RetryStage.cs` | Out0=final, Out1=retry→retryMerge |
| Merge (cache hit + response) | Akka built-in | Merges cached and live responses |
| RedirectStage | `Streams/Stages/RedirectStage.cs` | Out0=final, Out1=redirect→redirectMerge |

---

### Path 2: `BuildEngineCoreGraph` — Test Mode (http10Factory != null)

Partitions into 3 protocol lanes (HTTP/1.0, HTTP/1.1, HTTP/2):

| Lane | Protocol Engine | Builder |
|------|----------------|---------|
| Port 0 | Http10Engine | `BuildProtocolFlow<Http10Engine>(16, ActorRefs.Nobody, factory)` |
| Port 1 | Http11Engine | `BuildProtocolFlow<Http11Engine>(16, ActorRefs.Nobody, factory)` |
| Port 2 | Http20Engine | `BuildProtocolFlow<Http20Engine>(16, ActorRefs.Nobody, factory)` |

Each `BuildProtocolFlow` in test mode: `new TEngine().CreateFlow().Join(transportFactory())`
→ **No ConnectionStage used. Transport comes from the test factory.**

---

### Path 3: `BuildEngineCoreGraph` — Production Mode (http10Factory == null)

Partitions into 4 protocol lanes:

| Lane | Protocol Engine | maxSubstreams | Transport |
|------|----------------|---------------|-----------|
| Port 0 | Http10Engine | 256 | `ConnectionStage(poolRouter)` |
| Port 1 | Http11Engine | 256 | `ConnectionStage(poolRouter)` |
| Port 2 | Http20Engine | 64 | `ConnectionStage(poolRouter)` |
| Port 3 | Http30Engine | 32 | `ConnectionStage(poolRouter)` |

---

### Path 4: `BuildProtocolFlow` — Production substream wiring

Each protocol lane creates per-host substreams using `GroupBy(HostKey.FromRequest)`.

Within each substream (`BuildConnectionFlowPublic`):

| Component | What it does |
|-----------|-------------|
| `TEngine().CreateFlow()` (BidiFlow) | Protocol encoding/decoding |
| `ConnectionStage(poolRouter)` | TCP transport, contacts `PoolRouterActor` for socket refs |
| `Broadcast<HttpRequestMessage>(2)` | Fans out first request to (a) BidiFlow and (b) connectOnce |
| `connectOnce` (Take(1).Select ConnectItem) | Extracts URI on first request → creates `ConnectItem` |
| `Concat<IOutputItem>(2)` | Sends ConnectItem first, then BidiFlow data |
| `Buffer(1, Backpressure)` | Decouples Broadcast from BidiFlow (deadlock prevention) |

---

## Stage Inventory — Wired vs. Unused

| Stage | File | Used in Engine.cs? | Where |
|-------|------|--------------------|-------|
| RequestEnricherStage | Streams/Stages/RequestEnricherStage.cs | ✅ YES | ExtendedPipeline request chain |
| CookieInjectionStage | Streams/Stages/CookieInjectionStage.cs | ✅ YES | ExtendedPipeline request chain |
| CacheLookupStage | Streams/Stages/CacheLookupStage.cs | ✅ YES | ExtendedPipeline request chain |
| DecompressionStage | Streams/Stages/DecompressionStage.cs | ✅ YES | ExtendedPipeline response chain |
| CookieStorageStage | Streams/Stages/CookieStorageStage.cs | ✅ YES | ExtendedPipeline response chain |
| CacheStorageStage | Streams/Stages/CacheStorageStage.cs | ✅ YES | ExtendedPipeline response chain |
| RetryStage | Streams/Stages/RetryStage.cs | ✅ YES | ExtendedPipeline response chain |
| RedirectStage | Streams/Stages/RedirectStage.cs | ✅ YES | ExtendedPipeline response chain |
| ConnectionStage | IO/Stages/ConnectionStage.cs | ✅ YES | BuildConnectionFlowPublic (production) |
| ExtractOptionsStage | Streams/Stages/ExtractOptionsStage.cs | ❌ NO | File exists, not referenced in Engine.cs |
| PrependPrefaceStage | Streams/Stages/PrependPrefaceStage.cs | ❌ NO | Used inside Http20Engine internally |
| GroupByHostKeyStage | Streams/Stages/GroupByHostKeyStage.cs | ❌ NO | Engine uses `.GroupBy(HostKey.FromRequest)` directly |
| MergeSubstreamsStage | Streams/Stages/MergeSubstreamsStage.cs | ❌ NO | Engine uses `.MergeSubstreams()` directly |
| ConnectionReuseStage | Streams/Stages/ConnectionReuseStage.cs | ❌ NO | File exists, never wired in Engine.cs |
| Http10EncoderStage | Streams/Stages/Http10EncoderStage.cs | ✅ YES (indirect) | Used inside Http10Engine.CreateFlow() |
| Http10DecoderStage | Streams/Stages/Http10DecoderStage.cs | ✅ YES (indirect) | Used inside Http10Engine.CreateFlow() |
| Http11EncoderStage | Streams/Stages/Http11EncoderStage.cs | ✅ YES (indirect) | Used inside Http11Engine.CreateFlow() |
| Http11DecoderStage | Streams/Stages/Http11DecoderStage.cs | ✅ YES (indirect) | Used inside Http11Engine.CreateFlow() |
| Http20EncoderStage | Streams/Stages/Http20EncoderStage.cs | ✅ YES (indirect) | Used inside Http20Engine.CreateFlow() |
| Http20DecoderStage | Streams/Stages/Http20DecoderStage.cs | ✅ YES (indirect) | Used inside Http20Engine.CreateFlow() |
| Http20ConnectionStage | Streams/Stages/Http20ConnectionStage.cs | ✅ YES (indirect) | Used inside Http20Engine.CreateFlow() |
| Http20StreamStage | Streams/Stages/Http20StreamStage.cs | ✅ YES (indirect) | Used inside Http20Engine.CreateFlow() |
| StreamIdAllocatorStage | Streams/Stages/StreamIdAllocatorStage.cs | ✅ YES (indirect) | Used inside Http20Engine.CreateFlow() |
| Request2FrameStage | Streams/Stages/Request2FrameStage.cs | ✅ YES (indirect) | Used inside Http20Engine.CreateFlow() |
| Http1XCorrelationStage | Streams/Stages/Http1XCorrelationStage.cs | ✅ YES (indirect) | Used inside Http11Engine.CreateFlow() |
| Http20CorrelationStage | Streams/Stages/Http20CorrelationStage.cs | ✅ YES (indirect) | Used inside Http20Engine.CreateFlow() |

---

## Key Answers

### Does `Engine.cs` use `ConnectionPoolStage`?

**NO.** There is no `ConnectionPoolStage` class anywhere in the codebase (confirmed by grep). The term "Actor Pool" refers to `PoolRouterActor` / `HostPoolActor`, which are actors — not Akka.Streams stages. `ConnectionStage` is the Akka.Streams stage that bridges the actor pool to the stream graph.

### Is `ConnectionReuseStage` actively wired?

**NO.** `ConnectionReuseStage` exists at `Streams/Stages/ConnectionReuseStage.cs` and has tests in `TurboHttp.StreamTests/Streams/ConnectionReuseStageTests.cs`, but it is **never referenced in `Engine.cs`** or any production code path. It is dead code from an earlier design iteration.

---

## Architecture Notes

- **`PoolRouterActor`** IS used in production: passed as `poolRouter: IActorRef` to `BuildProtocolFlow`, which passes it to `ConnectionStage`. So the actor pool IS integrated — but via the `ConnectionStage` bridge, not via a stream stage called "ConnectionPoolStage".
- **`GroupByHostKeyStage`** and **`MergeSubstreamsStage`** exist as custom stages but Engine.cs uses the built-in Akka `.GroupBy()` / `.MergeSubstreams()` DSL extensions directly instead.
- **`ExtractOptionsStage`** exists but is not wired in Engine.cs. Its former role (splitting `HttpRequest(Options, Message)`) may have been superseded by the current `RequestEnricherStage` + `requestOptionsFactory` pattern.

---

## TASK-AUD-002 — Plan 4 Actor Pool: Integration Status Check

**Date:** 2026-03-15

### Grep Results

**`ConnectionPoolStage`** — grep in all of `src/`: **0 matches**. This class does not exist anywhere in the codebase. It was removed during Plan 4b (TASK-4B-004/006).

**`PoolRouterActor`** — found in the following production files:

| File | Usage |
|------|-------|
| `src/TurboHttp/IO/PoolRouterActor.cs` | Definition |
| `src/TurboHttp/IO/Stages/ConnectionStage.cs` | `ConnectionStage` sends `PoolRouterActor.GetPoolRefs` to obtain stream refs |
| `src/TurboHttp/Streams/Engine.cs` | `CreateFlow(IActorRef poolRouter, ...)` — all `BuildProtocolFlow` calls pass `poolRouter` → `new ConnectionStage(poolRouter)` |
| `src/TurboHttp/Client/TurboClientStreamManager.cs` | Creates `PoolRouterActor` via `system.ActorOf(Props.Create(() => new PoolRouterActor(clientOptions.PoolConfig)), "pool-router")` and passes it to `engine.CreateFlow(poolRouter, ...)` |

**`HostPoolActor`** — found in `src/TurboHttp/IO/HostPoolActor.cs` (definition) and spawned by `PoolRouterActor` internally when a `ConnectItem` arrives. No direct references in Engine.cs or TurboClientStreamManager.

### Answer: Is the Actor Pool used in Engine.cs or TurboHttpClient?

**YES — fully integrated.**

The complete production path is:

```
TurboHttpClient.SendAsync()
  → TurboClientStreamManager
    → system.ActorOf(Props.Create(() => new PoolRouterActor(poolConfig)))
    → engine.CreateFlow(poolRouter, clientOptions)
      → Engine.BuildExtendedPipeline(poolRouter, ...)
        → Engine.BuildEngineCoreGraph(poolRouter, ...)
          → Engine.BuildProtocolFlow<Http10|11|20|30Engine>(poolRouter)
            → Engine.BuildConnectionFlowPublic(poolRouter)
              → new ConnectionStage(poolRouter)
                → on-start: poolRouter.Tell(GetPoolRefs())
                → receives PoolRefs(SinkRef<ITransportItem>, SourceRef<IDataItem>)
                → stream items flow through SinkRef/SourceRef ↔ actor hierarchy
```

The actor hierarchy inside `PoolRouterActor`:
- `PoolRouterActor` receives `ConnectItem` → spawns `HostPoolActor` per host
- `HostPoolActor` receives `DataItem` → spawns `ConnectionActor` per TCP connection
- `ConnectionActor` → `ClientManager` → `ClientRunner` + `ClientByteMover` → TCP

### Assessment: "If no, what would need to happen?"

N/A — the Actor Pool is already integrated. No integration work is required.

### Test Coverage

Stream tests (non-test code verification):
- `src/TurboHttp.StreamTests/IO/ActorHierarchyStreamRefTests.cs` — ETE-001: full hierarchy `ConnectItem` via SinkRef → HostPoolActor spawned → DataItem → ConnectionActor spawned → TCP outbound
- `src/TurboHttp.StreamTests/Streams/ConnectionStageTests.cs` — CS-001, CS-002: `ConnectionStage` talks to stub router, items flow in both directions

---

## TASK-AUD-003 — Connection Reuse: Behavioural Check

**Date:** 2026-03-15

### Summary

| Question | Answer |
|----------|--------|
| HTTP/1.1 Keep-Alive (TCP reuse) | ❌ NOT empirically tested end-to-end |
| HTTP/2 multiplexing | ✅ Tested at stream/stage level (fake TCP, not real) |

---

### HTTP/1.1 Keep-Alive — Detailed Findings

#### What exists

1. **`ConnectionReuseEvaluator`** (`src/TurboHttp/Protocol/RFC9112/ConnectionReuseEvaluator.cs`) — protocol logic: evaluates `Connection: close` vs keep-alive per RFC 9112 §9.
2. **`ConnectionReuseStage`** (`src/TurboHttp/Streams/Stages/ConnectionReuseStage.cs`) — Akka.Streams stage wrapping the evaluator, passes decisions via `Action<ConnectionReuseDecision>` callback.
3. **10 unit tests** (`src/TurboHttp.StreamTests/Streams/ConnectionReuseStageTests.cs`) — cover HTTP/1.1 default keep-alive, `Connection: close`, HTTP/1.0 opt-in, body-not-consumed, 101 Switching Protocols, Keep-Alive timeout/max parsing.
4. **`Http11StageConnectionMgmtTests`** (`src/TurboHttp.StreamTests/Http11/Http11StageConnectionMgmtTests.cs`) — 5 tests that run 2–3 requests through the same `Http11Engine` materialization (same "connection" pipeline instance, using a fake TCP byte source).
5. **Kestrel routes** — `/conn/keep-alive`, `/conn/close`, `/conn/default`, `/conn/upgrade-101` are registered in `KestrelFixture` (via `Routes.RegisterConnectionReuseRoutes`).

#### Critical gap

**`ConnectionReuseStage` is NOT wired into `Engine.cs`.** The stage exists and is tested in isolation, but no production code path calls it. The decision callbacks (close vs. keep-alive) are never invoked in a real pipeline.

**No integration test class consumes the `/conn/*` routes.** The `src/TurboHttp.IntegrationTests/` directory contains only fixture/infrastructure files (`KestrelFixture.cs`, `KestrelH2Fixture.cs`, `KestrelTlsFixture.cs`, `Routes.cs`, `TestKit.cs`). There is no `Http11ConnectionTests` class or any other test class that sends real HTTP/1.1 requests to Kestrel and checks whether TCP connections are reused.

#### Verdict: keep-alive ❌

The stage-level tests prove the **protocol decision logic is correct** (RFC 9112 compliant header parsing), but there is no empirical test that proves TCP connections are actually kept open and reused across multiple requests in a real pipeline.

---

### HTTP/2 Multiplexing — Detailed Findings

#### What exists

1. **`Http20CorrelationStageTests`** (`COR20-002`) — sends requests with stream IDs 1, 3, 5 and receives responses in reverse order (5, 1, 3). All 3 are matched correctly. Tests the correlation stage in isolation with a fake source.
2. **`Http20EngineRfcRoundTripTests`** — `SendH2EngineAsyncMany` sends 3 requests through the same `Http20Engine` materialization, receives 3 `HeadersFrame` responses on streams 1, 3, 5. Uses `H2EngineFakeConnectionStage` — a fake TCP stage that serves pre-built H2 frame bytes.
3. **`SendH2ManyAsync` / `SendH2EngineAsyncMany`** in `EngineTestBase` — both helpers pipe multiple requests through a single engine instance (single fake TCP connection), proving the multiplexing at the protocol layer.

#### Critical gap

All H2 multiplexing tests use a **fake TCP stage** — byte arrays are served from an in-memory queue, not from a real TCP socket. There is no integration test that sends 2+ HTTP/2 requests to a real Kestrel server and confirms they share one TCP connection (e.g., by checking connection counts or stream IDs in the server's access log).

#### Verdict: http2-multiplex ✅ (at stream layer)

HTTP/2 multiplexing is tested at the stream/stage level: multiple requests are encoded with distinct odd stream IDs, sent through a single engine materialization, and responses are correctly correlated by stream ID — all on one fake TCP "connection". This validates the HTTP/2 protocol mechanics. Real TCP multiplexing is not empirically verified.

---

### Test File Search Results

Searched for integration test classes using `/conn/keep-alive` or `/conn/close` route: **0 matches** (outside of `Routes.cs` itself).

Searched for `Http11ConnectionTests`, `Http11BasicTests`, or any file consuming the connection routes: **not found**. The `Http10/`, `Http11/`, `Http20/`, `Shared/` subdirectories mentioned in MEMORY.md do not exist on disk in the `poc2` branch.

---

### Conclusion

- **Keep-alive: ❌** — `ConnectionReuseStage` is dead code (not in Engine.cs). No integration test proves TCP reuse.
- **HTTP/2 multiplex: ✅** — Multiplexing works at the protocol/stage layer (fake TCP). No real-TCP integration test.
- The Kestrel routes and the stage infrastructure are in place; what is missing is: (1) wiring `ConnectionReuseStage` into the Engine, and (2) writing integration test classes that exercise the `/conn/*` routes with real TCP.
