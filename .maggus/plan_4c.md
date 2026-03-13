# Plan 4c: Stage–Actor Communication via ITransportItem + Resilient HostPoolActor

## Introduction

The Akka.Streams pipeline contains two stages that currently cannot communicate with
`HostPoolActor`:

- **`ConnectionReuseStage`** — evaluates RFC 9112 §9 keep-alive headers but discards the
  decision via an `Action<>` callback that is never wired in production.
- **`ExtractOptionsStage`** — emits a hardcoded `TcpOptions { Host = "", Port = 0 }` that
  is never used with a real address.

Additionally, `HostPoolActor` drops in-flight requests on connection failure and never
replays them, and multiple stages call `FailStage` / `CompleteStage` on TCP-level errors,
which tears down the whole stream.

This plan wires these stages into the actor hierarchy using new `ITransportItem`
implementations as the lingua franca for stage-to-actor control messages, makes
`HostPoolActor` absorb connection failures transparently, and guarantees the stream
never fails or completes due to TCP errors.

---

## Goals

- `ConnectionReuseStage` emits a `ConnectionReuseHintItem : ITransportItem` on Out1 that
  flows to `HostPoolActor` and triggers `MarkNoReuse` on the right connection.
- `ExtractOptionsStage` extracts a real `TcpOptions` from the request URI via an injected
  factory.
- `ConnectionStage` handles `ConnectionReuseHintItem` in the transport stream and
  forwards it to `PoolRouterActor` (or injected hint-router actor) without touching TCP.
- `HostPoolActor` buffers in-flight `DataItem`s and replays them transparently after
  reconnect — callers never see a failure.
- No stage calls `FailStage` or `CompleteStage` due to a TCP-level error; only explicit
  upstream/downstream finish triggers stream termination.

---

## User Stories

### TASK-4C-001: Add `ConnectionReuseHintItem` ITransportItem
**Description:** As a stream designer, I want a dedicated `ITransportItem` record that
carries connection-reuse metadata so that the transport channel can carry control hints
alongside data.

**Acceptance Criteria:**
- [ ] `public record ConnectionReuseHintItem(string PoolKey, bool CanReuse, TimeSpan? KeepAliveTimeout = null, int? MaxRequests = null) : ITransportItem;` added to `ConnectionStage.cs` (or a companion `ConnectionHints.cs` in `IO/Stages/`)
- [ ] `ITransportItem.Key` default-interface implementation returns `HostKey.Default` (unchanged)
- [ ] Record is `public` so `ConnectionReuseStage` (in `Streams/Stages/`) can reference it
- [ ] Unit test: verify record equality and property round-trip
- [ ] Build succeeds, zero warnings

---

### TASK-4C-002: Refactor `ConnectionReuseStage` to FanOutShape
**Description:** As a pipeline architect, I want `ConnectionReuseStage` to expose two
outlets — Out0 for the response passthrough, Out1 for the reuse hint — so the hint can
be merged back into the transport stream before `ConnectionStage`.

**Acceptance Criteria:**
- [ ] Class signature changes to `GraphStage<FanOutShape<HttpResponseMessage, HttpResponseMessage, ITransportItem>>`
- [ ] Constructor gains a `string poolKey` parameter (used to populate `ConnectionReuseHintItem.PoolKey`)
- [ ] Old `Action<ConnectionReuseDecision> onDecision` callback is **removed** (replaced by the Out1 hint mechanism)
- [ ] On each inbound `HttpResponseMessage`:
  - evaluates `ConnectionReuseEvaluator.Evaluate(...)` as before
  - pushes response to Out0 unchanged
  - pushes `new ConnectionReuseHintItem(poolKey, decision.CanReuse, decision.KeepAliveTimeout, decision.MaxRequests)` to Out1
