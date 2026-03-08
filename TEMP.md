# TurboHttp — Plan: Remove Http2ProtocolSession

**Goal:** Delete `Http2ProtocolSession.cs`. All 25 files that use it will be migrated.
**Result:** Same RFC coverage, but verified against real production classes.

Source: `TEST.md`
Stage tests: `src/TurboHttp.StreamTests/Http20/`
Unit tests:  `src/TurboHttp.Tests/RFC9113/`

---

## Migration Strategy — Four Paths

| Path | When | Replacement |
|------|------|-------------|
| **A — FrameDecoder direct** | Test checks only frames or exceptions, no HTTP responses | `new Http2FrameDecoder().Decode(bytes)` |
| **B — CompletionDecoder** | Test checks `session.Responses` (full `HttpResponseMessage`) | `Http2CompletionDecoder.Process(bytes)` |
| **C — New Stage Test** | Test checks connection-level state (GOAWAY, MaxStreams, flood protection, flow control) | `Source.From(...).Via(stage).RunWith(...)` |
| **D — Delete** | Test checks only session-internal fields with no RFC basis | Test is removed without replacement |

---

## Phase A — FrameDecoder Direct (unit tests, mechanical migration)

Migration pattern:
```csharp
// Before:  session.Process(raw)  throws Http2Exception
// After:   decoder.Decode(raw)   throws Http2Exception

var decoder = new Http2FrameDecoder();
var ex = Assert.Throws<Http2Exception>(() => decoder.Decode(raw));
Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
```

### `02_FrameParsingTests.cs` — FP-016..FP-020 (5 tests via session)

- [ ] **A-01** FP-016: SETTINGS on non-zero stream → replace Session.Process() with FrameDecoder.Decode() — RFC 9113 §6.5
- [ ] **A-02** FP-017: PING on non-zero stream → replace — RFC 9113 §6.7
- [ ] **A-03** FP-018: GOAWAY on non-zero stream → replace — RFC 9113 §6.8
- [ ] **A-04** FP-019: WINDOW_UPDATE stream 0 accepted → replace — RFC 9113 §6.9
- [ ] **A-05** FP-020: WINDOW_UPDATE stream N accepted → replace — RFC 9113 §6.9

### `04_SettingsTests.cs` — SETTINGS validation

- [ ] **A-06** SS-001..SS-005: Connection-Preface SETTINGS bytes — raw FrameDecoder.Decode() instead of session — RFC 9113 §3.5
- [ ] **A-07** SS-006: MaxFrameSize=16383 is PROTOCOL_ERROR — RFC 9113 §6.5.2
- [ ] **A-08** SS-007: MaxFrameSize=16777216 is PROTOCOL_ERROR — RFC 9113 §6.5.2
- [ ] **A-09** SS-009: InitialWindowSize overflow → FLOW_CONTROL_ERROR — RFC 9113 §6.5.2
- [ ] **A-10** SS-010: InitialWindowSize=2^31-1 accepted — RFC 9113 §6.5.2
- [ ] **A-11** SS-011: EnablePush=0 accepted — RFC 9113 §6.5.2
- [ ] **A-12** SS-012: EnablePush=1 accepted — RFC 9113 §6.5.2
- [ ] **A-13** SS-013: EnablePush=2 is PROTOCOL_ERROR — RFC 9113 §6.5.2
- [ ] **A-14** SS-019: Non-ACK SETTINGS → 1 ACK to send (check SettingsFrame output) — RFC 9113 §6.5
- [ ] **A-15** SS-020: SETTINGS ACK → no new ACK in return — RFC 9113 §6.5
- [ ] **A-16** SS-021: Three SETTINGS → three ACK frames (via decoder output count) — RFC 9113 §6.5
- [ ] **A-17** SS-022: Empty SETTINGS → ACK required — RFC 9113 §6.5
- [ ] **A-18** SS-023: Encoded SETTINGS ACK is a valid 9-byte frame — RFC 9113 §6.5
- [ ] **A-19** SS-027: Unknown SETTINGS parameter silently ignored — RFC 9113 §6.5
- [ ] **A-20** SS-028: Multiple parameters in one frame all applied — RFC 9113 §6.5
- [ ] **A-21** SS-029: InitialWindowSize increase + open stream overflow — RFC 9113 §6.9.2

