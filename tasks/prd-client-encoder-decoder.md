# PRD: TurboHttp Client Encoder/Decoder — Production-Ready RFC Test Suite

## Introduction

TurboHttp's encoder/decoder implementation is functionally complete but lacks
systematic RFC-conformance test coverage. The existing test suite (~244 tests
across 10 files) leaves significant gaps: entire RFC sections are untested,
edge cases are missing, and TCP fragmentation, security limits, and round-trip
tests are absent.

This PRD drives the implementation of **644 new test cases** (≈730 effective
xUnit results via `[Theory]`) split into **47 small, focused user stories**.
Each story targets one test group in one file and is scoped to complete in a
single focused implementation session (≤20 new test methods).

**Source of truth:** `CLIENT_ENCODER_DECODER_PLAN.md` — check off `[ ]` → `[x]`
as each test is written and green.

---

## Goals

- Achieve RFC-conformance coverage across RFC 1945, 7230, 7231, 7233, 7540, 7541
- Every P0 test must pass before any production release
- All P1 tests must pass for production readiness
- `dotnet test ./src/TurboHttp.sln` green with zero skips
- ≥90% line coverage on all Protocol layer files
- h2spec conformance suite passes against any running server instance
- CI gate: no merge to `main` if any P0/P1 test is red

---

## Scope & Conventions

### What this PRD covers (gaps only)
All tasks below are **new tests** to be added. Do NOT re-implement tests
that already exist in the codebase (244 tests across 10 files).

### What is out of scope
- Server-side encoders/decoders
- I/O layer (Akka actors, `TcpConnectionManager`)
- Performance benchmarks
- h2spec server harness setup

### Key conventions
```
File:    src/TurboHttp.Tests/<FileName>.cs
NS:      namespace TurboHttp.Tests;
Fact:    [Fact] [DisplayName("ID: description")]
Theory:  [Theory] [InlineData(...)] [DisplayName("ID: description [{param}]")]
Method:  Should_ExpectedBehavior_When_Condition()
Style:   Allman braces, 4 spaces, sealed, #nullable enable
```

### Priority legend
- **P0** — Release blocker. Must be green before shipping.
- **P1** — Production-ready. Must be green for stable release.
- **P2** — Full compliance. Best-effort, can follow in a follow-up.

---

## User Stories

Each story maps to one section of `CLIENT_ENCODER_DECODER_PLAN.md`.
Acceptance criteria list every RFC test ID that must be implemented and green.

---

### US-001: HTTP/1.0 Encoder — Request-Line
**Description:** As a developer maintaining `Http10Encoder`, I want request-line
tests so that the version token, method casing, path, query, and URI rules are
verified per RFC 1945 §5.1.

**File:** `src/TurboHttp.Tests/Http10EncoderTests.cs`

**Acceptance Criteria:**
- [ ] `1945-enc-001` — Request-line starts with `METHOD path HTTP/1.0\r\n`
- [ ] `1945-enc-007` — Path + query string preserved in request-line
- [ ] `1945-5.1-004` — Lowercase method causes exception (P0)
- [ ] `1945-5.1-005` — Absolute URI encoded in request-line (P1)
- [ ] `enc1-m-001` — `[Theory]` × 8 HTTP methods all uppercase (P0)
- [ ] `enc1-uri-001` — Missing path normalized to `/`
- [ ] `enc1-uri-002` — Query string preserved verbatim
- [ ] `enc1-uri-003` — Percent-encoded chars not double-encoded (P1)
- [ ] `enc1-uri-004` — URI fragment stripped (P1)
- [ ] `dotnet test --filter "ClassName=TurboHttp.Tests.Http10EncoderTests"` green

---

### US-002: HTTP/1.0 Encoder — Header Suppression
**Description:** As a developer, I want header-suppression tests so that
HTTP/1.0 requests never emit `Host`, `Transfer-Encoding`, `Connection`, or
malformed values per RFC 1945 §5.2.

**File:** `src/TurboHttp.Tests/Http10EncoderTests.cs`

**Acceptance Criteria:**
- [ ] `1945-enc-002` — `Host:` absent
- [ ] `1945-enc-003` — `Transfer-Encoding:` absent
- [ ] `1945-enc-004` — `Connection:` absent
- [ ] `enc1-hdr-001` — Every header line ends with `\r\n`, no bare `\n`
- [ ] `enc1-hdr-002` — Custom header name casing preserved (P1)
- [ ] `enc1-hdr-003` — Multiple custom headers all emitted (P1)
- [ ] `enc1-hdr-004` — Semicolon in header value preserved verbatim (P1)
- [ ] `enc1-hdr-005` — NUL byte in header value throws `ArgumentException`
- [ ] All new tests green

---

### US-003: HTTP/1.0 Encoder — Body Encoding
**Description:** As a developer, I want body-encoding tests to verify that
Content-Length is set correctly and body bytes (including binary) are
transmitted verbatim per RFC 1945 §7.

**File:** `src/TurboHttp.Tests/Http10EncoderTests.cs`

**Acceptance Criteria:**
- [ ] `1945-enc-005` — `Content-Length` set for POST body
- [ ] `1945-enc-006` — `Content-Length` absent for bodyless GET
- [ ] `1945-enc-008` — Binary body bytes match input exactly
- [ ] `1945-enc-009` — UTF-8 JSON body encoded correctly
- [ ] `enc1-body-001` — Body with null bytes not truncated
- [ ] `enc1-body-002` — 2 MB body with correct `Content-Length` (P1)
- [ ] `enc1-body-003` — `\r\n\r\n` separates headers from body exactly
- [ ] All new tests green

---

### US-004: HTTP/1.0 Decoder — Status-Line
**Description:** As a developer, I want status-line tests for the RFC 1945
decoder so that all 15 standard codes, unknown codes, invalid codes, and
line-ending variants are handled.

**File:** `src/TurboHttp.Tests/Http10DecoderTests.cs`

**Acceptance Criteria:**
- [ ] `1945-dec-003a` — `[Theory]` × 15 RFC 1945 status codes (200–503)
- [ ] `dec1-sl-001` — Unknown status code 299 accepted
- [ ] `dec1-sl-002` — Status code 99 rejected
- [ ] `dec1-sl-003` — Status code 1000 rejected
- [ ] `dec1-sl-004` — LF-only line endings accepted (HTTP/1.0 permissive)
- [ ] `dec1-sl-005` — Empty reason phrase accepted
- [ ] All new tests green

---

### US-005: HTTP/1.0 Decoder — Header Parsing
**Description:** As a developer, I want header-parsing tests to verify
obs-fold, duplicate headers, malformed names, case-insensitivity, and
whitespace trimming per RFC 1945 §4.

**File:** `src/TurboHttp.Tests/Http10DecoderTests.cs`

**Acceptance Criteria:**
- [ ] `1945-4-002` — Obs-fold continuation accepted and merged
- [ ] `1945-4-002b` — Double obs-fold merged into single value (P1)
- [ ] `1945-4-003` — Duplicate response headers both accessible
- [ ] `1945-4-004` — Header without colon → `InvalidHeader` error
- [ ] `1945-4-005` — Header name lookup case-insensitive
- [ ] `1945-4-006` — Leading/trailing whitespace trimmed from value
- [ ] `1945-4-007` — Space in header name → `InvalidFieldName`
- [ ] `dec1-hdr-001` — Tab in header value accepted (P1)
- [ ] `dec1-hdr-002` — Response with zero headers accepted
- [ ] All new tests green

---

### US-006: HTTP/1.0 Decoder — Body Parsing
**Description:** As a developer, I want body-parsing tests to verify
Content-Length framing, no-body responses (304, 204), duplicate headers,
and binary body preservation per RFC 1945 §7.

