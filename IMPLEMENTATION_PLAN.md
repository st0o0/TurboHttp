# Implementation Plan — StreamTests Rewrite

## Goal

Delete the existing ad-hoc `TurboHttp.StreamTests` project (3 files, 24 tests) and replace
it with a structured suite covering every production `GraphStage`, BidiFlow engine, version
routing, and connection pool.  Tests are grouped by protocol layer, each phase is small and
independently buildable.  RFC-tagged `DisplayName` attributes tie each test to a spec section.

---

## Phases

---

### Phase 0 — Delete old files + create shared helpers
- [x] **Status**: done

**What to do**:
1. Delete `src/TurboHttp.StreamTests/EngineTests.cs`
2. Delete `src/TurboHttp.StreamTests/FakeConnectionStage.cs`
3. Delete `src/TurboHttp.StreamTests/HostConnectionPoolFlowTests.cs`
4. Create `src/TurboHttp.StreamTests/Shared/StreamTestBase.cs`:
   - `StreamTestBase : TestKit` (abstract)
   - `IMaterializer Materializer` property
   - `ActorSystem.Create("st-" + Guid.NewGuid())`
5. Create `src/TurboHttp.StreamTests/Shared/FakeTcpFlow.cs`:
   - `FakeTcpFlow` sealed class wrapping `GraphStage<FlowShape<(IMemoryOwner<byte>,int),(IMemoryOwner<byte>,int)>>`
   - Constructor takes `Func<byte[]> responseFactory`
   - `Channel<(IMemoryOwner<byte>, int)> Captured` for asserting outbound bytes
   - Echo variant: `FakeTcpFlow.Echo()` — reflects bytes back unchanged
6. Create `src/TurboHttp.StreamTests/Shared/SimpleMemoryOwner.cs`:
   - Move `SimpleMemoryOwner` from old `EngineTests.cs`
7. Create `src/TurboHttp.StreamTests/Shared/Http2FrameBuilder.cs`:
   - `BuildServerPreface()` → SETTINGS frame (stream 0, empty payload)
   - `BuildSettingsAck()` → SETTINGS+ACK frame
   - `BuildHeadersFrame(int streamId, byte[] hpackBlock, bool endStream)` → raw bytes
   - `BuildDataFrame(int streamId, byte[] body, bool endStream)` → raw bytes

**Acceptance criteria**:
- All 4 shared files compile
- `dotnet test src/TurboHttp.StreamTests/` → 0 failures (0 tests is fine at this stage)

---

### Phase 1 — `Http10EncoderStageTests.cs` (RFC 1945 request wire format)
- [x] **Status**: done

**Prerequisite**: Phase 0 complete.

**File**: `src/TurboHttp.StreamTests/Http10/Http10EncoderStageTests.cs`

**Test pattern**: `Source.Single(request).Via(Flow.FromGraph(new Stages.Http10EncoderStage())).RunWith(Sink.First<...>(), Materializer)`

**Tests**:

| ID | DisplayName | What it verifies |
|----|-------------|-----------------|
| ST-10-ENC-001 | RFC-1945-§5.1: Request-Line is `METHOD SP path SP HTTP/1.0 CRLF` | First line of encoded bytes |
| ST-10-ENC-002 | RFC-1945-§7.1: Custom header is forwarded verbatim | Raw bytes contain `X-Custom: value\r\n` |
| ST-10-ENC-003 | RFC-1945-§D.1: No `Host` header emitted | Raw bytes do NOT contain `Host:` |
| ST-10-ENC-004 | RFC-1945-§7.1: No `Connection` header emitted even when set on request | Filtered out |
| ST-10-ENC-005 | RFC-1945-§D.1: POST body bytes follow headers after double-CRLF | Body present after `\r\n\r\n` |
| ST-10-ENC-006 | RFC-1945-§D.1: `Content-Length` header present for POST body | `Content-Length: N` in raw |

