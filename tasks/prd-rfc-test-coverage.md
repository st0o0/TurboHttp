# PRD: RFC Test Matrix Coverage — Missing Tests

## Introduction

The RFC Test Matrix (`RFC_TEST_MATRIX.md`) defines ~150 test cases covering MUST and SHOULD
requirements across RFC 7230, RFC 7231, RFC 7233, RFC 7540, and RFC 7541. Currently only
~45% of MUST requirements have test coverage. This PRD tracks the work needed to fill those
gaps. It is scoped to *tests only* — implementation gaps that require new features (e.g.
stream state machine, flow control) are tracked separately.

The work is split into four phases that can be implemented independently.

---

## Scope: Client-Side Only

TurboHttp is a **pure HTTP client library**. Only two directions exist:

| Direction | Required |
|---|---|
| `HttpRequestMessage` → bytes (encode request to send) | ✅ In scope |
| bytes → `HttpResponseMessage` (decode received response) | ✅ In scope |
| bytes → `HttpRequestMessage` (server-side request parsing) | ❌ Out of scope |
| `HttpResponseMessage` → bytes (server-side response encoding) | ❌ Out of scope |

All new tests in this PRD cover either encoding requests or decoding responses.

## Goals

- Reach ≥ 90% coverage of MUST requirements in RFC 7230, RFC 7231, RFC 7540, and RFC 7541
- Cover all RFC 7541 Appendix C example sequences (C.2–C.6)
- Document every test with its RFC section reference as a code comment
- All new tests follow the existing xUnit style in the project (`[Fact]`, `[Theory]`, no test framework changes)

---

## Phase 1 — HTTP/1.1 Decoder & Encoder Gap Tests

### US-101: RFC 7230 §3.2 — Header field edge cases for Http11Decoder

**Description:** As a developer, I want Http11DecoderTests to cover the header field
rules from RFC 7230 §3.2 so that I can trust the decoder handles real-world edge cases.

**Acceptance Criteria:**
- [x] `Decode_Header_OWS_Trimmed` — header with leading/trailing whitespace: `X-Foo:   bar   ` → value is `"bar"`
- [x] `Decode_Header_EmptyValue_Accepted` — `X-Empty:\r\n` → header present with empty string value
- [x] `Decode_Header_CaseInsensitiveName` — `HOST: example.com` is accessible as `"Host"`
- [x] `Decode_Header_MultipleValuesForSameName_Preserved` — two `Accept:` lines → both values available
- [x] `Decode_Header_ObsFold_Http11_IsError` — obs-folded header (line starts with SP/HT) in HTTP/1.1 response → parse error (RFC 9112 §5.1)
- [x] `dotnet test` passes

---

### US-102: RFC 7230 §3.3 — Message Body edge cases for Http11Decoder

**Description:** As a developer, I want Http11DecoderTests to cover conflicting body
indicators and invalid Content-Length values.

**Acceptance Criteria:**
- [x] `Decode_ConflictingHeaders_ChunkedTakesPrecedence` — response with both `Transfer-Encoding: chunked` and `Content-Length: 5` → body parsed via chunked, Content-Length ignored
- [x] `Decode_MultipleContentLength_DifferentValues_Throws` — two `Content-Length` headers with different values → throws `HttpDecoderException`
- [x] `Decode_NegativeContentLength_HandledGracefully` — `Content-Length: -1` → no exception thrown (implementation-defined: empty body or error)
- [x] `Decode_NoBodyIndicator_EmptyBody` — response with neither Content-Length nor Transfer-Encoding and non-1xx/204/304 status → empty body, decoded successfully
- [x] `dotnet test` passes

---

### US-103: RFC 7230 §4.1 — Chunked encoding error cases for Http11Decoder

**Description:** As a developer, I want Http11DecoderTests to cover error paths in
chunked transfer encoding.