**File:** `src/TurboHttp.Tests/Http10DecoderTests.cs`

**Acceptance Criteria:**
- [ ] `1945-dec-004` — 304 Not Modified with Content-Length: body empty
- [ ] `1945-dec-004b` — 304 Not Modified without Content-Length: body empty
- [ ] `dec1-nb-001` — 204 No Content: body length = 0
- [ ] `1945-7-001` — Content-Length body decoded to exact byte count
- [ ] `1945-7-002` — Zero Content-Length → empty body
- [ ] `1945-7-003` — No Content-Length → read until EOF via `TryDecodeEof`
- [ ] `1945-7-005` — Two different Content-Length values → error
- [ ] `1945-7-005b` — Two identical Content-Length values accepted (P1)
- [ ] `1945-7-006` — Negative Content-Length → `InvalidContentLength`
- [ ] `dec1-body-001` — Binary body with null bytes decoded intact
- [ ] `dec1-body-002` — 2 MB body decoded correctly (P1)
- [ ] `1945-dec-006` — Chunked transfer treated as raw bytes (P1)
- [ ] All new tests green

---

### US-007: HTTP/1.0 Decoder — Connection Semantics + TCP Fragmentation
**Description:** As a developer, I want connection-semantics tests (RFC 1945 §8)
and TCP fragmentation tests so that the decoder handles partial reads correctly.

**File:** `src/TurboHttp.Tests/Http10DecoderTests.cs`

**Acceptance Criteria:**
- [ ] `1945-8-001` — Default connection is close
- [ ] `1945-8-002` — `Connection: keep-alive` recognized (P1)
- [ ] `1945-8-003` — Keep-Alive timeout/max parameters parsed (P1)
- [ ] `1945-8-004` — HTTP/1.0 does not default to keep-alive
- [ ] `1945-8-005` — `Connection: close` sets close flag
- [ ] `dec1-frag-001` — Status-line split at byte 1 reassembled
- [ ] `dec1-frag-002` — Status-line split inside version reassembled
- [ ] `dec1-frag-003` — Header name split across two reads
- [ ] `dec1-frag-004` — Header value split across two reads
- [ ] `dec1-frag-005` — Body split mid-content reassembled
- [ ] All new tests green

---

### US-008: HTTP/1.1 Encoder — Request-Line
**Description:** As a developer, I want request-line tests for the HTTP/1.1
encoder so that HTTP/1.1 version, all 9 methods, URI edge cases, and CRLF
termination are verified per RFC 7230 §3.1.1.

**File:** `src/TurboHttp.Tests/Http11EncoderTests.cs`

**Acceptance Criteria:**
- [ ] `7230-enc-001` — Request-line uses `HTTP/1.1`
- [ ] `7230-3.1.1-002` — Lowercase method causes exception
- [ ] `7230-3.1.1-004` — Every request-line ends with `\r\n`
- [ ] `enc3-m-001` — `[Theory]` × 9: GET POST PUT DELETE PATCH HEAD OPTIONS TRACE CONNECT
- [ ] `enc3-uri-001` — `OPTIONS * HTTP/1.1` encoded correctly
- [ ] `enc3-uri-002` — Absolute-URI preserved for proxy request
- [ ] `enc3-uri-003` — Missing path normalized to `/`
- [ ] `enc3-uri-004` — Query string preserved verbatim
- [ ] `enc3-uri-005` — Fragment stripped from request-target
- [ ] `enc3-uri-006` — Existing percent-encoding not re-encoded (P1)
- [ ] All new tests green

---

### US-009: HTTP/1.1 Encoder — Host Header
**Description:** As a developer, I want Host header tests so that the
mandatory Host header is always emitted exactly once, with correct port
handling and IPv6 literals per RFC 9112 §5.4.

**File:** `src/TurboHttp.Tests/Http11EncoderTests.cs`

**Acceptance Criteria:**
- [ ] `9112-enc-001` — `Host:` always present
- [ ] `9112-enc-002` — `Host:` emitted exactly once
- [ ] `enc3-host-001` — Non-standard port included in `Host` value
- [ ] `enc3-host-002` — IPv6 host literal bracketed: `Host: [::1]`
- [ ] `enc3-host-003` — Default port 80 omitted from `Host` header
- [ ] All new tests green

---

### US-010: HTTP/1.1 Encoder — Header Encoding
**Description:** As a developer, I want header-encoding tests for format,
whitespace, casing, security, and common header values per RFC 7230 §3.2.

**File:** `src/TurboHttp.Tests/Http11EncoderTests.cs`

**Acceptance Criteria:**
- [ ] `7230-3.2-001` — Header format is `Name: SP value CRLF`
- [ ] `7230-3.2-002` — No spurious whitespace added to header values
- [ ] `7230-3.2-007` — Header name casing preserved (not lowercased)
- [ ] `enc3-hdr-001` — NUL byte in header value throws `ArgumentException`
- [ ] `enc3-hdr-002` — `Content-Type` with semicolon parameters preserved (P1)
- [ ] `enc3-hdr-003` — All custom headers appear in output (P1)
- [ ] `enc3-hdr-004` — `Accept-Encoding: gzip, deflate` encoded (P1)
- [ ] `enc3-hdr-005` — `Authorization: Bearer …` preserved verbatim (P1)
- [ ] All new tests green

---

### US-011: HTTP/1.1 Encoder — Connection Management + Body Encoding
**Description:** As a developer, I want connection and body tests so that
keep-alive/close headers, Content-Length, chunked encoding, and binary bodies
are correctly encoded per RFC 7230 §3.3 and §6.1.

**File:** `src/TurboHttp.Tests/Http11EncoderTests.cs`

**Acceptance Criteria:**
- [ ] `7230-enc-003` — `Connection: keep-alive` default
- [ ] `7230-enc-004` — `Connection: close` when explicitly set
- [ ] `7230-6.1-005` — Multiple `Connection` tokens encoded (P1)
- [ ] `9112-enc-003` — `TE`, `Trailers`, `Keep-Alive` stripped
- [ ] `7230-enc-006` — No `Content-Length` for bodyless GET
- [ ] `7230-enc-008` — `Content-Length` set for POST body
- [ ] `7230-enc-009` — `Transfer-Encoding: chunked` + correct chunks (P1)
- [ ] `enc3-body-001` — `[Theory]` × 3: POST/PUT/PATCH each get `Content-Length`
- [ ] `enc3-body-002` — `[Theory]` × 3: GET/HEAD/DELETE omit `Content-Length`
- [ ] `enc3-body-003` — `\r\n\r\n` separates headers from body
- [ ] `enc3-body-004` — Binary body with null bytes preserved
- [ ] `enc3-body-005` — Chunked body ends with `0\r\n\r\n` (P1)
- [ ] `enc3-body-006` — No `Content-Length` when chunked
- [ ] All new tests green

---

### US-012: HTTP/1.1 Decoder — Status-Line and 1xx Handling
**Description:** As a developer, I want status-line tests so that all 2xx–5xx
codes, 1xx interim responses, custom codes, and edge cases are decoded per
RFC 7231 §6.1.

**File:** `src/TurboHttp.Tests/Http11DecoderTests.cs`

