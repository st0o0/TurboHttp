# PRD: HTTP/2 Stream State Management (Phase 3.3)

## Introduction

Implement a fully RFC 7540 §5.1 compliant stream state machine for HTTP/2 as a standalone
`Http2StreamStateMachine` class in the Protocol Layer. The state machine tracks the lifecycle
of every HTTP/2 stream (idle → open → half-closed → closed), enforces stream ID rules, limits
concurrent streams via `SETTINGS_MAX_CONCURRENT_STREAMS`, and handles error conditions
including GOAWAY and ID exhaustion. This is a prerequisite for any higher-level HTTP/2
request/response processing.

---

## Scope: Client-Side Only

TurboHttp is a **pure HTTP client library**. Only two directions exist:

| Direction | Required |
|---|---|
| `HttpRequestMessage` → bytes (encode request to send) | ✅ In scope |
| bytes → `HttpResponseMessage` (decode received response) | ✅ In scope |
| bytes → `HttpRequestMessage` (server-side request parsing) | ❌ Out of scope |
| `HttpResponseMessage` → bytes (server-side response encoding) | ❌ Out of scope |

For the stream state machine this means: the client **opens** streams (odd IDs), **sends** HEADERS/DATA, and **receives** HEADERS/DATA/PUSH_PROMISE from the server. Server-initiated stream logic (even IDs, server PUSH_PROMISE sending) is not required.

## Goals

- Implement all valid stream state transitions from RFC 7540 §5.1
- Enforce stream ID rules (client: odd, server: even; no reuse)
- Enforce `SETTINGS_MAX_CONCURRENT_STREAMS` limit
- Return correct `Http2ErrorCode` values for all protocol violations
- Handle GOAWAY: reject new streams after receiving GOAWAY
- Handle stream ID exhaustion (no more IDs available)
- Achieve 100% coverage of RFC test IDs `7540-5.1-001` through `7540-5.1-008`
- All unit tests pass, ≥ 90% line coverage, ≥ 85% branch coverage

---

## User Stories

### US-001: Define stream state enum and valid transitions
**Description:** As a developer, I need a `StreamState` enum and a transition table so that
the state machine can validate every frame against the current state.

**Acceptance Criteria:**
- [ ] `StreamState` enum defined: `Idle`, `Open`, `HalfClosedLocal`, `HalfClosedRemote`,
      `ReservedLocal`, `ReservedRemote`, `Closed`
- [ ] Transition table (or `switch` logic) covers all transitions from RFC 7540 §5.1 Figure 2
- [ ] Invalid transitions return `Http2ErrorCode.ProtocolError` or `Http2ErrorCode.StreamClosed`
      as specified by the RFC (not thrown exceptions — see FR-7)
- [ ] Typecheck passes

---

### US-002: Implement Http2StreamStateMachine class
**Description:** As a developer, I need an `Http2StreamStateMachine` class that tracks all
active streams and validates incoming/outgoing frames against their current state.

**Acceptance Criteria:**
- [ ] Class `Http2StreamStateMachine` created in `TurboHttp/Protocol/Http2StreamStateMachine.cs`
- [ ] Constructor accepts `Http2PeerRole role` (Client or Server) and
      `int maxConcurrentStreams` (default 100, from `SETTINGS_MAX_CONCURRENT_STREAMS`)
- [ ] Method `TryOpenStream(int streamId, out Http2ErrorCode error) → bool` opens a new stream
      and validates the stream ID parity rule
- [ ] Method `TryTransition(int streamId, FrameType frameType, bool endStream,
      out Http2ErrorCode error) → bool` applies a frame to a stream and advances its state
- [ ] Method `GetState(int streamId) → StreamState` returns `Idle` for unknown stream IDs
- [ ] Method `Reset()` clears all stream state (used between connections)
- [ ] Typecheck passes

---

### US-003: Enforce stream ID rules (RFC 7540 §5.1.1)
**Description:** As a developer, I need the state machine to reject stream IDs that violate
RFC rules so that protocol errors are caught early.

**Acceptance Criteria:**
- [ ] Client-initiated streams must use odd IDs (1, 3, 5, …); even IDs → `ProtocolError`
      (RFC test `7540-5.1-008`)
- [ ] Server-initiated streams must use even IDs (2, 4, 6, …); odd IDs → `ProtocolError`
- [ ] Stream ID 0 is never valid for stream-specific frames → `ProtocolError`
- [ ] Stream IDs must be monotonically increasing; reusing a closed stream ID → `ProtocolError`
      (RFC test `7540-5.1-007`)
- [ ] Typecheck passes

---

### US-004: Enforce SETTINGS_MAX_CONCURRENT_STREAMS
**Description:** As a developer, I need the state machine to enforce the concurrent stream
limit so that a peer cannot open more streams than negotiated.

**Acceptance Criteria:**
- [ ] Opening a stream when `openStreamCount >= maxConcurrentStreams` → returns `false` with
      `Http2ErrorCode.RefusedStream`
- [ ] Closing a stream (half-closed → closed, or RST_STREAM) decrements the open count
- [ ] `maxConcurrentStreams` can be updated at runtime via
      `UpdateMaxConcurrentStreams(int newMax)` to handle SETTINGS frame exchanges
- [ ] Typecheck passes

---

### US-005: Handle GOAWAY and stream ID exhaustion
**Description:** As a developer, I need the state machine to respect GOAWAY frames and
detect stream ID exhaustion so that no new streams are created in illegal states.