**Acceptance criteria**:
- 6 tests compile and pass
- `dotnet test src/TurboHttp.StreamTests/` → 0 failures

---

### Phase 2 — `Http10DecoderStageTests.cs` (RFC 1945 response decoding)
- [x] **Status**: done

**Prerequisite**: Phase 1 complete.

**File**: `src/TurboHttp.StreamTests/Http10/Http10DecoderStageTests.cs`

**Test pattern**: push raw bytes into `Stages.Http10DecoderStage` via `Source.From(chunks).Via(...).RunWith(Sink.First<HttpResponseMessage>(), Materializer)`

**Tests**:

| ID | DisplayName | What it verifies |
|----|-------------|-----------------|
| ST-10-DEC-001 | RFC-1945-§6.1: Status-Line decoded to `StatusCode` and `Version` | 200 OK → `HttpStatusCode.OK`, `Version10` |
| ST-10-DEC-002 | RFC-1945-§7.1: Response header decoded to `response.Headers` | `X-Custom: test` accessible |
| ST-10-DEC-003 | RFC-1945-§7.2: Body delimited by `Content-Length` decoded correctly | 5-byte body exact |
| ST-10-DEC-004 | RFC-1945-§6.1: 404 response decoded to `HttpStatusCode.NotFound` | Status code propagated |
| ST-10-DEC-005 | RFC-1945-§7.2: Response split across two TCP chunks reassembled | Fragmented input → single response |

**Acceptance criteria**:
- 5 tests compile and pass

---

### Phase 3 — `Http10EngineTests.cs` (BidiFlow round-trips)
- [x] **Status**: done

**Prerequisite**: Phase 2 complete.

**File**: `src/TurboHttp.StreamTests/Http10/Http10EngineTests.cs`

**Pattern**: `new Http10Engine().CreateFlow().Join(new FakeTcpFlow(responseFactory))` then `Source.Single(req).Via(flow).RunWith(Sink.ForEach(...))`

**Tests**:

| ID | DisplayName | What it verifies |
|----|-------------|-----------------|
| ST-10-ENG-001 | Simple GET returns 200 with correct version | `response.StatusCode == OK`, `response.Version == Version10` |
| ST-10-ENG-002 | POST with body returns 200 | Body bytes transmitted, response decoded |
| ST-10-ENG-003 | Response body available via `Content.ReadAsByteArrayAsync` | Non-empty body decoded |
| ST-10-ENG-004 | 404 response decoded correctly | `response.StatusCode == NotFound` |
| ST-10-ENG-005 | Three sequential requests all return 200 | `SendManyAsync` with 3 requests |
| ST-10-ENG-006 | Request with custom header passes through | Header visible in captured outbound bytes |

**Acceptance criteria**:
- 6 tests compile and pass
- Total passing: 17

---

### Phase 4 — `Http11EncoderStageTests.cs` (RFC 9112 request wire format)
- [x] **Status**: done

**Prerequisite**: Phase 3 complete.

**File**: `src/TurboHttp.StreamTests/Http11/Http11EncoderStageTests.cs`

**Tests**:

| ID | DisplayName | What it verifies |
|----|-------------|-----------------|
| ST-11-ENC-001 | RFC-9112-§3.1: Request-Line is `METHOD SP path SP HTTP/1.1 CRLF` | First line |
| ST-11-ENC-002 | RFC-9112-§7.2: `Host` header is emitted for HTTP/1.1 requests | `Host: example.com` present |
| ST-11-ENC-003 | RFC-9112-§6.1: POST with known body has `Content-Length` or `Transfer-Encoding: chunked` | At least one framing header |
| ST-11-ENC-004 | RFC-9112-§7.6.1: `Connection` header is suppressed | Not forwarded to wire |
| ST-11-ENC-005 | RFC-9112-§3.1: Custom request header forwarded verbatim | `X-Custom: value` in raw |

**Acceptance criteria**:
- 5 tests compile and pass
- Total passing: 22

---