**Acceptance Criteria:**
- [ ] `7231-6.1-002a` — `[Theory]` × 8: 2xx codes (200–207)
- [ ] `7231-6.1-003a` — `[Theory]` × 7: 3xx codes (300–308)
- [ ] `7231-6.1-004a` — `[Theory]` × 12: 4xx codes
- [ ] `7231-6.1-005a` — `[Theory]` × 5: 5xx codes
- [ ] `7231-6.1-001` — 1xx informational response has no body
- [ ] `dec4-1xx-001` — `[Theory]` × 4: 100/101/102/103 each no body
- [ ] `dec4-1xx-002` — 100 Continue before 200 OK decoded correctly
- [ ] `dec4-1xx-003` — Multiple 1xx interim responses then 200
- [ ] `7231-6.1-006` — Custom status code 599 parsed (P1)
- [ ] `7231-6.1-007` — Status > 599 rejected
- [ ] `7231-6.1-008` — Empty reason phrase valid
- [ ] All new tests green

---

### US-013: HTTP/1.1 Decoder — Header Parsing
**Description:** As a developer, I want header-parsing tests so that OWS
trimming, empty values, duplicate headers, obs-fold rejection, malformed
names, and case-insensitivity are all handled per RFC 7230 §3.2.

**File:** `src/TurboHttp.Tests/Http11DecoderTests.cs`

**Acceptance Criteria:**
- [ ] `7230-3.2-001` — Standard `Name: value` parsed
- [ ] `7230-3.2-002` — OWS trimmed from header value
- [ ] `7230-3.2-003` — Empty header value accepted
- [ ] `7230-3.2-004` — Multiple same-name headers both accessible
- [ ] `7230-3.2-005` — Obs-fold rejected in HTTP/1.1 → `ObsoleteFoldingDetected`
- [ ] `7230-3.2-006` — Header without colon → `InvalidHeader`
- [ ] `7230-3.2-007` — Case-insensitive header name lookup
- [ ] `7230-3.2-008` — Space in header name → `InvalidFieldName`
- [ ] `dec4-hdr-001` — Tab in header value accepted (P1)
- [ ] `dec4-hdr-002` — Quoted-string header value parsed
- [ ] `dec4-hdr-003` — `Content-Type` with parameters parsed
- [ ] All new tests green

---

### US-014: HTTP/1.1 Decoder — Message Body (RFC 7230 §3.3)
**Description:** As a developer, I want message-body tests so that
Content-Length framing, empty bodies, conflict detection, and binary
content are all verified.

**File:** `src/TurboHttp.Tests/Http11DecoderTests.cs`

**Acceptance Criteria:**
- [ ] `7230-3.3-001` — Content-Length body decoded to exact byte count
- [ ] `7230-3.3-002` — Zero Content-Length → empty body
- [ ] `7230-3.3-003` — Chunked response body decoded (all chunks concatenated)
- [ ] `7230-3.3-004` — Transfer-Encoding chunked takes priority over CL
- [ ] `7230-3.3-005` — Multiple Content-Length values rejected
- [ ] `7230-3.3-006` — Negative Content-Length rejected
- [ ] `7230-3.3-007` — Response without body framing has empty body (204/304)
- [ ] `dec4-body-001` — 10 MB body decoded with correct Content-Length
- [ ] `dec4-body-002` — Binary body with null bytes intact
- [ ] All new tests green

---

### US-015: HTTP/1.1 Decoder — Chunked Transfer Encoding (RFC 7230 §4.1)
**Description:** As a developer, I want chunked-encoding tests so that single
chunks, multiple chunks, extensions, trailers, malformed sizes, and edge cases
are all handled correctly.

**File:** `src/TurboHttp.Tests/Http11DecoderTests.cs`

**Acceptance Criteria:**
- [ ] `7230-4.1-001` — Single chunk decoded: `5\r\nHello\r\n0\r\n\r\n` → `Hello`
- [ ] `7230-4.1-002` — Multiple chunks concatenated correctly
- [ ] `7230-4.1-003` — Chunk extensions silently ignored (P1)
- [ ] `7230-4.1-004` — Trailer fields after final chunk accessible (P1)
- [ ] `7230-4.1-005` — Non-hex chunk size → `InvalidChunkedEncoding`
- [ ] `7230-4.1-006` — Missing final chunk → `NeedMoreData` / `Incomplete()`
- [ ] `7230-4.1-007` — `0\r\n\r\n` terminates chunked body
- [ ] `7230-4.1-008` — Chunk size overflow → parse error
- [ ] `dec4-chk-001` — 1-byte chunk decoded: `1\r\nX\r\n0\r\n\r\n` → `X`
- [ ] `dec4-chk-002` — Uppercase hex chunk size accepted
- [ ] `dec4-chk-003` — Empty chunk before terminator accepted (P1)
- [ ] All new tests green

---

### US-016: HTTP/1.1 Decoder — No-Body Responses + Connection Semantics
**Description:** As a developer, I want no-body and connection-semantics tests
so that 204/304/HEAD responses have empty bodies and connection persistence
flags are correctly detected per RFC 7230 §6.1.

**File:** `src/TurboHttp.Tests/Http11DecoderTests.cs`

**Acceptance Criteria:**
- [ ] `7230-nb-001` — 204 No Content: empty body
- [ ] `7230-nb-002` — 304 Not Modified: empty body
- [ ] `dec4-nb-001` — `[Theory]` × 3: 204/205/304 always empty body
- [ ] `dec4-nb-002` — HEAD response: `Content-Length` header present, no body bytes
- [ ] `7230-6.1-001` — `Connection: close` signals connection close
- [ ] `7230-6.1-002` — `Connection: keep-alive` signals reuse (P1)
- [ ] `7230-6.1-003` — HTTP/1.1 default connection is keep-alive
- [ ] `7230-6.1-004` — HTTP/1.0 default connection is close
- [ ] `7230-6.1-005` — Multiple `Connection` tokens all recognized (P1)
- [ ] All new tests green

---

### US-017: HTTP/1.1 Decoder — Date/Time Parsing + Pipelining
**Description:** As a developer, I want Date header parsing tests (RFC 7231
§7.1.1.1) and pipelining tests so that all three date formats and back-to-back
responses are handled.

**File:** `src/TurboHttp.Tests/Http11DecoderTests.cs`

**Acceptance Criteria:**
- [ ] `7231-7.1.1-001` — IMF-fixdate parsed to `DateTimeOffset` (P1)
- [ ] `7231-7.1.1-002` — RFC 850 obsolete format accepted (P1)
- [ ] `7231-7.1.1-003` — ANSI C asctime format accepted (P1)
- [ ] `7231-7.1.1-004` — Non-GMT timezone rejected or ignored (P1)
- [ ] `7231-7.1.1-005` — Invalid Date value handled gracefully (P1)
- [ ] `7230-pipe-001` — Two pipelined responses decoded (P1)
- [ ] `7230-pipe-002` — Partial second response buffered as remainder (P1)
- [ ] `dec4-pipe-001` — Three pipelined responses decoded in order (P1)
- [ ] All new tests green

---

### US-018: HTTP/1.1 Decoder — TCP Fragmentation
**Description:** As a developer, I want TCP fragmentation tests so that the
HTTP/1.1 decoder correctly reassembles partial reads at all critical split
points.

**File:** `src/TurboHttp.Tests/Http11DecoderTests.cs`

**Acceptance Criteria:**
- [ ] `dec4-frag-001` — Status-line split at byte 1 reassembled
- [ ] `dec4-frag-002` — Status-line split inside `HTTP/1.1` version
- [ ] `dec4-frag-003` — Header split at colon
- [ ] `dec4-frag-004` — Split at `\r\n\r\n` header-body boundary
- [ ] `dec4-frag-005` — Chunk-size line split across two reads
- [ ] `dec4-frag-006` — Chunk data split mid-content
- [ ] `dec4-frag-007` — Response delivered 1 byte at a time assembles correctly
- [ ] All new tests green

