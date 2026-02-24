# PRD: RFC Compliance Bug Fixes

## Introduction

During RFC test matrix analysis, four concrete RFC compliance violations were found in
the existing TurboHttp implementation. These are not missing features — they are bugs
where the current code either silently ignores protocol errors, misvalidates data, or
is completely absent where the RFC mandates behaviour. All issues are classified as
MUST requirements in their respective RFCs and must be fixed before a production release.

## Scope: Client-Side Only

TurboHttp is a **pure HTTP client library**. Only two directions exist:

| Direction | Required |
|---|---|
| `HttpRequestMessage` → bytes (encode request to send) | ✅ In scope |
| bytes → `HttpResponseMessage` (decode received response) | ✅ In scope |
| bytes → `HttpRequestMessage` (server-side request parsing) | ❌ Out of scope |
| `HttpResponseMessage` → bytes (server-side response encoding) | ❌ Out of scope |

All fixes and tests in this PRD must stay within the client-side boundary.

## Goals

- Fix all four identified RFC compliance bugs
- Each fix is accompanied by a regression test that would have caught the bug
- No existing tests break after the fixes
- Implementation matches the exact RFC wording cited in each issue

## User Stories

---

### US-001: Fix mislabeled HPACK Appendix C test

**Description:** As a developer, I want `HpackTests.Decode_Rfc7541_AppendixC3_FirstRequest`
to correctly test RFC 7541 Appendix C.3.1 (with Huffman) so that the test suite actually
validates Huffman-encoded request decoding.

**Background:**
The test currently uses the byte sequence `82 86 84 41 0F 77 77 77 2E...` which is
RFC 7541 **Appendix C.2.1** (First Request, *without* Huffman). Appendix C.3.1 (with
Huffman) encodes `"www.example.com"` as Huffman and would start with `41 8C F1 E3 C2...`.
The test name claims C.3 but validates C.2 — the Huffman path in the decoder is not
covered by this test.

**RFC reference:** RFC 7541 Appendix C.2.1 (without Huffman) and C.3.1 (with Huffman).

**Acceptance Criteria:**
- [ ] Rename existing test to `Decode_Rfc7541_AppendixC2_FirstRequest` — it is correct but mislabeled
- [ ] Add new test `Decode_Rfc7541_AppendixC3_FirstRequest` using the correct Huffman-encoded bytes from RFC 7541 C.3.1:
  - `82 86 84 41 8C F1 E3 C2 E5 F2 3A 6B A0 AB 90 F4 FF`
  - Expected: `:method=GET`, `:scheme=http`, `:path=/`, `:authority=www.example.com`
- [ ] All four C.3.x requests (C.3.1, C.3.2, C.3.3) tested with the dynamic-table state carried across requests
- [ ] `dotnet test` passes

---

### US-002: Fix Http11Decoder — silently ignoring malformed header lines

**Description:** As a developer, I want `Http11Decoder.ParseHeaders` to return
`HttpDecodeError.InvalidHeader` (or equivalent) when a header line contains no colon,
so that the decoder is RFC 7230 §3.2 compliant.

**Background:**
Current code in `Http11Decoder.ParseHeaders`:
```csharp
if (colonIdx > 0)
{
    // parse name/value
}
// else: line is silently skipped — RFC violation
```
RFC 7230 §3.2 states a header field MUST contain a colon. A server that receives a
request with a header line that has no colon MUST reject it. A client decoder that
receives a response with such a line MUST treat it as malformed. Silently skipping
allows header injection attacks via ambiguous parsing.

**RFC reference:** RFC 7230 §3.2, RFC 9112 §5.1.

**Acceptance Criteria:**
- [ ] `ParseHeaders` returns `HttpDecodeError.InvalidHeader` (add to enum if needed) for any header line without a colon
- [ ] A new test `Decode_HeaderWithoutColon_ReturnsError` in `Http11DecoderTests` verifies this behaviour
- [ ] Existing tests still pass (all valid responses have properly formed headers)
- [ ] The `HttpDecodeError` enum has a new `InvalidHeader` value (or reuse an existing appropriate value, documented in a code comment)
- [ ] `dotnet test` passes

---

### US-003: Fix HTTP/2 Decoder — DATA and HEADERS on Stream 0 must be PROTOCOL_ERROR

**Description:** As a developer, I want `Http2Decoder` to return / throw a protocol
error when it receives a DATA or HEADERS frame on stream ID 0, so that the implementation
is RFC 7540 §6.1 and §6.2 compliant.

**Background:**
RFC 7540 §6.1: "DATA frames MUST be associated with a stream. If a DATA frame is received
whose stream identifier field is 0x0, the recipient MUST respond with a connection error
of type PROTOCOL_ERROR."
RFC 7540 §6.2: Same rule for HEADERS frames on stream 0.
Currently `Http2Decoder` does not validate stream IDs on DATA or HEADERS frames, so frames
on stream 0 are processed silently, which is a MUST-level violation.