### `07_ErrorHandlingTests.cs` — Error code mapping

- [ ] **A-22** EM-001..EM-010: Connection error → `IsConnectionError = true` (10 tests) — RFC 9113 §5.4.1
- [ ] **A-23** EM-011..EM-020: Stream error → `IsConnectionError = false`, correct StreamId (10 tests) — RFC 9113 §5.4.2
- [ ] **A-24** EM-021..EM-025: PROTOCOL_ERROR vs FLOW_CONTROL_ERROR vs FRAME_SIZE_ERROR (5 tests) — RFC 9113 §6.1 §6.4

### `14_DecoderErrorCodeTests.cs` — Wire format of error codes

- [ ] **A-25** DC-001..DC-010: Error code uint32 big-endian in RST_STREAM and GOAWAY (10 tests) — RFC 9113 §6.4 §6.6

### `08_GoAwayTests.cs` — GOAWAY frame-parsing portion

- [ ] **A-26** GA-Frame tests: GOAWAY frame field parsing (LastStreamId, ErrorCode, debug data) without `session.IsGoingAway` → FrameDecoder — RFC 9113 §6.8

### `11_DecoderStreamValidationTests.cs` — stream validation (exception-only tests)

- [ ] **A-27** SVL-Exception tests: All tests that only check Http2Exception → FrameDecoder direct — RFC 9113 §5.1 §6.1

### `09_ContinuationFrameTests.cs` — exception portion

- [ ] **A-28** CF-004: CONTINUATION on wrong stream → PROTOCOL_ERROR — RFC 9113 §6.10
- [ ] **A-29** CF-005: Non-CONTINUATION after HEADERS-without-END_HEADERS → PROTOCOL_ERROR — RFC 9113 §6.10
- [ ] **A-30** CF-006: Orphaned CONTINUATION → PROTOCOL_ERROR — RFC 9113 §6.10
- [ ] **A-31** CF-007..CF-016: Flood protection, partial blocks exception tests → FrameDecoder — RFC 9113 §6.10

### `03_StreamStateMachineTests.cs` — exception portion (DATA on idle stream etc.)

- [ ] **A-32** SM-Exception tests: All tests that only check Http2Exception on illegal stream transitions → FrameDecoder direct — RFC 9113 §5.1

---

## Phase B — CompletionDecoder (unit tests, full HTTP response)

Migration pattern:
```csharp
// Before
var session = new Http2ProtocolSession();
session.Process(headersBytes);
session.Process(dataBytes);
Assert.Equal(HttpStatusCode.OK, session.Responses[0].StatusCode);

// After
var decoder = new Http2CompletionDecoder();
decoder.Process(headersBytes);
decoder.Process(dataBytes);
var response = decoder.TryGetResponse(streamId: 1);
Assert.NotNull(response);
Assert.Equal(HttpStatusCode.OK, response.StatusCode);
```

### `15_RoundTripHandshakeTests.cs` (~18 tests)

- [ ] **B-01** RT-HND-001..RT-HND-018: Round-trip handshake tests → Http2CompletionDecoder — RFC 9113 §3.5 §6.5

### `16_RoundTripMethodTests.cs` (~18 tests)

- [ ] **B-02** RT-MTH-001..RT-MTH-018: GET, POST, PUT, DELETE, HEAD round-trips → Http2CompletionDecoder — RFC 9113 §8.3

### `17_RoundTripHpackTests.cs` (~18 tests)

- [ ] **B-03** RT-HPK-001..RT-HPK-018: HPACK round-trips → Http2CompletionDecoder — RFC 7541 §2–7

### `06_HeadersTests.cs` — response portion

- [ ] **B-04** HDR-Response tests: Tests that check `session.Responses` → Http2CompletionDecoder — RFC 9113 §6.2

### `09_ContinuationFrameTests.cs` — response portion (CF-001..CF-003)

- [ ] **B-05** CF-001: HEADERS with END_HEADERS → full response — RFC 9113 §6.10
- [ ] **B-06** CF-002: HEADERS + CONTINUATION → full response after reassembly — RFC 9113 §6.10
- [ ] **B-07** CF-003: HEADERS + 3x CONTINUATION → full response — RFC 9113 §6.10

### `01_ConnectionPrefaceTests.cs` — session.Responses portion