---

### US-019: Range Requests (RFC 7233) — Encoder + Decoder
**Description:** As a developer, I want Range request encoder tests and
206 Partial Content decoder tests so that byte-range requests and responses
are handled per RFC 7233 §2.1 and §4.1.

**Files:** `Http11EncoderTests.cs`, `Http11DecoderTests.cs`

**Acceptance Criteria:**
- [ ] `7233-2.1-001` — `Range: bytes=0-499` encoded (P2)
- [ ] `7233-2.1-002` — Suffix range `bytes=-500` encoded (P2)
- [ ] `7233-2.1-003` — Open-ended range `bytes=500-` encoded (P2)
- [ ] `7233-2.1-004` — Multi-range comma-separated encoded (P2)
- [ ] `7233-2.1-005` — Invalid range rejected (P2)
- [ ] `7233-4.1-001` — `Content-Range: bytes 0-499/1000` parsed (P2)
- [ ] `7233-4.1-002` — 206 Partial Content with Content-Range decoded (P2)
- [ ] `7233-4.1-003` — `multipart/byteranges` body decoded (P2)
- [ ] `7233-4.1-004` — Unknown total length (`*`) accepted (P2)
- [ ] All new tests compile and green

---

### US-020: HTTP/2 Encoder — Connection Preface + Pseudo-Headers
**Description:** As a developer, I want connection preface and pseudo-header
tests so that the correct 24-byte client magic, SETTINGS frame, and all four
pseudo-headers are produced per RFC 7540 §3.5 and §8.1.2.

**File:** `src/TurboHttp.Tests/Http2EncoderTests.cs`

**Acceptance Criteria:**
- [ ] `7540-3.5-001` — Client preface bytes = `PRI * HTTP/2.0\r\n\r\nSM\r\n\r\n`
- [ ] `7540-3.5-003` — SETTINGS frame immediately follows client preface
- [ ] `7540-8.1-001` — All four pseudo-headers emitted (`:method` `:scheme` `:authority` `:path`)
- [ ] `7540-8.1-002` — Pseudo-headers precede all regular headers
- [ ] `7540-8.1-003` — No duplicate pseudo-headers
- [ ] `7540-8.1-004` — `Connection`, `Keep-Alive`, `Upgrade` absent in HTTP/2
- [ ] `enc5-ph-001` — `[Theory]` × 9: `:method` correct for all HTTP methods
- [ ] `enc5-ph-002` — `:scheme http` and `:scheme https` reflect URI scheme
- [ ] All new tests green

---

### US-021: HTTP/2 Encoder — SETTINGS Frame
**Description:** As a developer, I want SETTINGS frame encoder tests so that
all 6 SETTINGS parameters are encoded correctly and SETTINGS ACK has the
right structure per RFC 7540 §6.5.

**File:** `src/TurboHttp.Tests/Http2EncoderTests.cs`

**Acceptance Criteria:**
- [ ] `enc5-set-001` — `[Theory]` × 6: HeaderTableSize, EnablePush, MaxConcurrentStreams,
  InitialWindowSize, MaxFrameSize, MaxHeaderListSize all encoded correctly
- [ ] `enc5-set-002` — SETTINGS ACK has type=`0x04` flags=`0x01` stream=0
- [ ] All new tests green

---

### US-022: HTTP/2 Encoder — Stream IDs + HEADERS Frame
**Description:** As a developer, I want stream ID and HEADERS frame tests so
that odd client IDs, incrementing order, END_STREAM, END_HEADERS flags, and
frame structure are correct per RFC 7540 §5.1 and §6.2.

**File:** `src/TurboHttp.Tests/Http2EncoderTests.cs`

**Acceptance Criteria:**
- [ ] `7540-5.1-001` — First request uses stream ID 1
- [ ] `7540-5.1-002` — Stream IDs increment as 1, 3, 5, …
- [ ] `enc5-sid-001` — Client never produces even stream IDs
- [ ] `enc5-sid-002` — Stream ID near 2^31 handled gracefully (P1)
- [ ] `7540-6.2-001` — HEADERS frame has correct 9-byte header, type `0x01`
- [ ] `7540-6.2-002` — END_STREAM set on HEADERS for GET (bodyless)
- [ ] `7540-6.2-003` — END_HEADERS set on single HEADERS frame
- [ ] All new tests green

---

### US-023: HTTP/2 Encoder — CONTINUATION + DATA Frames
**Description:** As a developer, I want CONTINUATION and DATA frame encoder
tests so that header block splitting, final frame flags, DATA type byte, and
stream IDs in DATA frames are verified per RFC 7540 §6.1 and §6.9.

**File:** `src/TurboHttp.Tests/Http2EncoderTests.cs`

**Acceptance Criteria:**
- [ ] `7540-6.9-001` — Headers exceeding max frame size split into CONTINUATION
- [ ] `7540-6.9-002` — END_HEADERS on final CONTINUATION frame
- [ ] `7540-6.9-003` — Multiple CONTINUATION frames for very large headers (P1)
- [ ] `7540-6.1-002enc` — END_STREAM set on final DATA frame
- [ ] `7540-6.1-003enc` — GET uses END_STREAM on HEADERS, no DATA
- [ ] `enc5-data-001` — DATA frame type byte = `0x00`
- [ ] `enc5-data-002` — DATA frame carries correct stream ID
- [ ] `enc5-data-003` — Body > MAX_FRAME_SIZE split into multiple DATA frames (P1)
- [ ] All new tests green

---

### US-024: HTTP/2 Encoder — Flow Control
**Description:** As a developer, I want encoder-side flow control tests so
that the encoder respects stream and connection windows, blocks on zero window,
and resumes after WINDOW_UPDATE per RFC 7540 §5.2.

**File:** `src/TurboHttp.Tests/Http2EncoderTests.cs`

**Acceptance Criteria:**
- [ ] `7540-5.2-001enc` — Encoder does not exceed initial 65535-byte window
- [ ] `7540-5.2-002enc` — WINDOW_UPDATE allows more DATA to be sent
- [ ] `7540-5.2-005enc` — Encoder blocks when window is zero
- [ ] `7540-5.2-006enc` — Connection-level window limits total DATA
- [ ] `7540-5.2-007enc` — Per-stream window limits DATA on that stream
- [ ] All new tests green

---

### US-025: HTTP/2 Decoder — Connection Preface + Frame Header
**Description:** As a developer, I want decoder-side preface and frame header
tests so that invalid prefaces, 24-bit length fields, all frame types, unknown
types, R-bit masking, and oversized frames are handled per RFC 7540 §3.5
and §4.1.

**File:** `src/TurboHttp.Tests/Http2DecoderTests.cs`

**Acceptance Criteria:**
- [ ] `7540-3.5-002` — Invalid server preface → `PROTOCOL_ERROR`
- [ ] `7540-3.5-004` — Missing SETTINGS after preface → error
- [ ] `7540-4.1-001` — Valid 9-byte frame header decoded correctly
- [ ] `7540-4.1-002` — 24-bit length field parsed (lengths > 65535)
- [ ] `7540-4.1-003` — `[Theory]` × 10: all frame types 0x00–0x09 dispatched
- [ ] `7540-4.1-004` — Unknown frame type 0x0A ignored
- [ ] `7540-4.1-005` — R-bit masked out when reading stream ID
- [ ] `7540-4.1-006` — R-bit set → `PROTOCOL_ERROR`
- [ ] `7540-4.1-007` — Frame > MAX_FRAME_SIZE → `FRAME_SIZE_ERROR`
- [ ] All new tests green

---