### Phase 5 — `Http11DecoderStageTests.cs` (RFC 9112 response decoding)
- [x] **Status**: done

**Prerequisite**: Phase 4 complete.

**File**: `src/TurboHttp.StreamTests/Http11/Http11DecoderStageTests.cs`

**Tests**:

| ID | DisplayName | What it verifies |
|----|-------------|-----------------|
| ST-11-DEC-001 | RFC-9112-§4: Status-Line decoded to `StatusCode` and `Version11` | 200 OK |
| ST-11-DEC-002 | RFC-9112-§6.1: `Content-Length` body decoded correctly | 5-byte body |
| ST-11-DEC-003 | RFC-9112-§7.1: Chunked body decoded correctly | `Transfer-Encoding: chunked` payload assembled |
| ST-11-DEC-004 | RFC-9112-§4: Two pipelined responses decoded as two messages | 2 messages out of 1 Source |
| ST-11-DEC-005 | RFC-9112-§4: Response header decoded to `response.Headers` | Custom header accessible |
| ST-11-DEC-006 | RFC-9112-§6.1: Response split across three TCP chunks reassembled | Fragmentation tolerance |

**Acceptance criteria**:
- 6 tests compile and pass
- Total passing: 28

---

### Phase 6 — `Http11EngineTests.cs` (BidiFlow round-trips)
- [x] **Status**: done

**Prerequisite**: Phase 5 complete.

**File**: `src/TurboHttp.StreamTests/Http11/Http11EngineTests.cs`

**Tests**:

| ID | DisplayName | What it verifies |
|----|-------------|-----------------|
| ST-11-ENG-001 | Simple GET returns 200 with `Version11` | Basic round-trip |
| ST-11-ENG-002 | POST with body returns 200 | Body transmitted |
| ST-11-ENG-003 | Response with chunked body decoded | Body accessible |
| ST-11-ENG-004 | Three pipelined requests all return 200 | Pipeline via `SendManyAsync` |
| ST-11-ENG-005 | Custom header forwarded to wire | `fake.Captured` contains header |

**Acceptance criteria**:
- 5 tests compile and pass
- Total passing: 33

---

### Phase 7 — `PrependPrefaceStageTests.cs` (RFC 9113 §3.5)
- [x] **Status**: done

**Prerequisite**: Phase 6 complete.

**File**: `src/TurboHttp.StreamTests/Http20/PrependPrefaceStageTests.cs`

**Tests**:

| ID | DisplayName | What it verifies |
|----|-------------|-----------------|
| ST-20-PRE-001 | RFC-9113-§3.5: First 24 bytes are exactly the connection preface magic | `"PRI * HTTP/2.0\r\n\r\nSM\r\n\r\n"` |
| ST-20-PRE-002 | RFC-9113-§3.5: Bytes 24..32 are a SETTINGS frame header (type=0x4, stream=0) | Frame type byte and stream ID |
| ST-20-PRE-003 | RFC-9113-§3.5: Second element passed through unchanged after preface emitted | Subsequent bytes pass through |
| ST-20-PRE-004 | RFC-9113-§3.5: Preface emitted exactly once (not repeated for second demand) | Only one preface chunk in output |

**Acceptance criteria**:
- 4 tests compile and pass
- Total passing: 37

---

### Phase 8 — `Http2FrameEncoderStageTests.cs` (RFC 9113 frame serialization)
- [x] **Status**: done

**Prerequisite**: Phase 7 complete.

**File**: `src/TurboHttp.StreamTests/Http20/Http2FrameEncoderStageTests.cs`

**Tests**:

| ID | DisplayName | What it verifies |
|----|-------------|-----------------|
| ST-20-FENC-001 | RFC-9113-§4.1: HEADERS frame has 9-byte header + HPACK payload | Total length ≥ 9, type byte = 0x1 |
| ST-20-FENC-002 | RFC-9113-§4.1: DATA frame has 9-byte header + body payload | Type byte = 0x0 |
| ST-20-FENC-003 | RFC-9113-§4.1: Stream ID field is encoded big-endian in bytes 5–8 | Stream ID 1 → `00 00 00 01` |
| ST-20-FENC-004 | RFC-9113-§4.2: Payload length field matches actual payload size | 3-byte length field correct |