**Acceptance Criteria:**
- [ ] Method `OnGoAwayReceived(int lastStreamId)` marks the connection as draining;
      any attempt to open a new stream after this → `Http2ErrorCode.RefusedStream`
- [ ] Streams with ID > `lastStreamId` that were idle are immediately considered `Closed`
- [ ] When the 31-bit stream ID space is exhausted (next ID would exceed 0x7FFFFFFF) →
      `TryOpenStream` returns `false` with `Http2ErrorCode.ProtocolError`
- [ ] Typecheck passes

---

### US-006: Write unit tests covering all RFC 7540 §5.1 test IDs
**Description:** As a developer, I need tests for every stream state scenario from the
RFC test matrix so that RFC compliance is verifiable.

**Acceptance Criteria:**
- [ ] Test file `TurboHttp.Tests/Http2StreamStateMachineTests.cs` created
- [ ] RFC test `7540-5.1-001`: Idle → Open on HEADERS send → state becomes Open
- [ ] RFC test `7540-5.1-002`: Open → HalfClosedLocal on outgoing END_STREAM
- [ ] RFC test `7540-5.1-003`: Open → HalfClosedRemote on incoming END_STREAM
- [ ] RFC test `7540-5.1-004`: HalfClosedLocal + incoming END_STREAM → Closed (and vice versa)
- [ ] RFC test `7540-5.1-005`: PUSH_PROMISE on idle stream → ReservedRemote
- [ ] RFC test `7540-5.1-006`: DATA on closed stream → `StreamClosed` error
- [ ] RFC test `7540-5.1-007`: Reuse of closed stream ID → `ProtocolError`
- [ ] RFC test `7540-5.1-008`: Client sends even stream ID → `ProtocolError`
- [ ] Tests for `SETTINGS_MAX_CONCURRENT_STREAMS` enforcement (open, close, refuse)
- [ ] Tests for GOAWAY handling (draining mode, last-stream-id filtering)
- [ ] Tests for stream ID exhaustion
- [ ] All tests pass: `dotnet test --filter ClassName=TurboHttp.Tests.Http2StreamStateMachineTests`

---

## Functional Requirements

- **FR-1:** `Http2StreamStateMachine` is a pure Protocol Layer class with no Akka or I/O
  dependencies. It is a plain C# class, thread-unsafe by design (callers synchronize).
- **FR-2:** Stream states follow exactly the state diagram in RFC 7540 §5.1 Figure 2.
  No additional states may be invented.
- **FR-3:** `TryOpenStream` validates: parity, monotonic increase, no reuse, GOAWAY draining,
  ID exhaustion, and concurrent stream limit — in this order.
- **FR-4:** `TryTransition` maps each `(currentState, frameType, direction, endStream)` tuple
  to either a new `StreamState` or an error code. Transitions must match the RFC table exactly.
- **FR-5:** Streams in `Closed` state are retained in the state dictionary long enough to
  detect stream ID reuse. A dictionary of `int → StreamState` is sufficient; entries for
  `Closed` streams must not be pruned unless the entire connection is reset.
- **FR-6:** `SETTINGS_MAX_CONCURRENT_STREAMS` counts streams that are `Open`,
  `HalfClosedLocal`, or `HalfClosedRemote`. `ReservedLocal`/`ReservedRemote` are not counted
  unless the peer negotiates otherwise (RFC 7540 §5.1.2 leaves this implementation-defined;
  use the three states above).
- **FR-7:** Errors are communicated via `out Http2ErrorCode error` return values, not
  exceptions. Callers decide whether to send RST_STREAM or GOAWAY.
- **FR-8:** The class must have a `IsGoingAway` bool property that the caller can check before
  writing new frames.

---

## Non-Goals

- Server-side request decoding (`bytes → HttpRequestMessage`) — client-only library
- Server-side response encoding (`HttpResponseMessage → bytes`) — client-only library
- Server-initiated stream opening (even stream IDs) — client never opens even-ID streams
- No Akka actor wrapping in this story — that is part of Phase 4.2 (Connection Management).
- No flow control / WINDOW_UPDATE tracking — that is Phase 3.4.
- No PRIORITY frame dependency tree — that is Phase 4.3.
- No persistence or serialization of stream state.
- No thread safety inside the class itself.

---

## Technical Considerations

- Existing types to reuse: `Http2ErrorCode` (in `Http2Frame.cs`), `FrameType` (in `Http2Frame.cs`),
  `SettingsParameter.MaxConcurrentStreams`.
- Use a `Dictionary<int, StreamState>` for stream tracking. Capacity hint: 128 initial entries.
- Use `int _nextExpectedClientId = 1` / `_nextExpectedServerId = 2` to enforce monotonic IDs.
- The `Http2PeerRole` enum (`Client`, `Server`) determines which ID parity is "local".
- Target framework: .NET 10.0 (matches project).

---

## Success Metrics

- All 8 RFC test IDs (`7540-5.1-001` – `7540-5.1-008`) covered by named tests
- ≥ 90% line coverage on `Http2StreamStateMachine.cs`
- ≥ 85% branch coverage on `Http2StreamStateMachine.cs`
- Zero exceptions escaping the public API (all errors via return values)
- `dotnet test` green with no skipped tests

---

## Open Questions

- Should `ReservedLocal` / `ReservedRemote` streams count against
  `SETTINGS_MAX_CONCURRENT_STREAMS`? Current decision: no (see FR-6). Revisit in Phase 4.1
  (Server Push).
- Should `Http2StreamStateMachine` expose an event/callback for stream closure to allow
  resource cleanup, or is polling `GetState` sufficient? Decision deferred to Phase 4.2.