### US-026: HTTP/2 Decoder — All 14 Error Codes
**Description:** As a developer, I want one test per RFC 7540 error code so
that every `Http2ErrorCode` enum value can be decoded from GOAWAY or
RST_STREAM frames.

**File:** `src/TurboHttp.Tests/Http2DecoderTests.cs`

**Acceptance Criteria:**
- [ ] `7540-err-000` — `NO_ERROR (0x0)` in GOAWAY decoded
- [ ] `7540-err-001` — `PROTOCOL_ERROR (0x1)` in RST_STREAM decoded
- [ ] `7540-err-002` — `INTERNAL_ERROR (0x2)` in GOAWAY decoded
- [ ] `7540-err-003` — `FLOW_CONTROL_ERROR (0x3)` in GOAWAY decoded
- [ ] `7540-err-004` — `SETTINGS_TIMEOUT (0x4)` in GOAWAY decoded
- [ ] `7540-err-005` — `STREAM_CLOSED (0x5)` in RST_STREAM decoded
- [ ] `7540-err-006` — `FRAME_SIZE_ERROR (0x6)` decoded
- [ ] `7540-err-007` — `REFUSED_STREAM (0x7)` in RST_STREAM decoded
- [ ] `7540-err-008` — `CANCEL (0x8)` in RST_STREAM decoded
- [ ] `7540-err-009` — `COMPRESSION_ERROR (0x9)` in GOAWAY decoded
- [ ] `7540-err-00a` — `CONNECT_ERROR (0xa)` (P1)
- [ ] `7540-err-00b` — `ENHANCE_YOUR_CALM (0xb)` (P1)
- [ ] `7540-err-00c` — `INADEQUATE_SECURITY (0xc)` (P1)
- [ ] `7540-err-00d` — `HTTP_1_1_REQUIRED (0xd)` in GOAWAY decoded
- [ ] All new tests green

---

### US-027: HTTP/2 Decoder — Stream States
**Description:** As a developer, I want stream state tests so that
half-closed-remote, fully closed, PUSH_PROMISE reserved, and error cases for
closed/reused streams are verified per RFC 7540 §5.1.

**File:** `src/TurboHttp.Tests/Http2DecoderTests.cs`

**Acceptance Criteria:**
- [ ] `7540-5.1-003` — END_STREAM on incoming DATA → half-closed remote
- [ ] `7540-5.1-004` — Both sides END_STREAM → stream fully closed
- [ ] `7540-5.1-005` — PUSH_PROMISE → reserved remote state (P1)
- [ ] `7540-5.1-006` — DATA on closed stream → `STREAM_CLOSED`
- [ ] `7540-5.1-007` — Reusing closed stream ID → `PROTOCOL_ERROR`
- [ ] `7540-5.1-008` — Even stream ID from client → `PROTOCOL_ERROR`
- [ ] All new tests green

---

### US-028: HTTP/2 Decoder — Flow Control (Decoder Side)
**Description:** As a developer, I want decoder-side flow control tests so
that window initialization, WINDOW_UPDATE decoding, overflow detection, and
zero-increment rejection are verified per RFC 7540 §5.2.

**File:** `src/TurboHttp.Tests/Http2DecoderTests.cs`

**Acceptance Criteria:**
- [ ] `7540-5.2-001dec` — New stream initial window = 65535
- [ ] `7540-5.2-002dec` — WINDOW_UPDATE decoded, window updated
- [ ] `7540-5.2-003dec` — Peer DATA beyond window → `FLOW_CONTROL_ERROR`
- [ ] `7540-5.2-004dec` — WINDOW_UPDATE overflow → `FLOW_CONTROL_ERROR`
- [ ] `7540-5.2-008dec` — WINDOW_UPDATE increment=0 → `PROTOCOL_ERROR`
- [ ] All new tests green

---

### US-029: HTTP/2 Decoder — DATA Frame
**Description:** As a developer, I want DATA frame decoder tests so that
payload extraction, END_STREAM signalling, padding stripping, and error cases
for invalid stream IDs are verified per RFC 7540 §6.1.

**File:** `src/TurboHttp.Tests/Http2DecoderTests.cs`

**Acceptance Criteria:**
- [ ] `7540-6.1-001` — DATA frame payload decoded correctly
- [ ] `7540-6.1-002` — END_STREAM on DATA marks stream complete
- [ ] `7540-6.1-003` — PADDED DATA: padding stripped (P1)
- [ ] `7540-6.1-004` — DATA on stream 0 → `PROTOCOL_ERROR`
- [ ] `7540-6.1-005` — DATA on closed stream → `STREAM_CLOSED`
- [ ] `7540-6.1-006` — Empty DATA + END_STREAM: empty body, response complete
- [ ] All new tests green

---

### US-030: HTTP/2 Decoder — HEADERS Frame
**Description:** As a developer, I want HEADERS frame decoder tests so that
header block extraction, END_STREAM, END_HEADERS, padding, PRIORITY flag, and
error cases are verified per RFC 7540 §6.2.

**File:** `src/TurboHttp.Tests/Http2DecoderTests.cs`

**Acceptance Criteria:**
- [ ] `7540-6.2-001` — HEADERS frame decoded into response headers
- [ ] `7540-6.2-002` — END_STREAM on HEADERS closes stream immediately
- [ ] `7540-6.2-003` — END_HEADERS marks header block complete
- [ ] `7540-6.2-004` — PADDED HEADERS: padding stripped (P1)
- [ ] `7540-6.2-005` — PRIORITY flag consumed correctly (P1)
- [ ] `7540-6.2-006` — HEADERS without END_HEADERS waits for CONTINUATION
- [ ] `7540-6.2-007` — HEADERS on stream 0 → `PROTOCOL_ERROR`
- [ ] All new tests green

---

### US-031: HTTP/2 Decoder — CONTINUATION Frame
**Description:** As a developer, I want CONTINUATION frame decoder tests so
that multi-frame header blocks are reassembled correctly and protocol errors
for wrong stream, interleaving, and orphaned CONTINUATION are caught per
RFC 7540 §6.9.

**File:** `src/TurboHttp.Tests/Http2DecoderTests.cs`

**Acceptance Criteria:**
- [ ] `7540-6.9-001` — CONTINUATION appended to HEADERS block
- [ ] `7540-6.9-002dec` — END_HEADERS on final CONTINUATION completes block
- [ ] `7540-6.9-003` — Multiple CONTINUATION frames all merged
- [ ] `7540-6.9-004` — CONTINUATION on wrong stream → `PROTOCOL_ERROR`
- [ ] `7540-6.9-005` — Non-CONTINUATION after HEADERS → `PROTOCOL_ERROR`
- [ ] `7540-6.9-006` — CONTINUATION on stream 0 → `PROTOCOL_ERROR`
- [ ] `dec6-cont-001` — CONTINUATION without preceding HEADERS → `PROTOCOL_ERROR`
- [ ] All new tests green

---

### US-032: HTTP/2 Decoder — SETTINGS, PING, GOAWAY, RST_STREAM
**Description:** As a developer, I want control frame decoder tests so that
SETTINGS parsing, ACK generation, PING echo, GOAWAY stream/error tracking, and
RST_STREAM stream closure are all verified per RFC 7540 §6.4–§6.8.

**File:** `src/TurboHttp.Tests/Http2DecoderTests.cs`