- [ ] `onUpstreamFailure` no longer calls `FailStage`; instead calls `CompleteStage` (stream graceful finish, not failure)
- [ ] Out0/Out1 backpressure handled independently: stage waits until both outlets have been pulled before pulling inlet
- [ ] Build succeeds, zero warnings

---

### TASK-4C-003: Update `ConnectionReuseStageTests` for FanOutShape
**Description:** As a developer, I want the existing 10 REUSE tests to pass against the
new FanOutShape API so that RFC 9112 §9 logic is still fully covered.

**Acceptance Criteria:**
- [ ] `RunAsync` helper updated: uses `GraphDsl` to wire Out0 → `Sink.Seq<HttpResponseMessage>` and Out1 → `Sink.Seq<ITransportItem>` (captured as `hints`)
- [ ] All 10 existing test cases (REUSE-001 through REUSE-010) pass with the new helper
- [ ] Assertions on `decisions` replaced by assertions on `hints` (cast to `ConnectionReuseHintItem`)
  - `CanReuse`, `KeepAliveTimeout`, `MaxRequests` verified through hint properties
- [ ] New test `REUSE-011`: Out1 emits exactly one hint per response (same count as Out0)
- [ ] New test `REUSE-012`: upstream failure causes `CompleteStage` (no `FailStage`) — Out0 and Out1 complete normally
- [ ] Build and tests pass

---

### TASK-4C-004: Fix `ExtractOptionsStage` — real TcpOptions via injected factory
**Description:** As a client user, I want the pipeline to connect to the actual server
specified in the request URI, not to `Host=""` and `Port=0`.

**Acceptance Criteria:**
- [ ] `ExtractOptionsStage` constructor gains `Func<Uri, TcpOptions> optionsFactory` parameter
- [ ] On first request: `Push(Out0, new ConnectItem(optionsFactory(request.RequestMessage.RequestUri!)))` — real host/port
- [ ] `onUpstreamFailure` no longer calls `FailStage`; calls `CompleteStage` instead
- [ ] `Engine.BuildProtocolFlow<TEngine>` passes `uri => TcpOptionsFactory.Build(uri, clientOptions)` as factory when creating the stage
- [ ] Test: `ExtractOptionsStageTests` (new file) — verify that `ConnectItem.Options.Host` matches the request URI host for an `http://example.com/path` request
- [ ] Test: verify `TlsOptions` (not plain `TcpOptions`) is returned for `https://` URIs (by checking `options is TlsOptions`)
- [ ] Build succeeds, zero warnings

---

### TASK-4C-005: `ConnectionStage` handles `ConnectionReuseHintItem`
**Description:** As a stream designer, I want `ConnectionStage` to intercept
`ConnectionReuseHintItem` items from the merged transport stream and forward them to the
hint-router actor without touching the TCP channel.

**Acceptance Criteria:**
- [ ] `ConnectionStage(IActorRef clientManager)` gains an optional second parameter: `IActorRef? hintRouter = null`
- [ ] `HandlePush()` gains a new switch case:
  ```csharp
  case ConnectionReuseHintItem hint:
      _hintRouter?.Tell(hint);
      Pull(_stage._inlet);
      break;
  ```
- [ ] `_hintRouter` stores the injected actor ref
- [ ] Hint items are **never** written to `_outboundWriter`
- [ ] Unit test `ConnectionStageHintTests` (new file):
  - Probe plays the role of `hintRouter`
  - Push a `ConnectionReuseHintItem` into the stage inlet before connect
  - Verify the probe receives the hint and the stage pulls for more
  - Verify a subsequent `DataItem` is buffered (not discarded) while disconnected
- [ ] Build succeeds, zero warnings

---

### TASK-4C-006: `PoolRouterActor` routes `ConnectionReuseHintItem` to `HostPoolActor`
**Description:** As a connection pool operator, I want `PoolRouterActor` to receive
`ConnectionReuseHintItem` messages and route them to the correct `HostPoolActor` by
pool key so that the right connection is marked no-reuse.