**RFC reference:** RFC 7540 §6.1, §6.2.

**Acceptance Criteria:**
- [ ] `Http2Decoder.TryDecode` throws `Http2Exception` (or sets an error result) with `Http2ErrorCode.ProtocolError` when a DATA frame arrives on stream 0
- [ ] Same for HEADERS frame on stream 0
- [ ] New tests in `Http2DecoderTests`:
  - `Decode_DataOnStream0_ThrowsProtocolError`
  - `Decode_HeadersOnStream0_ThrowsProtocolError`
- [ ] Existing tests still pass
- [ ] `dotnet test` passes

---

### US-004: Fix HTTP/2 Decoder — CONTINUATION on wrong stream ID or stream 0 must be PROTOCOL_ERROR

**Description:** As a developer, I want `Http2Decoder` to raise a protocol error when
a CONTINUATION frame arrives on a different stream ID than the preceding HEADERS frame,
or on stream 0, so that the decoder is RFC 7540 §6.10 compliant.

**Background:**
RFC 7540 §6.10: "A CONTINUATION frame MUST be associated with a stream. If a CONTINUATION
frame is received whose stream identifier field is 0x0, the recipient MUST respond with a
connection error of type PROTOCOL_ERROR."
Additionally, a CONTINUATION must follow on the same stream as the preceding HEADERS
or PUSH_PROMISE frame; if the stream ID differs this is also a PROTOCOL_ERROR.

**RFC reference:** RFC 7540 §6.10.

**Acceptance Criteria:**
- [ ] `Http2Decoder.TryDecode` throws `Http2Exception` with `Http2ErrorCode.ProtocolError` when a CONTINUATION frame arrives on stream 0
- [ ] Same when a CONTINUATION frame arrives on a different stream ID than the preceding HEADERS
- [ ] New tests in `Http2DecoderTests`:
  - `Decode_ContinuationOnStream0_ThrowsProtocolError`
  - `Decode_ContinuationOnWrongStream_ThrowsProtocolError`
- [ ] Existing continuation test (`Decode_ContinuationFrames_Reassembled`) still passes
- [ ] `dotnet test` passes

---

## Functional Requirements

- FR-1: `Http11Decoder` MUST return an error (not silently skip) for any header line that has no colon separator
- FR-2: `Http2Decoder` MUST throw `Http2Exception(ProtocolError)` for DATA frames on stream ID 0
- FR-3: `Http2Decoder` MUST throw `Http2Exception(ProtocolError)` for HEADERS frames on stream ID 0
- FR-4: `Http2Decoder` MUST throw `Http2Exception(ProtocolError)` for CONTINUATION frames on stream ID 0
- FR-5: `Http2Decoder` MUST throw `Http2Exception(ProtocolError)` for CONTINUATION frames on a different stream than the preceding HEADERS
- FR-6: `HpackTests` MUST have a correctly named and byte-accurate test for RFC 7541 Appendix C.3.1 (with Huffman)
- FR-7: `HpackTests` MUST rename the existing Appendix C test to match what it actually tests (C.2.1)

## Non-Goals

- Do not implement server-side request parsing (`bytes → HttpRequestMessage`) — client-only library
- Do not implement server-side response encoding (`HttpResponseMessage → bytes`) — client-only library
- Do not implement a full HTTP/2 stream state machine (that is tracked in `prd-http2-stream-state-management.md`)
- Do not implement flow control enforcement (tracked in RFC test coverage PRD)
- Do not add RFC 7233 (Range) support
- Do not change the Http10Decoder's `StatusLine_InvalidStatusCode_FallsBackTo500` behaviour — this is implementation-defined and not a MUST violation

## Technical Considerations

- `HttpDecodeError` enum is in `TurboHttp/Protocol/HttpDecodeError.cs` — add `InvalidHeader` there if needed
- `Http2Exception` already exists in the protocol layer; reuse it
- The CONTINUATION stream-tracking state (`_pendingContinuationStreamId` or similar) already exists in `Http2Decoder` — validate against it when a new CONTINUATION frame arrives
- US-001 requires checking the actual RFC 7541 Appendix C byte tables — copy them from the RFC exactly

## Success Metrics

- All four bugs have a failing test before the fix and a passing test after
- Zero existing tests regress
- `dotnet test` green on all test classes

## Open Questions

- Should `Http11Decoder` throw `HttpDecoderException` (breaking the decode loop) or return `NeedMoreData` for a no-colon header? Throwing is the RFC-correct behaviour — confirm with team.
- Should `Http2Decoder` use a result-based error pattern or throw? Currently it mixes both styles — keep consistent with existing `Http2Exception` throw pattern.