**Acceptance Criteria:**
- [ ] `7540-set-001` — Server SETTINGS decoded (`HasNewSettings = true`)
- [ ] `7540-set-002` — SETTINGS ACK generated after SETTINGS received
- [ ] `7540-set-003` — MAX_FRAME_SIZE applied from SETTINGS
- [ ] `dec6-set-001` — `[Theory]` × 6: all 6 SETTINGS parameters decoded
- [ ] `dec6-set-002` — SETTINGS ACK with payload → `FRAME_SIZE_ERROR`
- [ ] `dec6-set-003` — Unknown SETTINGS ID accepted and ignored (P1)
- [ ] `7540-ping-001` — PING request from server decoded (P1)
- [ ] `7540-ping-002` — PING ACK produced for server PING (P1)
- [ ] `dec6-ping-001` — PING ACK carries same 8 payload bytes (P1)
- [ ] `7540-goaway-001` — GOAWAY decoded with last stream ID + error code
- [ ] `7540-goaway-002` — No new streams accepted after GOAWAY
- [ ] `dec6-goaway-001` — GOAWAY debug data bytes accessible (P1)
- [ ] `7540-rst-001` — RST_STREAM decoded (`RstStreams` entry present)
- [ ] `7540-rst-002` — Stream closed after RST_STREAM
- [ ] All new tests green

---

### US-033: HTTP/2 Decoder — TCP Fragmentation
**Description:** As a developer, I want TCP fragmentation tests so that the
HTTP/2 decoder correctly reassembles partial reads at critical byte boundaries
within frame headers, DATA payloads, HPACK blocks, and CONTINUATION sequences.

**File:** `src/TurboHttp.Tests/Http2DecoderTests.cs`

**Acceptance Criteria:**
- [ ] `dec6-frag-001` — Frame header split at byte 1 reassembled
- [ ] `dec6-frag-002` — Frame header split at byte 5 reassembled
- [ ] `dec6-frag-003` — DATA payload split across two reads
- [ ] `dec6-frag-004` — HPACK block split across two reads
- [ ] `dec6-frag-005` — Two complete frames in single read both decoded (P1)
- [ ] All new tests green

---

### US-034: HPACK — All 61 Static Table Entries
**Description:** As a developer, I want a single `[Theory]` test with 61
`[InlineData]` rows so that every RFC 7541 Appendix A static table entry
round-trips correctly as an indexed representation.

**File:** `src/TurboHttp.Tests/HpackTests.cs`

**Acceptance Criteria:**
- [ ] `7541-st-001` — `[Theory]` × 61 rows: each entry index 1–61 round-trips
  as `0x80 | index` (encode → decode → correct name + value)
- [ ] Entries covered: `:authority`, `:method GET/POST`, `:path /`, `:scheme http/https`,
  `:status 200/204/206/304/400/404/500`, all 46 standard headers (accept-charset … www-authenticate)
- [ ] All 61 assertions pass in a single test run
- [ ] All new tests green

---

### US-035: HPACK — Sensitive Headers (NeverIndexed)
**Description:** As a developer, I want NeverIndexed tests so that
`authorization`, `cookie`, `set-cookie`, and `proxy-authorization` headers
are encoded with the `0x10` byte prefix and never inserted into the dynamic
table per RFC 7541 §7.1.3.

**File:** `src/TurboHttp.Tests/HpackTests.cs`

**Acceptance Criteria:**
- [ ] `7541-ni-001` — `[Theory]` × 4: each sensitive header uses `0x10` prefix
- [ ] `7541-ni-002` — `[Theory]` × 4: each sensitive header does NOT grow the dynamic table
- [ ] `7541-ni-003` — Decoded authorization header preserves NeverIndex flag
- [ ] All new tests green

---

### US-036: HPACK — Dynamic Table Operations
**Description:** As a developer, I want dynamic table tests so that insertion,
FIFO eviction, resize from SETTINGS, size-0 clear, overflow detection, and
the 32-byte per-entry overhead are all verified per RFC 7541 §2.3.

**File:** `src/TurboHttp.Tests/HpackTests.cs`

**Acceptance Criteria:**
- [ ] `7541-2.3-001` — Incrementally indexed header added at dynamic index 62
- [ ] `7541-2.3-002` — Oldest entry evicted when dynamic table is full
- [ ] `7541-2.3-003` — Dynamic table resized on `SETTINGS_HEADER_TABLE_SIZE`
- [ ] `7541-2.3-004` — Table size 0 evicts all entries
- [ ] `7541-2.3-005` — Table size exceeding maximum → `HpackException`
- [ ] `hpack-dt-001` — Entry size = name length + value length + 32 bytes
- [ ] `hpack-dt-002` — Size update prefix emitted before first header after resize
- [ ] `hpack-dt-003` — Three entries evicted in FIFO order
- [ ] All new tests green

---

### US-037: HPACK — Integer Representation
**Description:** As a developer, I want integer encoding/decoding tests so
that small integers, multi-byte continuations, all prefix widths (1–7 bits),
the maximum value, and overflow are verified per RFC 7541 §5.1.

**File:** `src/TurboHttp.Tests/HpackTests.cs`

**Acceptance Criteria:**
- [ ] `7541-5.1-001` — Integer smaller than prefix limit encodes in one byte
- [ ] `7541-5.1-002` — Integer at prefix limit requires continuation bytes
- [ ] `7541-5.1-003` — Maximum integer 2147483647 round-trips exactly
- [ ] `7541-5.1-004` — Integer exceeding 2^31-1 → `HpackException`
- [ ] `hpack-int-001` — `[Theory]` × 7: boundary values for 1–7 bit prefixes
- [ ] All new tests green

---

### US-038: HPACK — String Representation + Huffman Edge Cases
**Description:** As a developer, I want string representation tests so that
plain and Huffman-encoded strings, empty strings, large strings, invalid
Huffman data, and EOS padding rules are all verified per RFC 7541 §5.2.

**File:** `src/TurboHttp.Tests/HpackTests.cs`

**Acceptance Criteria:**
- [ ] `7541-5.2-001` — Plain string literal (H=0) decoded
- [ ] `7541-5.2-002` — Huffman-encoded string (H=1) decoded
- [ ] `7541-5.2-003` — Empty string literal decoded
- [ ] `7541-5.2-004` — String larger than 8 KB decoded without truncation (P1)
- [ ] `7541-5.2-005` — Malformed Huffman data → `HpackException`
- [ ] `hpack-str-001` — Non-1 EOS padding bits → `HpackException`
- [ ] `hpack-str-002` — EOS padding > 7 bits → `HpackException`
- [ ] All new tests green

---

### US-039: HPACK — Indexed + Literal Header Field Representations
**Description:** As a developer, I want indexed and literal header field tests
so that all three literal variants (incremental, without-indexing, never-index)
and out-of-range index errors are verified per RFC 7541 §6.1 and §6.2.

**File:** `src/TurboHttp.Tests/HpackTests.cs`

**Acceptance Criteria:**
- [ ] `7541-6.1-002` — Dynamic table entry at index 62+ retrieved
- [ ] `7541-6.1-003` — Out-of-range index → `HpackException`
- [ ] `hpack-idx-001` — Index 0 is invalid → `HpackException`
- [ ] `7541-6.2-001` — Incremental indexing: entry added at index 62
- [ ] `7541-6.2-002` — Without-indexing: NOT added to dynamic table
- [ ] `7541-6.2-003` — Never-indexed: NOT added, flag preserved
- [ ] `7541-6.2-004` — Indexed name + literal value decoded
- [ ] `7541-6.2-005` — Both name and value as literals decoded
- [ ] All new tests green

---

### US-040: HPACK — RFC 7541 Appendix C Byte-Exact Vectors
**Description:** As a developer, I want byte-exact tests from RFC 7541
Appendix C so that our HPACK encoder/decoder matches the reference test
vectors to the byte, including Huffman variants and multi-request state.

**File:** `src/TurboHttp.Tests/HpackTests.cs`