**Acceptance Criteria:**
- [ ] `PoolRouterActor` adds `Receive<ConnectionReuseHintItem>(HandleReuseHint)`
- [ ] `HandleReuseHint`: look up `_hosts[msg.PoolKey]`; if found → `host.Tell(msg)`; if not found → log warning and discard (never throw)
- [ ] Pool key format used in hint matches the pool key registered via `RegisterHost`; verify this in an actor unit test using `TestProbe`
- [ ] Unit test: router with two registered hosts forwards hint only to the matching host actor
- [ ] Build succeeds, zero warnings

---

### TASK-4C-007: `HostPoolActor` handles `ConnectionReuseHintItem`
**Description:** As a connection pool operator, I want `HostPoolActor` to react to
`ConnectionReuseHintItem` messages by marking the affected connection non-reusable, so
that future requests get a fresh connection.

**Acceptance Criteria:**
- [ ] `HostPoolActor` adds `Receive<ConnectionReuseHintItem>(HandleReuseHint)`
- [ ] `HandleReuseHint`:
  - If `msg.CanReuse == true` and `msg.MaxRequests` is set → update `PendingRequests` threshold (or ignore if not tracked, acceptable simplification)
  - If `msg.CanReuse == false` → call `MarkNoReuse()` on the first non-idle connection (the one that just responded); log at Debug level
  - If no active connections found → discard hint silently
- [ ] Unit tests:
  - `POOL-HINT-001`: `CanReuse=false` → `Find` returns a `ConnectionState` where `Reusable == false` after processing
  - `POOL-HINT-002`: `CanReuse=true` → no `ConnectionState` is modified
  - `POOL-HINT-003`: hint arrives when no connections exist → no exception
- [ ] Build succeeds, zero warnings

---

### TASK-4C-008: `HostPoolActor` takes ownership of `DataItem` until response confirmed
**Description:** As a connection pool operator, I want `HostPoolActor` to retain the
`DataItem` reference (and its `IMemoryOwner<byte>`) in `PendingItem` until the
corresponding `ConnectionResponse` is received, so that in-flight bytes can be replayed
on reconnect.

**Note:** This is a prerequisite for TASK-4C-009 (transparent replay). The ownership
model change ensures that `ConnectionActor.HandleSend` does **not** call `data.Memory.Dispose()`
on channel write failure — it is the pool actor that owns the memory and is responsible
for eventual disposal.

**Acceptance Criteria:**
- [ ] `PendingItem` is updated: `readonly record struct PendingItem(DataItem Data, PendingReplyTo Pending)` — `DataItem` is stored by reference in `_replyToMap` alongside `PendingReplyTo`
  - Implementation: change `_replyToMap` value type to `Queue<(DataItem Data, PendingReplyTo Pending)>` (tuple queue)
- [ ] `SendToConnection` enqueues `(data, pending)` in the tuple queue, **not** a separate `DataItem`
- [ ] `HandleResponse`: dequeue one `(data, pending)` tuple; call `data.Memory.Dispose()` after reply is sent (memory released once acknowledged)
- [ ] `ConnectionActor.HandleSend`: remove `data.Memory.Dispose()` on `TryWrite` failure — the actor does not own the memory; log a warning instead
- [ ] `PostStop` on `HostPoolActor`: dispose all `DataItem`s still in `_replyToMap` queues
- [ ] Unit test: after `HandleResponse`, `DataItem.Memory` is disposed (verify with a custom `IMemoryOwner<byte>` that tracks disposal)
- [ ] Build succeeds, zero warnings

---

### TASK-4C-009: `HostPoolActor` transparent reconnect — replay in-flight items
**Description:** As a pipeline consumer, I want connection failures to be invisible: in-flight
requests must be re-enqueued and replayed on the new connection, so callers never see a
response gap.