- [ ] **B-08** CP-Session tests: Tests that check `session.ReceivedSettings` / `session.Responses` → Http2CompletionDecoder or FrameDecoder direct — RFC 9113 §3.4

---

## Phase C — New Stage Tests (Category B from TEST.md)

Shared helper method in `StreamTestBase`:
```csharp
private static (IMemoryOwner<byte>, int) Chunk(byte[] data)
    => (new SimpleMemoryOwner(data), data.Length);

protected async Task<IReadOnlyList<Http2Frame>> DecodeFramesAsync(params byte[][] chunks)
{
    var source = Source.From(chunks.Select(Chunk));
    return await source
        .Via(Flow.FromGraph(new Stages.Http2FrameDecoderStage()))
        .RunWith(Sink.Seq<Http2Frame>(), Materializer);
}
```

### C-1: `Http2FlowControlStageTests.cs` — RFC 9113 §6.9
Replaces: `05_FlowControlTests.cs` + `13_DecoderStreamFlowControlTests.cs`

- [ ] **C-01** ST_20_FC_001: Connection-level WINDOW_UPDATE (stream 0) passes through stage — RFC 9113 §6.9
- [ ] **C-02** ST_20_FC_002: Stream-level WINDOW_UPDATE (stream N) passes through stage — RFC 9113 §6.9
- [ ] **C-03** ST_20_FC_003: WINDOW_UPDATE increment field decoded correctly — RFC 9113 §6.9.1
- [ ] **C-04** ST_20_FC_004: Maximum increment value (2^31-1) decoded correctly — RFC 9113 §6.9.1
- [ ] **C-05** ST_20_FC_005: WINDOW_UPDATE with zero increment decoded (value=0) — RFC 9113 §6.9.1
- [ ] **C-06** ST_20_FC_006: HEADERS then WINDOW_UPDATE in one chunk: order preserved — RFC 9113 §6.9
- [ ] **C-07** ST_20_FC_007: WINDOW_UPDATE split across two TCP chunks reassembled — RFC 9113 §6.9
- [ ] **C-08** ST_20_FC_008: Three WINDOW_UPDATE frames decoded as three distinct frames — RFC 9113 §6.9

### C-2: `Http2SecurityStageTests.cs` — RFC 9113 §6.4 / CVE-2023-44487
Replaces: `Http2SecurityTests.cs` + `Http2ResourceExhaustionTests.cs`

- [ ] **C-09** ST_20_SEC_001: 100 RST_STREAM frames decoded without truncation (Rapid Reset baseline) — RFC 9113 §6.4 / CVE-2023-44487
- [ ] **C-10** ST_20_SEC_002: 100 CONTINUATION frames after HEADERS-without-END_HEADERS decoded — RFC 9113 §6.10
- [ ] **C-11** ST_20_SEC_003: 100 empty DATA frames (END_STREAM=false) decoded without loss — RFC 9113 §6.1
- [ ] **C-12** ST_20_SEC_004: 50 SETTINGS frames decoded without suppression — RFC 9113 §6.5
- [ ] **C-13** ST_20_SEC_005: HEADERS + RST_STREAM on same stream both decoded — RFC 9113 §6.4
- [ ] **C-14** ST_20_SEC_006: Interleaved RST_STREAM from multiple streams: StreamId preserved — RFC 9113 §6.4
- [ ] **C-15** ST_20_SEC_007: 50 SETTINGS + 50 SETTINGS-ACK decoded in correct order — RFC 9113 §6.5
- [ ] **C-16** ST_20_SEC_008: Empty DATA + WINDOW_UPDATE decoded in correct order — RFC 9113 §6.1 §6.9

### C-3: `Http2ConcurrentStreamsStageTests.cs` — RFC 9113 §6.5.2
Replaces: `Http2MaxConcurrentStreamsTests.cs`

- [ ] **C-17** ST_20_MCS_001: SETTINGS with MAX_CONCURRENT_STREAMS=1 decoded correctly — RFC 9113 §6.5.2
- [ ] **C-18** ST_20_MCS_002: SETTINGS with MAX_CONCURRENT_STREAMS=100 decoded correctly — RFC 9113 §6.5.2
- [ ] **C-19** ST_20_MCS_003: SETTINGS with MAX_CONCURRENT_STREAMS=0 decoded (zero is legal) — RFC 9113 §6.5.2
- [ ] **C-20** ST_20_MCS_004: SETTINGS with MAX_CONCURRENT_STREAMS=2^32-1 decoded correctly — RFC 9113 §6.5.2
- [ ] **C-21** ST_20_MCS_005: Two SETTINGS updating MAX_CONCURRENT_STREAMS decoded in order — RFC 9113 §6.5
- [ ] **C-22** ST_20_MCS_006: SETTINGS with multiple params including MAX_CONCURRENT_STREAMS — RFC 9113 §6.5