**Acceptance Criteria:**
- [ ] `7541-C.2-001` — Appendix C.2.1: first request, no Huffman
- [ ] `7541-C.2-002` — Appendix C.2.2: dynamic table first referenced entry
- [ ] `7541-C.2-003` — Appendix C.2.3: third request, table state correct
- [ ] `7541-C.3-001` — Appendix C.3: requests with Huffman encoding
- [ ] `7541-C.4-001` — Appendix C.4.1: response, no Huffman
- [ ] `7541-C.4-002` — Appendix C.4.2: response, dynamic table reused
- [ ] `7541-C.4-003` — Appendix C.4.3: response, table state after C.4.2
- [ ] `7541-C.5-001` — Appendix C.5: responses with Huffman
- [ ] `7541-C.6-001` — Appendix C.6: large cookie responses (P1)
- [ ] All new tests green

---

### US-041: Security — HTTP/1.1 Input Limits
**Description:** As a developer, I want input-limit tests for the HTTP/1.1
decoder so that configurable limits on header count, header block size, single
header value size, and body size are enforced.

**New file:** `src/TurboHttp.Tests/Http11SecurityTests.cs`

**Acceptance Criteria:**
- [ ] `sec-001a` — 100 headers accepted at default limit
- [ ] `sec-001b` — 101 headers rejected above default limit
- [ ] `sec-001c` — Custom header count limit respected (P1)
- [ ] `sec-002a` — 8191-byte header block accepted
- [ ] `sec-002b` — 8193-byte header block rejected
- [ ] `sec-002c` — Single 9000-byte header value rejected
- [ ] `sec-003a` — Body at 10 MB limit accepted
- [ ] `sec-003b` — Body exceeding 10 MB rejected
- [ ] `sec-003c` — Zero body limit rejects any body (P1)
- [ ] File created at `src/TurboHttp.Tests/Http11SecurityTests.cs`
- [ ] All new tests green

---

### US-042: Security — HTTP Smuggling + State Isolation
**Description:** As a developer, I want HTTP request-smuggling prevention tests
and decoder state isolation tests so that CRLF injection, TE+CL conflicts, NUL
bytes, and dirty state after Reset() are caught.

**New file:** `src/TurboHttp.Tests/Http11SecurityTests.cs`

**Acceptance Criteria:**
- [ ] `sec-005a` — `Transfer-Encoding` + `Content-Length` conflict rejected
- [ ] `sec-005b` — CRLF injection in header value rejected → `InvalidFieldValue`
- [ ] `sec-005c` — NUL byte in decoded header value rejected → `InvalidFieldValue`
- [ ] `sec-006a` — `Reset()` after partial headers restores clean state
- [ ] `sec-006b` — `Reset()` after partial body restores clean state
- [ ] All new tests green

---

### US-043: Security — HTTP/2 Limits + Protocol Protection
**Description:** As a developer, I want HTTP/2 security tests so that HPACK
bombs, CONTINUATION floods, rapid-reset CVE-2023-44487, and invalid SETTINGS
values are all rejected.

**New file:** `src/TurboHttp.Tests/Http2SecurityTests.cs`

**Acceptance Criteria:**
- [ ] `sec-h2-001` — HPACK literal name exceeding limit → `HpackException`
- [ ] `sec-h2-002` — HPACK literal value exceeding limit → `HpackException`
- [ ] `sec-h2-003` — Excessive CONTINUATION frames (1000) rejected
- [ ] `sec-h2-004` — 100 streams immediately RST'd triggers protection (P1, CVE-2023-44487)
- [ ] `sec-h2-005` — 10000 zero-length DATA frames rejected (P1)
- [ ] `sec-h2-006` — `SETTINGS_ENABLE_PUSH` > 1 → `PROTOCOL_ERROR`
- [ ] `sec-h2-007` — `SETTINGS_INITIAL_WINDOW_SIZE` > 2^31-1 → `FLOW_CONTROL_ERROR`
- [ ] `sec-h2-008` — Unknown SETTINGS ID silently ignored (P1)
- [ ] File created at `src/TurboHttp.Tests/Http2SecurityTests.cs`
- [ ] All new tests green

---

### US-044: Round-Trip — HTTP/1.1 Encode → Decode
**Description:** As a developer, I want HTTP/1.1 round-trip tests so that the
encoder and decoder work together end-to-end across all common methods,
status codes, body types, pipelining, and keep-alive scenarios.

**New file:** `src/TurboHttp.Tests/Http11RoundTripTests.cs`

**Acceptance Criteria:**
- [ ] `rt11-001` — GET → 200 OK round-trip (empty body)
- [ ] `rt11-002` — POST JSON → 201 Created (Location header)
- [ ] `rt11-003` — PUT → 204 No Content
- [ ] `rt11-004` — DELETE → 200 OK
- [ ] `rt11-005` — PATCH → 200 OK (modified body)
- [ ] `rt11-006` — HEAD → Content-Length present, no body (P1)
- [ ] `rt11-007` — OPTIONS → 200 with Allow header (P1)
- [ ] `rt11-008` — GET → chunked response (body assembled)
- [ ] `rt11-009` — GET → response with 5 chunks concatenated
- [ ] `rt11-010` — Chunked with trailer round-trip (P1)
- [ ] `rt11-011` — GET → 301 with Location header
- [ ] `rt11-012` — POST binary → 200 binary response (bytes preserved)
- [ ] `rt11-013` — GET → 404 Not Found
- [ ] `rt11-014` — GET → 500 Internal Server Error
- [ ] `rt11-015` — Two pipelined requests+responses (P1)
- [ ] `rt11-016` — 100 Continue before 200 OK
- [ ] `rt11-017` — 1 MB body round-trip (all bytes preserved)
- [ ] `rt11-018` — Binary body with null bytes round-trip
- [ ] `rt11-019` — Two responses on keep-alive connection (P1)
- [ ] `rt11-020` — `Content-Type: application/json; charset=utf-8` preserved (P1)
- [ ] File created at `src/TurboHttp.Tests/Http11RoundTripTests.cs`
- [ ] All new tests green

---

### US-045: Round-Trip — HTTP/2 Encode → Decode
**Description:** As a developer, I want HTTP/2 round-trip tests so that the
full connection lifecycle (preface, SETTINGS, requests, HPACK reuse, flow
control, GOAWAY, RST) works correctly end-to-end.

**New file:** `src/TurboHttp.Tests/Http2RoundTripTests.cs`

**Acceptance Criteria:**
- [ ] `rt2-001` — Connection preface + SETTINGS exchange
- [ ] `rt2-002` — GET → 200 on stream 1
- [ ] `rt2-003` — POST with DATA → HEADERS+DATA → 201 response
- [ ] `rt2-004` — Three concurrent streams each complete independently
- [ ] `rt2-005` — HPACK dynamic table reused across three requests
- [ ] `rt2-006` — Server SETTINGS → client ACK → both sides updated
- [ ] `rt2-007` — Server PING → client PONG with same 8-byte payload (P1)
- [ ] `rt2-008` — GOAWAY received → encoder refuses new streams
- [ ] `rt2-009` — RST_STREAM cancels stream; other streams continue
- [ ] `rt2-010` — Authorization header NeverIndexed in round-trip
- [ ] `rt2-011` — Cookie header NeverIndexed in round-trip (P1)
- [ ] `rt2-012` — Headers exceeding frame size via CONTINUATION
- [ ] `rt2-013` — Server PUSH_PROMISE decoded, pushed response received (P1)
- [ ] `rt2-014` — POST body larger than initial window uses WINDOW_UPDATE
- [ ] `rt2-015` — Request → 404 response on stream decoded (P1)
- [ ] File created at `src/TurboHttp.Tests/Http2RoundTripTests.cs`
- [ ] All new tests green