**Acceptance Criteria:**
- [ ] `HandleFailure` (triggered by `ConnectionFailed`):
  - Retrieves in-flight `(DataItem, PendingReplyTo)` tuples from `_replyToMap[conn]`
  - Prepends them (in order) to the front of `_pending` — they are replayed before new arrivals
  - Removes the dead connection from `_connections` and `_replyToMap`
  - Schedules `Reconnect` as before
- [ ] `HandleReconnect`: spawns new `ConnectionActor`; calls `DrainPending()` immediately after spawn (the replayed items are now first in `_pending`)
- [ ] If `DrainPending` selects the new connection: items are sent normally; `_replyToMap` for the new connection is populated
- [ ] Unit test `POOL-RECONNECT-001`:
  - Send 2 requests to a connection; simulate `ConnectionFailed`
  - Verify both `DataItem`s are re-enqueued in `_pending` and eventually sent to the new connection
- [ ] Unit test `POOL-RECONNECT-002`:
  - Send 3 requests; 1 has already received a response (removed from queue); simulate `ConnectionFailed`
  - Verify only 2 in-flight items are replayed (not the already-acked one)
- [ ] Build succeeds, zero warnings

---

### TASK-4C-010: Remove `FailStage` on TCP errors from all stages
**Description:** As a stream consumer, I want the Akka.Streams pipeline to never fail
or complete due to a TCP-level error, so that the stream stays alive for the full
application lifetime.

**Affected stages:**
- `ConnectionReuseStage` — already covered in TASK-4C-002
- `ExtractOptionsStage` — already covered in TASK-4C-004
- Any other stage in `Streams/Stages/` that calls `FailStage` from an upstream-failure handler

**Acceptance Criteria:**
- [ ] Grep all `.cs` files under `src/TurboHttp/Streams/Stages/` for `onUpstreamFailure: FailStage`
- [ ] For each match: replace `FailStage` with `CompleteStage` so the stage completes cleanly instead of propagating failure downstream
- [ ] `ConnectionStage` (in `IO/Stages/`): `onUpstreamFinish` and `onDownstreamFinish` already call `CompleteStage` — verify no `FailStage` call exists there; if found, replace
- [ ] All replaced stages have a comment: `// TCP-level errors must not tear down the stream — complete gracefully`
- [ ] Unit test for each changed stage: send an `UpstreamFailure` signal; verify the stage emits `OnComplete` (not `OnError`) downstream
- [ ] Build succeeds, zero warnings

---

### TASK-4C-011: Wire `ConnectionReuseStage` hint merge into `BuildProtocolFlow<TEngine>`
**Description:** As a pipeline architect, I want the hint from `ConnectionReuseStage.Out1`
to be merged back into the transport stream that feeds `ConnectionStage`, so the hint
reaches the actor hierarchy without breaking backpressure.

**Target topology inside `BuildProtocolFlow<TEngine>` (per-version protocol flow):**
```
[HttpRequestMessage]
        ↓
[TEngine BidiFlow (encoder + correlation + decoder)]
        ↓ transport out (ITransportItem: DataItem/ConnectItem)     ↓ response out (HttpResponseMessage)
        ↓                                                           ↓
        │                                             [ConnectionReuseStage]
        │                                              Out0 ↓        Out1 ↓ (ConnectionReuseHintItem)
[Merge<ITransportItem>(2)] ←─────────────────────────────────────────────
        ↓
[ConnectionStage(clientManager, hintRouter: poolRouterActor)]
        ↓ (IDataItem, bytes from TCP)
[TEngine BidiFlow (decoder/response side)]
```

**Acceptance Criteria:**
- [ ] `BuildProtocolFlow<TEngine>` accepts a new parameter `IActorRef? hintRouter = null`
- [ ] Inside the graph DSL:
  - `ConnectionReuseStage` is added for the appropriate HTTP version with the pool key derived from `clientOptions.BaseAddress` (or a placeholder pool key `""` when `BaseAddress` is null)
  - `Merge<ITransportItem>(2)` merges Out1 of `ConnectionReuseStage` (hint) with the existing transport stream
  - The merged stream feeds `ConnectionStage(clientManager, hintRouter)`