**Acceptance criteria**:
- 4 tests compile and pass
- Total passing: 41

---

### Phase 9 — `Http2FrameDecoderStageTests.cs` (RFC 9113 fragmentation)
- [x] **Status**: done

**Prerequisite**: Phase 8 complete.

**File**: `src/TurboHttp.StreamTests/Http20/Http2FrameDecoderStageTests.cs`

**Tests**:

| ID | DisplayName | What it verifies |
|----|-------------|-----------------|
| ST-20-FDEC-001 | RFC-9113-§4.1: Single complete frame decoded correctly | HEADERS frame → `HeadersFrame` object |
| ST-20-FDEC-002 | RFC-9113-§4.1: Frame split across two TCP chunks reassembled | Fragment + remainder = one frame |
| ST-20-FDEC-003 | RFC-9113-§4.1: Two frames in one TCP chunk each decoded | Multi-frame chunk split correctly |
| ST-20-FDEC-004 | RFC-9113-§4.1: SETTINGS frame (stream 0) decoded | `SettingsFrame` with correct params |
| ST-20-FDEC-005 | RFC-9113-§4.1: DATA frame decoded with correct stream ID and payload | `DataFrame.StreamId` and payload match |

**Acceptance criteria**:
- 5 tests compile and pass
- Total passing: 46

---

### Phase 10 — `Request2Http2FrameStageTests.cs` (pseudo-headers + HPACK)
- [x] **Status**: done

**Prerequisite**: Phase 9 complete.

**File**: `src/TurboHttp.StreamTests/Http20/Request2Http2FrameStageTests.cs`

**Tests**:

| ID | DisplayName | What it verifies |
|----|-------------|-----------------|
| ST-20-REQ-001 | RFC-9113-§8.3.1: Emits HEADERS frame with `:method` pseudo-header | HPACK block contains `:method` |
| ST-20-REQ-002 | RFC-9113-§8.3.1: Emits `:path`, `:scheme`, `:authority` pseudo-headers | All 4 required pseudo-headers present |
| ST-20-REQ-003 | RFC-9113-§8.1: Stream IDs are odd and strictly ascending (1, 3, 5…) | IDs of two sequential requests |
| ST-20-REQ-004 | RFC-9113-§8.1: POST request emits HEADERS then DATA frame | Two-frame sequence for body request |
| ST-20-REQ-005 | RFC-9113-§8.3.1: GET request has END_STREAM flag set on HEADERS frame | Flags byte bit 0 set |

**Acceptance criteria**:
- 5 tests compile and pass
- Total passing: 51

---

### Phase 11 — `Http20EngineTests.cs` — single stream (ST-20-ENG-001..004)
- [ ] **Status**: pending

**Prerequisite**: Phase 10 complete.

**File**: `src/TurboHttp.StreamTests/Http20/Http20EngineTests.cs`

**Fake server setup**: Use `Http2FrameBuilder` helpers to construct server preface + SETTINGS-ACK + HEADERS response frame.

**Tests**:

| ID | DisplayName | What it verifies |
|----|-------------|-----------------|
| ST-20-ENG-001 | RFC-9113: Simple GET returns 200 with `Version20` | End-to-end BidiFlow round-trip |
| ST-20-ENG-002 | RFC-9113-§3.5: Outbound bytes start with 24-byte connection preface | First captured bytes |
| ST-20-ENG-003 | RFC-9113-§8.3.1: HEADERS frame with 4 pseudo-headers encoded | Raw HPACK block content |
| ST-20-ENG-004 | RFC-9113: Response body available via `Content.ReadAsByteArrayAsync` | DATA frame payload decoded |

**Acceptance criteria**:
- 4 tests compile and pass
- Total passing: 55