**Acceptance Criteria:**
- [x] `Decode_InvalidChunkSize_ReturnsError` — chunk size line contains `xyz` → `HttpDecoderException` or returns false with error
- [x] `Decode_ChunkSizeTooLarge_ReturnsError` — chunk size `999999999999\r\n` → `HttpDecoderException` (overflow)
- [x] `Decode_ChunkedWithTrailer_TrailerHeadersPresent` — chunked body with `X-Trailer: value\r\n` in the trailer section → trailing header is accessible via `response.TrailingHeaders`
- [x] `dotnet test` passes

---

### US-104: RFC 7231 §6.1 — Status code edge cases for Http11Decoder

**Description:** As a developer, I want Http11DecoderTests to cover boundary status
codes and the empty reason phrase case.

**Acceptance Criteria:**
- [x] `Decode_Status599_ParsedAsCustom` — `HTTP/1.1 599 Custom\r\n\r\n` → status code 599 decoded successfully
- [x] `Decode_Status600_ReturnsError` — `HTTP/1.1 600 Invalid\r\n\r\n` → `TryParseStatusLine` returns false (status ≥ 600 rejected)
- [x] `Decode_EmptyReasonPhrase_Accepted` — `HTTP/1.1 200 \r\n\r\n` → status 200 decoded, reason phrase is empty or whitespace
- [x] `dotnet test` passes

---

## Phase 2 — HPACK Full Coverage

### US-201: RFC 7541 §2.3 — Dynamic table eviction

**Description:** As a developer, I want explicit tests for dynamic table eviction so that
I can trust the HPACK decoder/encoder handles table overflow correctly.

**Acceptance Criteria:**
- [x] `DynamicTable_Eviction_OldestEntryRemovedWhenFull` — fill table to capacity, add one more entry → oldest entry no longer accessible
- [x] `DynamicTable_EvictionOrder_NewestSurvives` — fill with A, B, C; evict until space for D; D and most recent entries remain, oldest gone
- [x] `DynamicTable_SizeTooBig_ThrowsHpackException` — call `SetMaxAllowedTableSize` with negative value → `HpackException` thrown
- [x] `dotnet test` passes

---

### US-202: RFC 7541 §5.1 — Integer representation edge cases

**Description:** As a developer, I want tests for HPACK integer encoding boundary
values so that overflow and truncation are validated.

**Acceptance Criteria:**
- [ ] `ReadInteger_FitsInPrefix_SingleByte` — value 5 with prefix 5 bits → reads exactly 1 byte, returns 5
- [ ] `ReadInteger_MultiByteEncoding_DecodedCorrectly` — encode integer 1337 (RFC 7541 §5.1 example) → decoded as 1337
- [ ] `ReadInteger_MaxValue_Accepted` — value `(1 << 28) - 1` encoded and decoded without error
- [ ] `ReadInteger_Overflow_ThrowsHpackException` — encode value `(1 << 30)` → decoder throws `HpackException` with overflow message
- [ ] `ReadInteger_TruncatedData_ThrowsHpackException` — multi-byte integer with no stop bit → `HpackException`
- [ ] `dotnet test` passes

---

### US-203: RFC 7541 §5.2 — String representation edge cases

**Description:** As a developer, I want tests for empty strings, large strings, and
invalid Huffman data in HPACK string encoding.

**Acceptance Criteria:**
- [ ] `Decode_EmptyLiteralString_ReturnsEmptyString` — H=0, length=0 → header with empty value decoded
- [ ] `Decode_EmptyHuffmanString_ReturnsEmptyString` — H=1, length=0 → header with empty value decoded
- [ ] `Decode_LargeString_8KB_DecodedCorrectly` — literal string of 8192 chars → decoded without error
- [ ] `Decode_InvalidHuffman_ThrowsHpackException` — H=1 flag with malformed Huffman bytes → `HpackException`
- [ ] `dotnet test` passes

---

### US-204: RFC 7541 §6.x — Literal header indexing modes