- [ ] `Engine.CreateFlow(...)` passes `poolRouterActor` (or `ActorRefs.Nobody` in test mode) as `hintRouter`
- [ ] All existing engine tests still pass (hint path is additive, not breaking)
- [ ] Stream test: in an end-to-end `Http11Engine` test, verify that after a `Connection: close` response, a `ConnectionReuseHintItem` with `CanReuse=false` is received by a `TestProbe` wired as `hintRouter`
- [ ] Build succeeds, zero warnings

---

### TASK-4C-012: `ExtractOptionsStage` wired into protocol flows
**Description:** As a pipeline architect, I want `ExtractOptionsStage` to be wired into
the per-version protocol flows in `Engine.cs` so that real `TcpOptions` (with correct
host/port/TLS) are sent to `ConnectionStage` on the first request.

**Note:** In the current design `ExtractOptionsStage` is defined but never used in
`BuildProtocolFlow`. This task wires it in.

**Acceptance Criteria:**
- [ ] `BuildProtocolFlow<TEngine>` (or a helper it calls) adds `ExtractOptionsStage` with the factory `uri => TcpOptionsFactory.Build(uri, clientOptions)` before the transport outlet
- [ ] The `ConnectItem` emitted by `ExtractOptionsStage.Out0` is merged into the transport stream **before** `ConnectionStage` (alongside `DataItem`s from the encoder)
- [ ] The `HttpRequestMessage` from `ExtractOptionsStage.Out1` feeds the encoder
- [ ] Stream test: first request to `http://127.0.0.1:5000/ping` results in a `ConnectItem` with `Options.Host = "127.0.0.1"` and `Options.Port = 5000`
- [ ] Build succeeds, zero warnings

---

### TASK-4C-013: Validation Gate
**Description:** As a developer, I want to confirm that all plan-4c changes are green,
no regressions were introduced, and the stream-never-fail guarantee is documented.

**Acceptance Criteria:**
- [ ] `dotnet build --configuration Release src/TurboHttp.sln` → 0 errors, 0 warnings
- [ ] `dotnet test src/TurboHttp.Tests/TurboHttp.Tests.csproj` → all tests pass (≥2158)
- [ ] `dotnet test src/TurboHttp.StreamTests/TurboHttp.StreamTests.csproj` → all tests pass (≥421)
- [ ] `dotnet test src/TurboHttp.IntegrationTests/TurboHttp.IntegrationTests.csproj` → all tests pass (≥234)
- [ ] New tests from TASK-4C-001 through TASK-4C-012 all present and green
- [ ] No `FailStage` call remains in `src/TurboHttp/Streams/Stages/` (grep confirms zero matches)
- [ ] `CLAUDE.md` "Current Limitations" section updated: remove bullet about `ExtractOptionsStage` stub and add note that `ConnectionReuseStage` hint path is wired

---

## Functional Requirements

- FR-1: `ConnectionReuseHintItem` must implement `ITransportItem` so it can flow through the same channel as `DataItem` and `ConnectItem` without additional type unions.
- FR-2: `ConnectionReuseStage` Out0 and Out1 must both receive a signal per inbound response; the stage must not drop either output due to backpressure from the other.
- FR-3: `ExtractOptionsStage` must derive TLS vs. plain TCP from the request URI scheme without the caller having to specify it.
- FR-4: `ConnectionStage` must pull the inlet after forwarding a `ConnectionReuseHintItem` so backpressure is released immediately; hint items must not be written to the TCP channel.
- FR-5: `PoolRouterActor` routing of `ConnectionReuseHintItem` must be key-exact; an unknown pool key must be logged and discarded, not thrown.
- FR-6: `HostPoolActor.HandleFailure` must re-enqueue all in-flight `DataItem`s into `_pending` before scheduling `Reconnect`, preserving original order.
- FR-7: The reconnecting `HostPoolActor` must call `DrainPending()` immediately after the new `ConnectionActor` is spawned, not wait for an external trigger.
- FR-8: Memory ownership of `DataItem.Memory` must rest with `HostPoolActor` until `ConnectionResponse` is received; `ConnectionActor` must not dispose the memory.
- FR-9: No stage in `TurboHttp/Streams/Stages/` may call `FailStage` in its `onUpstreamFailure` handler; all such handlers must call `CompleteStage` instead.
- FR-10: The pool key passed in `ConnectionReuseHintItem` must match the key used in `PoolRouterActor.RegisterHost` and `SendRequest` for the same host.