---

### Phase 12 — `Http20EngineTests.cs` — multi-stream + SETTINGS ACK (ST-20-ENG-005..008)
- [ ] **Status**: pending

**Prerequisite**: Phase 11 complete.

**Tests**:

| ID | DisplayName | What it verifies |
|----|-------------|-----------------|
| ST-20-ENG-005 | RFC-9113-§6.5: SETTINGS frame from server elicits SETTINGS-ACK outbound | ACK bytes (type=0x4, flags=0x1) present |
| ST-20-ENG-006 | RFC-9113-§5.1: N concurrent streams produce N responses in stream-ID order | Stream IDs 1,3,5 → 3 responses |
| ST-20-ENG-007 | RFC-9113-§10.3: Content-Encoding gzip response is decompressed | Body matches original plaintext |
| ST-20-ENG-008 | RFC-9113: Second request uses next odd stream ID (3) | Captured HEADERS stream ID = 3 |

**Acceptance criteria**:
- 4 tests compile and pass
- `Http20EngineTests.cs` has 8 tests total (ST-20-ENG-001..008)
- Total passing: 59

---

### Phase 13 — `EngineRoutingTests.cs` — version routing (ST-ENG-001..003)
- [ ] **Status**: pending

**File**: `src/TurboHttp.StreamTests/Engine/EngineRoutingTests.cs`

**Context**: `Engine.cs` uses `Partition → Http*Engine → Merge`.  No existing test verifies that each HTTP version is routed to the correct sub-engine.

**Pattern**: Inject a `FakeEngine` per version (echoes `x-correlation-id` + sets `response.Version`) then wrap in the same Partition/Merge graph structure as `Engine.cs`.

**Tests**:

| ID | DisplayName | What it verifies |
|----|-------------|-----------------|
| ST-ENG-001 | HTTP/1.0 request routed to HTTP/1.0 engine | `response.Version == Version10` |
| ST-ENG-002 | HTTP/1.1 request routed to HTTP/1.1 engine | `response.Version == Version11` |
| ST-ENG-003 | HTTP/2.0 request routed to HTTP/2.0 engine | `response.Version == Version20` |

**Acceptance criteria**:
- 3 tests compile and pass
- Total passing: 62

---

### Phase 14 — `EngineRoutingTests.cs` — concurrency + edge cases (ST-ENG-004..006)
- [ ] **Status**: pending

**Prerequisite**: Phase 13 complete.

**Tests**:

| ID | DisplayName | What it verifies |
|----|-------------|-----------------|
| ST-ENG-004 | Mixed-version batch: each response version matches its request | 3 simultaneous requests × 3 versions |
| ST-ENG-005 | N concurrent same-version requests — no cross-stream bleed | Correlation IDs match after N=5 concurrent requests |
| ST-ENG-006 | `x-correlation-id` header preserved through full routing flow | Header value identical in response |

**Acceptance criteria**:
- All 6 routing tests pass
- `dotnet test src/TurboHttp.StreamTests/` → 0 failures
- Total passing: 65

---

### Phase 15 — `HostConnectionPoolTests.cs` — basic queue + version isolation (ST-POOL-001..004)
- [ ] **Status**: pending

**File**: `src/TurboHttp.StreamTests/Pool/HostConnectionPoolTests.cs`

**Context**: `HostConnectionPoolFlowTests.cs` was deleted.  This new file replaces it with proper display names and RFC traceability.

**Tests**:

| ID | DisplayName | What it verifies |
|----|-------------|-----------------|
| ST-POOL-001 | HTTP/1.0 request through pool returns correct status and version | Basic round-trip via pool |
| ST-POOL-002 | HTTP/1.1 request through pool returns correct status and version | Basic round-trip via pool |
| ST-POOL-003 | HTTP/2.0 request through pool returns correct status and version | Basic round-trip via pool |
| ST-POOL-004 | Mixed-version batch via pool: each response version matches request | 3 simultaneous requests → correct routing |