**Description:** As a developer, I want explicit tests for the three literal header
indexing modes (incremental, without indexing, never indexed) in isolation.

**Acceptance Criteria:**
- [ ] `Decode_LiteralWithoutIndexing_NotAddedToDynamicTable` — encode header with without-indexing pattern (0000xxxx), decode → header decoded correctly but NOT added to dynamic table (table count unchanged)
- [ ] `Decode_NeverIndexed_DecodedWithNeverIndexFlag` — encode with never-indexed pattern (0001xxxx) → decoded `HpackHeader.NeverIndex == true`
- [ ] `Decode_IndexOutOfRange_ThrowsHpackException` — indexed header field with index > static count + dynamic count → `HpackException`
- [ ] `Encode_SensitiveHeader_Authorization_UsesNeverIndex` — encoder encodes `"authorization"` header → the encoded byte uses never-index pattern (0001xxxx prefix)
- [ ] `Encode_SensitiveHeader_Cookie_UsesNeverIndex` — same for `"cookie"`
- [ ] `dotnet test` passes

---

### US-205: RFC 7541 Appendix C — Full example sequences

**Description:** As a developer, I want all RFC 7541 Appendix C example sequences tested
with exact byte verification so that I have a ground truth for HPACK correctness.

**Background:** The existing `Decode_Rfc7541_AppendixC3_FirstRequest` is actually testing
Appendix C.2.1 (see compliance bug PRD). All C.2 through C.6 multi-request sequences need
coverage.

**Acceptance Criteria:**
- [ ] `Decode_AppendixC2_AllThreeRequests` — three sequential requests from C.2.1–C.2.4 decoded in order; dynamic table state carried across requests; each decoded header list matches the RFC exactly
- [ ] `Decode_AppendixC3_AllThreeRequests_WithHuffman` — same as C.2 but using Huffman-encoded bytes from C.3; `HuffmanCodec` must produce the exact bytes from the RFC
- [ ] `Decode_AppendixC4_AllThreeResponses` — three sequential responses from C.4 without Huffman; dynamic table eviction occurs in C.4.3 → verify entry count
- [ ] `Decode_AppendixC5_AllThreeResponses_WithHuffman` — C.5 with Huffman
- [ ] `Decode_AppendixC6_AllThreeResponses_WithHuffmanAndEviction` — C.6 with Huffman and eviction; verify the specific entries evicted match the RFC's expected dynamic table state
- [ ] `dotnet test` passes

---

## Phase 3 — HTTP/2 Decoder Additional Coverage

### US-301: RFC 7540 §4.1 — Frame format validation

**Description:** As a developer, I want Http2DecoderTests to cover unknown frame types
and the frame size limit so that the decoder handles RFC 7540 §4.1 correctly.

**Acceptance Criteria:**
- [ ] `Decode_UnknownFrameType_IsIgnored` — construct a frame with type byte `0x0A` (unknown) → `TryDecode` returns false/empty result, no exception, decoder remains usable
- [ ] `Decode_FrameExceedingMaxFrameSize_ThrowsFrameSizeError` — frame with payload length > 16384 (default `SETTINGS_MAX_FRAME_SIZE`) → `Http2Exception` with `Http2ErrorCode.FrameSizeError`
- [ ] `dotnet test` passes

---

### US-302: RFC 7540 §6.1 — DATA frame edge cases

**Description:** As a developer, I want Http2DecoderTests to cover empty DATA frames
and DATA with padding.

**Acceptance Criteria:**
- [ ] `Decode_EmptyDataFrame_Accepted` — DATA frame with payload length = 0 on open stream → accepted, body is empty byte array
- [ ] `Decode_DataFrame_WithPadding_PaddingStripped` — DATA frame with PADDED flag, 5 bytes padding → only the actual data bytes are returned, padding stripped
- [ ] `dotnet test` passes

---

### US-303: RFC 7540 §6.2 — HEADERS frame edge cases

**Description:** As a developer, I want Http2DecoderTests to cover HEADERS with PRIORITY
flag set.