---

### US-046: TCP Fragmentation Matrix — HTTP/1.0 + HTTP/1.1
**Description:** As a developer, I want a systematic TCP fragmentation test
file so that HTTP/1.0 and HTTP/1.1 decoders are verified at every critical
byte-boundary split point including single-byte delivery.

**New file:** `src/TurboHttp.Tests/TcpFragmentationTests.cs`

**Acceptance Criteria:**
- [ ] `frag10-001` — HTTP/1.0 status-line split at byte 1
- [ ] `frag10-002` — HTTP/1.0 status-line split mid-version string
- [ ] `frag10-003` — HTTP/1.0 header name split mid-word
- [ ] `frag10-004` — HTTP/1.0 body split at first byte
- [ ] `frag10-005` — HTTP/1.0 body split at midpoint
- [ ] `frag11-001` — HTTP/1.1 status-line split at byte 1
- [ ] `frag11-002` — HTTP/1.1 status-line split inside version
- [ ] `frag11-003` — HTTP/1.1 header split at colon
- [ ] `frag11-004` — HTTP/1.1 split at first byte of `\r\n\r\n`
- [ ] `frag11-005` — HTTP/1.1 chunk-size line split mid-hex
- [ ] `frag11-006` — HTTP/1.1 chunk data split mid-content
- [ ] `frag11-007` — HTTP/1.1 final `0\r\n\r\n` chunk split
- [ ] `frag11-008` — HTTP/1.1 response delivered 1 byte at a time
- [ ] File created at `src/TurboHttp.Tests/TcpFragmentationTests.cs`
- [ ] All new tests green

---

### US-047: TCP Fragmentation Matrix — HTTP/2
**Description:** As a developer, I want HTTP/2 TCP fragmentation tests in the
same matrix file so that frame header boundaries, DATA payloads, HPACK splits,
CONTINUATION sequences, and multi-stream partial reads are all covered.

**New file:** `src/TurboHttp.Tests/TcpFragmentationTests.cs`

**Acceptance Criteria:**
- [ ] `frag2-001` — Frame header split at byte 1
- [ ] `frag2-002` — Frame header split at byte 3 (end of length field)
- [ ] `frag2-003` — Frame header split at byte 5 (after flags)
- [ ] `frag2-004` — Frame header split at byte 8 (last stream ID byte)
- [ ] `frag2-005` — DATA payload split mid-content
- [ ] `frag2-006` — HPACK block split mid-stream
- [ ] `frag2-007` — Split between HEADERS and CONTINUATION frames
- [ ] `frag2-008` — Two complete frames in one buffer both processed
- [ ] `frag2-009` — Second stream's HEADERS split while first stream active
- [ ] All new tests green
- [ ] `dotnet test ./src/TurboHttp.Tests/TurboHttp.Tests.csproj --filter "ClassName=TurboHttp.Tests.TcpFragmentationTests"` green

---

## Functional Requirements

- **FR-1:** Every new test must have a `[DisplayName]` attribute containing its RFC ID (e.g. `"7230-3.2-001: ..."`)
- **FR-2:** Every new test must follow the naming convention `Should_ExpectedBehavior_When_Condition()`
- **FR-3:** `[Theory]` tests must use `[InlineData]` for parameterization; no `[MemberData]` unless the data cannot fit inline
- **FR-4:** Test files must use `#nullable enable`, Allman braces, 4-space indent, `namespace TurboHttp.Tests;`
- **FR-5:** All temporary buffers in tests must use `ArrayPool<byte>.Shared` and return in `finally`
- **FR-6:** After each user story is implemented, run `dotnet test ./src/TurboHttp.Tests/TurboHttp.Tests.csproj` — zero failures allowed before moving to next story
- **FR-7:** Mark `[ ]` → `[x]` in `CLIENT_ENCODER_DECODER_PLAN.md` after each RFC ID passes
- **FR-8:** New test files required: `Http11SecurityTests.cs`, `Http2SecurityTests.cs`, `Http11RoundTripTests.cs`, `Http2RoundTripTests.cs`, `TcpFragmentationTests.cs`
- **FR-9:** No test may suppress warnings with `#pragma` without a comment explaining why
- **FR-10:** P2 stories (US-019) may be deferred to a follow-up if schedule is tight

---

## Non-Goals

- **No server-side tests** — TurboHttp is client-only; request parsing from the server perspective is out of scope
- **No I/O layer tests** — Akka actors, `TcpConnectionManager`, and `TcpClientRunner` are tested separately
- **No benchmarks** — performance measurement is a separate workstream
- **No h2spec server harness** — external RFC conformance tools are integration-level, not unit-level
- **No new production code** — this PRD only adds tests; if a test reveals a bug, fix it in a separate PR
- **No test duplication** — do not re-implement any of the ~244 tests already in the codebase

---

## Technical Considerations

### Helper pattern for encoder tests
```csharp
private static string Encode(HttpRequestMessage request)
{
    var buffer = new byte[4096];
    var span = buffer.AsSpan();
    var written = Http11Encoder.Encode(request, ref span);
    return Encoding.ASCII.GetString(buffer, 0, written);
}
```

### Helper pattern for decoder tests
```csharp
private static HttpDecodeResult Decode(string raw, out HttpResponseMessage? response)
{
    var decoder = new Http11Decoder();
    var bytes = new ReadOnlyMemory<byte>(Encoding.ASCII.GetBytes(raw));
    return decoder.TryDecode(bytes, out response);
}
```

### TCP fragmentation test pattern
```csharp
// Split at `splitPoint` bytes, feed in two calls
var firstChunk  = new ReadOnlyMemory<byte>(bytes, 0, splitPoint);
var secondChunk = new ReadOnlyMemory<byte>(bytes, splitPoint, bytes.Length - splitPoint);

var r1 = decoder.TryDecode(firstChunk,  out var _);
var r2 = decoder.TryDecode(secondChunk, out var response);

Assert.True(r1.IsIncomplete);
Assert.True(r2.IsSuccess);
```

### Run order
Implement in US-001 → US-047 order. Each story's tests must be green before starting the next.

### Token-safe implementation strategy
Each user story is sized at ≤20 new test methods to prevent hitting context
limits. Implement one user story per conversation session.

---

## Success Metrics

- `dotnet test ./src/TurboHttp.sln` exits 0 with all 47 user stories complete
- All P0 tests green = release unblocked
- All P0 + P1 tests green = production-ready milestone
- All P0 + P1 + P2 tests green = full RFC compliance milestone
- ≥90% line coverage on all files in `src/TurboHttp/Protocol/`
- `CLIENT_ENCODER_DECODER_PLAN.md` progress tracker shows 644/644 `[x]`
- h2spec conformance suite passes (integration test, separate from this PRD)

---

## Open Questions

1. **Header limit default value:** Is the default maximum header count 100? If a different value is configured, US-041 acceptance criteria for `sec-001a`/`sec-001b` must be updated.
2. **Body limit default value:** Is the default maximum body size 10 MB? If different, update `sec-003a`/`sec-003b`.
3. **Range request support (US-019):** RFC 7233 support is P2. Confirm whether `Http11Encoder` already has a `Range` header API or if it must be added before tests can be written.
4. **CVE-2023-44487 protection (sec-h2-004):** Confirm whether the HTTP/2 decoder has any rapid-reset detection; if not, this test should document the expected behavior for a future implementation.
5. **PUSH_PROMISE (rt2-013, 7540-5.1-005):** Confirm whether server push is in scope for the client decoder.