---

## Non-Goals

- HTTP/2 multiplexing connection reuse decisions (stream-level hints) — HTTP/2 streams already managed by `Http20ConnectionStage`; this plan covers HTTP/1.x only.
- Changing the binary encoding of `DataItem` or `IMemoryOwner` pooling strategy.
- Adding circuit-breaker logic or maximum reconnect attempt limits (that belongs in plan_5).
- Persisting in-flight requests to disk on crash.
- Changing the public `TurboHttpClient` or `ITurboHttpClient` API.

---

## Technical Considerations

- **FanOutShape backpressure**: `FanOutShape<TIn, TOut0, TOut1>` requires independent demand tracking. The stage must track `_out0Demanded` and `_out1Demanded` booleans; it pulls the inlet only when **both** are true (or buffer one while waiting for the other). See `CacheLookupStage` for reference.
- **Pool key format**: Establish a canonical format `$"{scheme}://{host}:{port}"` and use it consistently in `ExtractOptionsStage`, `ConnectionReuseStage`, and the tests. A private helper method `TcpOptionsFactory.PoolKeyOf(Uri)` is the right home.
- **`DataItem` replay + `IMemoryOwner` lifetime**: `IMemoryOwner<byte>` from `ArrayPool<byte>` is not reference-counted. Once the backing array is returned, the bytes are invalid. Since `ConnectionActor` calls `_outbound.TryWrite((data.Memory, data.Length))` which copies the `IMemoryOwner` reference (not the bytes), HostPoolActor retaining the `DataItem` ref is sufficient — the array is not freed until `HostPoolActor` calls `data.Memory.Dispose()`.
- **`DrainPending` after reconnect**: Currently `DrainPending` selects a connection via `SelectConnection`. The new connection may not yet be `Active=true` if it hasn't received `ClientConnected`. To avoid dropped items, `HandleReconnect` should first try `DrainPending`; if no connection is available the items stay in `_pending` and will drain via the normal `HandleIdle` path once the connection reports ready.
- **Test isolation**: `ConnectionActor` and `HostPoolActor` tests should use `TestKit` with `TestProbe` for child actors; avoid real TCP in unit tests for these actors.

---

## Success Metrics

- Zero `FailStage` calls in `src/TurboHttp/Streams/Stages/` after the plan is complete.
- All 13 REUSE tests pass (10 updated + 2 new).
- All existing stream tests pass (≥421).
- A `Connection: close` response round-trip in a stream test results in a `ConnectionReuseHintItem` reaching a `TestProbe` wired as `hintRouter`.
- A simulated `ConnectionFailed` in a `HostPoolActor` unit test results in two re-enqueued `DataItem`s visible in `_pending` before the new connection is spawned.

---

## Open Questions

- Should `HostPoolActor` emit a log event when it replays in-flight items, for observability? (Recommended: `Debug` level with item count.)
- Should `ConnectionReuseStage` also emit a hint with `CanReuse=true` and `MaxRequests` to let `HostPoolActor` track per-connection request budgets? (Out of scope for now; `MaxRequests` is already in the hint record for future use.)
- Is `ExtractOptionsStage` the right place to handle per-request proxy settings in the future? (Noted for plan_5.)