**Acceptance Criteria:**
- [ ] `Decode_HeadersFrame_WithPriorityFlag_ParsedCorrectly` — HEADERS frame with PRIORITY flag set (5 extra bytes: exclusive bit + stream dependency + weight) → headers decoded correctly, priority bytes skipped
- [ ] `Decode_HeadersFrame_WithPadding_PaddingStripped` — HEADERS with PADDED flag → header block decoded without padding bytes
- [ ] `dotnet test` passes

---

### US-304: RFC 7540 §6.9 — Multiple CONTINUATION frames

**Description:** As a developer, I want Http2DecoderTests to verify that more than two
CONTINUATION frames can be reassembled correctly.

**Acceptance Criteria:**
- [ ] `Decode_ThreeContinuationFrames_AllReassembled` — split a large header block across one HEADERS + two CONTINUATION frames → response decoded correctly with all headers present
- [ ] `dotnet test` passes

---

### US-305: RFC 7540 §3.5 — Connection preface error cases

**Description:** As a developer, I want Http2EncoderTests or integration tests to cover
the preface validation path.

**Acceptance Criteria:**
- [ ] `BuildConnectionPreface_MagicBytes_ExactlyMatchRfc7540` — byte-for-byte comparison: `50 52 49 20 2A 20 48 54 54 50 2F 32 2E 30 0D 0A 0D 0A 53 4D 0D 0A 0D 0A` (24 bytes)
- [ ] `dotnet test` passes

---

## Functional Requirements

- FR-101: Http11DecoderTests has ≥ 12 new tests covering §3.2, §3.3, §4.1, §6.1 gaps
- FR-201: HpackTests has explicit tests for all three literal indexing modes
- FR-202: HpackTests covers all five Appendix C example sequences with byte-accurate assertions
- FR-203: HpackTests covers integer overflow, truncation, and empty string cases
- FR-301: Http2DecoderTests covers unknown frame type, frame size limit, DATA padding, HEADERS priority/padding, and multiple CONTINUATION frames
- FR-302: Every new test has an RFC section reference in a code comment: `// RFC 7230 §3.2-002`

## Non-Goals

- Server-side request decoding (`bytes → HttpRequestMessage`) — client-only library
- Server-side response encoding (`HttpResponseMessage → bytes`) — client-only library
- RFC 7233 (Range Requests) — no parser exists; adding tests would require implementation first
- RFC 7231 §7.1.1 (Date/Time formats) — no parser exists; deferred
- HTTP/2 flow control enforcement tests — no enforcement implementation exists; tracked in stream state PRD
- HTTP/2 stream state machine tests — tracked in `prd-http2-stream-state-management.md`
- Changing any existing passing tests

## Technical Considerations

- Appendix C byte sequences must be copied verbatim from RFC 7541 — do not derive them; compare the decoded result to the RFC's expected header list
- For multi-request Appendix C tests, use a single `HpackDecoder` instance across all three calls (shared dynamic table) and a single `HpackEncoder` instance
- `Http2DecoderTests` helper methods for building raw frames already exist in the test class (`PingFrame.Serialize()`, etc.) — use the same pattern for constructing padded/priority frames
- PADDED DATA and HEADERS frames are not currently built by encoder tests — construct raw bytes directly in test setup

## Success Metrics

- `dotnet test` passes with all new tests green
- RFC Test Matrix coverage for MUST items reaches ≥ 90% in RFC 7230, RFC 7231, RFC 7540, and RFC 7541
- Every new test method name matches the pattern `<Component>_<Scenario>_<ExpectedResult>`

## Open Questions

- Should Appendix C multi-request tests live in `HpackTests.cs` or a new `HpackAppendixCTests.cs`? A new file is recommended for clarity.
- Phase 3 DATA/HEADERS padding tests require constructing raw frame bytes — should a `Http2TestFrameBuilder` helper class be added to the test project?