**Acceptance criteria**:
- 4 tests compile and pass
- Total passing: 69

---

### Phase 16 — `HostConnectionPoolTests.cs` — connection isolation + backpressure (ST-POOL-005..008)
- [ ] **Status**: pending

**Prerequisite**: Phase 15 complete.

**Tests**:

| ID | DisplayName | What it verifies |
|----|-------------|-----------------|
| ST-POOL-005 | HTTP/1.0 bytes only reach HTTP/1.0 fake connection | Isolation: `fake11.Captured` and `fake20.Captured` remain empty |
| ST-POOL-006 | HTTP/1.1 bytes only reach HTTP/1.1 fake connection | Isolation: `fake10.Captured` and `fake20.Captured` remain empty |
| ST-POOL-007 | HTTP/2.0 bytes only reach HTTP/2.0 fake connection | Isolation: `fake10.Captured` and `fake11.Captured` remain empty |
| ST-POOL-008 | Backpressure: queue of 256 requests does not deadlock | All 256 requests complete within timeout |

**Acceptance criteria**:
- All 8 pool tests pass
- `HostConnectionPoolTests.cs` is the only pool test file
- Total passing: 73

---

### Phase 17 — `Http10WireComplianceTests.cs` (RFC 1945 on-wire validation)
- [ ] **Status**: pending

**File**: `src/TurboHttp.StreamTests/Http10/Http10WireComplianceTests.cs`

**Purpose**: Cross-check that `Http10Engine` produces exactly the bytes mandated by RFC 1945 §5–8.  Uses `FakeTcpFlow.Capture()` (no response, single captured chunk).

**Tests**:

| ID | DisplayName | What it verifies |
|----|-------------|-----------------|
| ST-10-WIRE-001 | RFC-1945-§5.1: `GET /path HTTP/1.0\r\n` exact bytes | Regex or `StartsWith` match |
| ST-10-WIRE-002 | RFC-1945-§7.1: Header folding absent — each header on its own line | No `\r\n ` or `\r\n\t` in output |
| ST-10-WIRE-003 | RFC-1945-§5.1: Query string included in Request-URI | `?foo=bar` preserved |
| ST-10-WIRE-004 | RFC-1945-§D.1: Absolute URI path includes only the path+query, not scheme/host | Wire target starts with `/` |
| ST-10-WIRE-005 | RFC-1945-§7.2: `Content-Length` matches actual body byte count | Body and length field consistent |

**Acceptance criteria**:
- 5 tests compile and pass
- Total passing: 78

---

### Phase 18 — `Http11WireComplianceTests.cs` (RFC 9112 on-wire validation)
- [ ] **Status**: pending

**File**: `src/TurboHttp.StreamTests/Http11/Http11WireComplianceTests.cs`

**Tests**:

| ID | DisplayName | What it verifies |
|----|-------------|-----------------|
| ST-11-WIRE-001 | RFC-9112-§3.1: `GET /path HTTP/1.1\r\n` exact bytes | Request-Line format |
| ST-11-WIRE-002 | RFC-9112-§7.2: `Host` header present and correct | `Host: example.com` |
| ST-11-WIRE-003 | RFC-9112-§7.6.1: `Connection` header absent on outbound | Not forwarded |
| ST-11-WIRE-004 | RFC-9112-§6.1: Chunked encoding: first chunk header `<hex-size>\r\n` | Regex match |
| ST-11-WIRE-005 | RFC-9112-§2.1: Header section ends with double CRLF | `\r\n\r\n` present before body |
| ST-11-WIRE-006 | RFC-9112-§3.1: Method preserved verbatim (`DELETE`, `PATCH`) | Correct method on wire |

**Acceptance criteria**:
- 6 tests compile and pass
- Total passing: 84

---

### Phase 19 — `Http2WireComplianceTests.cs` + validation gate (RFC 9113 on-wire)
- [ ] **Status**: pending

**File**: `src/TurboHttp.StreamTests/Http20/Http2WireComplianceTests.cs`