### C-4: `Http2GoAwayStageTests.cs` — RFC 9113 §6.8
Replaces: `08_GoAwayTests.cs` state portion (`session.IsGoingAway`)

- [ ] **C-23** ST_20_GA_001: GOAWAY with NO_ERROR decoded correctly — RFC 9113 §6.8
- [ ] **C-24** ST_20_GA_002: GOAWAY with PROTOCOL_ERROR decoded correctly — RFC 9113 §6.8
- [ ] **C-25** ST_20_GA_003: GOAWAY with non-zero LastStreamId decoded correctly — RFC 9113 §6.8
- [ ] **C-26** ST_20_GA_004: GOAWAY with debug data: optional bytes accessible — RFC 9113 §6.8
- [ ] **C-27** ST_20_GA_005: GOAWAY split across two TCP chunks reassembled — RFC 9113 §6.8
- [ ] **C-28** ST_20_GA_006: HEADERS followed by GOAWAY decoded in order — RFC 9113 §6.8

---

## Phase D — Delete (no RFC basis)

- [ ] **D-01** Delete `Http2HighConcurrencyTests.cs` — checks only `session.ActiveStreamCount` (session field, not RFC)
- [ ] **D-02** Delete `Http2CrossComponentValidationTests.cs` — cross-validates session properties with no production equivalent
- [ ] **D-03** Review `Http2FuzzHarnessTests.cs` — rewrite session calls as FrameDecoder direct, or delete if no RFC value remains
- [ ] **D-04** Delete state portion of `03_StreamStateMachineTests.cs` — tests that only check `session.GetStreamState()` (no RFC requirement)

---

## Phase E — External files + delete Http2ProtocolSession

- [ ] **E-01** `Integration/TcpFragmentationTests.cs` — identify session calls and migrate to FrameDecoder or stage
- [ ] **E-02** `RFC9110/01_ContentEncodingGzipTests.cs` — identify session calls and migrate
- [ ] **E-03** `RFC9110/02_ContentEncodingDeflateTests.cs` — identify session calls and migrate
- [ ] **E-04** Verify build: `dotnet build src/TurboHttp.sln` — zero errors, zero session references
- [ ] **E-05** Delete `src/TurboHttp.Tests/Http2ProtocolSession.cs`
- [ ] **E-06** Run full test suite: `dotnet test src/TurboHttp.sln` — all tests green

---

## Acceptance Criteria (final state)

- [ ] `dotnet test src/TurboHttp.Tests` — all tests green
- [ ] `dotnet test src/TurboHttp.StreamTests` — all tests green
- [ ] `Http2ProtocolSession.cs` no longer exists
- [ ] No `using` reference to `Http2ProtocolSession` anywhere in the solution (grep confirms 0 matches)
- [ ] RFC coverage equal or better than before — no RFC section lost
- [ ] All new stage tests have `DisplayName` starting with `"RFC-9113-§…:"`
- [ ] Stage tests use only `Flow.FromGraph(new Stages.*)` — no FrameDecoder direct calls inside stage test files

---

## Open Questions Before Starting

1. **`Http2FrameDecoder.Decode()` signature** — Returns `IReadOnlyList<Http2Frame>` or single frame? Check `src/TurboHttp/Protocol/Http2FrameDecoder.cs` before Phase A.
2. **`Http2CompletionDecoder` API** — Is `TryGetResponse(int streamId)` the correct method name? Check `Http2CompletionDecoder.cs` before Phase B.
3. **`SettingsParameter.MaxConcurrentStreams`** — Confirm enum value name.
4. **`GoAwayFrame.DebugData`** — Type is `ReadOnlyMemory<byte>` or `byte[]` after decode?
5. **`03_StreamStateMachineTests.cs`** — Read file before Phase A/D: which tests only throw Http2Exception (Path A), which only check `GetStreamState()` (Path D)?