**Tests**:

| ID | DisplayName | What it verifies |
|----|-------------|-----------------|
| ST-20-WIRE-001 | RFC-9113-§3.5: First 24 bytes = connection preface magic verbatim | Byte-exact comparison |
| ST-20-WIRE-002 | RFC-9113-§3.5: SETTINGS frame immediately follows preface (bytes 24–32 header) | Frame type 0x4 at offset 24 |
| ST-20-WIRE-003 | RFC-9113-§8.3.1: `:method`, `:path`, `:scheme`, `:authority` all present in HPACK | Decode HPACK block, check names |
| ST-20-WIRE-004 | RFC-9113-§4.1: All frame lengths consistent with actual payload sizes | Every frame: `length_field == payload.Length` |
| ST-20-WIRE-005 | RFC-9113-§5.1.1: First request stream ID is 1 | Stream ID field = 1 |
| ST-20-WIRE-006 | RFC-9113-§6.5: SETTINGS ACK flag byte is `0x01` | Captured ACK frame flags |

**Validation gate**:
- `dotnet test src/TurboHttp.StreamTests/` → 0 failures, ≥ 84 tests pass
- `dotnet test src/TurboHttp.Tests/` → 0 failures (no regression in unit tests)
- `dotnet test src/TurboHttp.IntegrationTests/` → 0 failures (no integration regression)

**Acceptance criteria**:
- All 6 wire tests compile and pass
- All three test projects green

---

## Summary

| Phase | File | New Tests | RFC focus |
|-------|------|-----------|-----------|
| 0 | Shared helpers scaffold | 0 | — |
| 1 | `Http10/Http10EncoderStageTests.cs` | 6 | RFC 1945 §5.1, §7.1, §D.1 |
| 2 | `Http10/Http10DecoderStageTests.cs` | 5 | RFC 1945 §6.1, §7.1, §7.2 |
| 3 | `Http10/Http10EngineTests.cs` | 6 | RFC 1945 round-trips |
| 4 | `Http11/Http11EncoderStageTests.cs` | 5 | RFC 9112 §3.1, §6.1, §7.2, §7.6.1 |
| 5 | `Http11/Http11DecoderStageTests.cs` | 6 | RFC 9112 §4, §6.1, §7.1 |
| 6 | `Http11/Http11EngineTests.cs` | 5 | RFC 9112 round-trips |
| 7 | `Http20/PrependPrefaceStageTests.cs` | 4 | RFC 9113 §3.5 |
| 8 | `Http20/Http2FrameEncoderStageTests.cs` | 4 | RFC 9113 §4.1, §4.2 |
| 9 | `Http20/Http2FrameDecoderStageTests.cs` | 5 | RFC 9113 §4.1 |
| 10 | `Http20/Request2Http2FrameStageTests.cs` | 5 | RFC 9113 §8.1, §8.3.1 |
| 11 | `Http20/Http20EngineTests.cs` (batch 1) | 4 | RFC 9113 round-trips |
| 12 | `Http20/Http20EngineTests.cs` (batch 2) | 4 | RFC 9113 multi-stream |
| 13 | `Engine/EngineRoutingTests.cs` (basic) | 3 | Version demux |
| 14 | `Engine/EngineRoutingTests.cs` (concurrent) | 3 | Concurrency, no bleed |
| 15 | `Pool/HostConnectionPoolTests.cs` (basic) | 4 | Pool round-trips |
| 16 | `Pool/HostConnectionPoolTests.cs` (isolation) | 4 | Isolation + backpressure |
| 17 | `Http10/Http10WireComplianceTests.cs` | 5 | RFC 1945 wire bytes |
| 18 | `Http11/Http11WireComplianceTests.cs` | 6 | RFC 9112 wire bytes |
| 19 | `Http20/Http2WireComplianceTests.cs` + gate | 6 | RFC 9113 wire bytes |
| **Total** | | **~90 tests** | |
