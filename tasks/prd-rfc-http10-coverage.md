# PRD: RFC 1945 (HTTP/1.0) Test Coverage

## Scope: Client-Side Only

TurboHttp is a **pure HTTP client library**. Only two directions exist:

| Direction | Required |
|---|---|
| `HttpRequestMessage` → bytes (encode request to send) | ✅ In scope |
| bytes → `HttpResponseMessage` (decode received response) | ✅ In scope |
| bytes → `HttpRequestMessage` (server-side request parsing) | ❌ Out of scope |
| `HttpResponseMessage` → bytes (server-side response encoding) | ❌ Out of scope |

---

## Introduction

`Http10Encoder` and `Http10Decoder` implement the HTTP/1.0 client path. While
`Http10DecoderTests.cs` has good coverage of fragmentation and status-line parsing,
several RFC 1945-specific behaviours are not yet tested:

- **Encoder**: No tests verify that HTTP/1.1-specific headers (`Host`, `Transfer-Encoding`,
  `Connection`) are stripped and that `Content-Length` is correctly enforced.
- **Decoder**: No test verifies that a server-sent `Transfer-Encoding: chunked` body is
  treated as raw bytes (HTTP/1.0 has no chunked encoding). The `304 Not Modified`
  no-body case is also untested.
- **RFC_TEST_MATRIX.md**: The `RFC 1945` section has now been added. This PRD drives
  test coverage to ≥ 90% of MUST requirements in that section.

---

## Goals

- Cover all MUST-level RFC 1945 encoder requirements in `Http10EncoderTests`
- Cover remaining RFC 1945 decoder gaps in `Http10DecoderTests`
- Every new test has an RFC section reference comment: `// RFC 1945 §X.X`
- `dotnet test` stays green throughout

---

## User Stories

---

### US-001: RFC 1945 §5 — Encoder: HTTP/1.0-specific header rules

**Description:** As a developer, I want `Http10EncoderTests` to verify that the encoder
produces a valid HTTP/1.0 request line and strips headers that are not valid in HTTP/1.0,
so that clients do not accidentally send HTTP/1.1-only fields to HTTP/1.0 servers.

**RFC reference:** RFC 1945 §5.1 (Request-Line), §5.2 (Request-Header fields).

**Key RFC 1945 rules for the encoder:**
- Request-line MUST be `Method SP Request-URI SP HTTP/1.0 CRLF`
- `Host` header: not defined in RFC 1945 — encoder must remove it
- `Transfer-Encoding`: not defined in RFC 1945 — encoder must remove it
- `Connection`: not defined in RFC 1945 — encoder must remove it
- `Content-Length` MUST be set when a body is present (RFC 1945 §10.4)

**Acceptance Criteria:**
- [ ] `// RFC 1945 §5.1 — Encode_Get_RequestLine_UsesHttp10` — `GET /path HTTP/1.0\r\n` is the first line
- [ ] `// RFC 1945 §5.1 — Encode_Get_PathAndQuery_Preserved` — `GET /search?q=hello HTTP/1.0\r\n`
- [ ] `// RFC 1945 §5.2 — Encode_HostHeader_NotEmitted` — request with `Host: example.com` set → `Host` absent in output
- [ ] `// RFC 1945 §5.2 — Encode_TransferEncoding_NotEmitted` — request with `Transfer-Encoding: chunked` → TE absent in output
- [ ] `// RFC 1945 §5.2 — Encode_ConnectionHeader_NotEmitted` — request with `Connection: keep-alive` → Connection absent in output
- [ ] `// RFC 1945 §10.4 — Encode_Post_SetsContentLength` — POST with 5-byte body → `Content-Length: 5` present in output
- [ ] `// RFC 1945 §10.4 — Encode_Get_NoContentLength` — GET without body → Content-Length absent in output
- [ ] `// RFC 1945 §5.1 — Encode_Post_BinaryBody_PreservedExactly` — POST with `byte[]{0x00,0x01,0xFF}` → body bytes match exactly
- [ ] `dotnet test` passes

---

### US-002: RFC 1945 §6 — Decoder: 304 Not Modified has no body

**Description:** As a developer, I want `Http10DecoderTests` to verify that a
`304 Not Modified` response is decoded with an empty body, consistent with RFC 1945 §9.3
(304 responses MUST NOT include a message-body).

**RFC reference:** RFC 1945 §9.3, §7.2.

**Acceptance Criteria:**
- [ ] `// RFC 1945 §9.3 — Decode_304_NoBody_EmptyContent` — `HTTP/1.0 304 Not Modified\r\n\r\n` → `response.Content` non-null, `Content.Headers.ContentLength == 0`
- [ ] `// RFC 1945 §9.3 — Decode_304_WithEtag_HeaderPresent` — `HTTP/1.0 304 Not Modified\r\nETag: "abc"\r\n\r\n` → ETag header accessible on response, body empty
- [ ] Existing 200/204 tests still pass
- [ ] `dotnet test` passes

---

### US-003: RFC 1945 §7 — Decoder: chunked body treated as raw bytes

**Description:** As a developer, I want `Http10DecoderTests` to verify that when an
HTTP/1.0 server incorrectly sends `Transfer-Encoding: chunked`, the decoder treats the
payload as raw bytes (not parsed as chunks), because HTTP/1.0 has no chunked encoding.

**RFC reference:** RFC 1945 §7.1 — only `Content-Length` and connection-close determine body length;
chunked transfer encoding is not defined in RFC 1945.

**Acceptance Criteria:**
- [ ] `// RFC 1945 §7.1 — Decode_ChunkedHeader_TreatedAsRawBody` — response with `Transfer-Encoding: chunked\r\n` and body `5\r\nHello\r\n0\r\n\r\n` → raw bytes returned, NOT parsed as chunks (body contains `5\r\nHello\r\n0\r\n\r\n` literally, NOT `Hello`)
- [ ] `dotnet test` passes

---

### US-004: RFC 1945 §8 — Decoder: all RFC 1945 defined status codes

**Description:** As a developer, I want `Http10DecoderTests` to explicitly cover every
status code defined in RFC 1945 §9 (the complete set for HTTP/1.0).

**RFC reference:** RFC 1945 §9 (Status Code Definitions): 200, 201, 202, 204, 301, 302,
304, 400, 401, 403, 404, 500, 501, 502, 503.

**Acceptance Criteria:**
- [ ] `// RFC 1945 §9 — Decode_AllRfc1945StatusCodes_ParsedCorrectly` — `[Theory]` with all 14 RFC 1945 defined codes → each parses without exception, `StatusCode` matches
- [ ] 200, 201, 202, 204 → 2xx success
- [ ] 301, 302, 304 → 3xx redirect/not-modified
- [ ] 400, 401, 403, 404 → 4xx client error
- [ ] 500, 501, 502, 503 → 5xx server error
- [ ] `dotnet test` passes

---

## Functional Requirements

- FR-1: `Http10EncoderTests` covers: request-line format, Host/TE/Connection stripping, Content-Length enforcement, binary body preservation (RFC 1945 §5, §10.4)
- FR-2: `Http10DecoderTests` covers: 304 no-body, chunked-as-raw, all 14 RFC 1945 status codes
- FR-3: Every new test method has an `// RFC 1945 §X.X — description` comment
- FR-4: Test method names follow pattern `Encode_<Scenario>_<Expected>` / `Decode_<Scenario>_<Expected>`
- FR-5: `RFC_TEST_MATRIX.md` §5 Encoder and §6 Decoder columns updated to `✅` after implementation

## Non-Goals

- Server-side request parsing (`bytes → HttpRequestMessage`) — client-only library
- Server-side response encoding (`HttpResponseMessage → bytes`) — client-only library
- HTTP/0.9 Simple-Request/Simple-Response compatibility — not needed
- Keep-Alive extension (RFC 1945 §19.7.1) connection management tests — no connection-level implementation in scope
- `WWW-Authenticate` / `Authorization` header semantics — pass-through headers only, no parsing

## Technical Considerations

- `Http10Encoder` is in `TurboHttp/Protocol/Http10Encoder.cs`
- `Http10Decoder` is in `TurboHttp/Protocol/Http10Decoder.cs`
- `Http10EncoderTests` is in `TurboHttp.Tests/Http10EncoderTests.cs`
- `Http10DecoderTests` is in `TurboHttp.Tests/Http10DecoderTests.cs`
- US-003 (chunked as raw): The decoder currently reads body by Content-Length or to EOF — confirm that no chunked parsing is triggered when `Transfer-Encoding: chunked` is present. If it is, the test will catch it.
- US-002 (304 no-body): HTTP/1.0 decoder currently does NOT have `IsNoBodyResponse` logic (unlike the HTTP/1.1 decoder). 304 responses are likely read until EOF or Content-Length. The test may reveal a bug.

## Success Metrics

- All new tests pass (`dotnet test` green)
- Zero regressions in existing `Http10DecoderTests` and `Http10EncoderTests`
- RFC 1945 MUST coverage in `RFC_TEST_MATRIX.md` reaches ≥ 90%

## Open Questions

- Does `Http10Decoder` currently handle `304 Not Modified` as no-body? The test in US-002 will reveal this.
- Should `Http10Encoder` throw `ArgumentException` when buffer is too small (like `Http11Encoder`), or `InvalidOperationException` (current behaviour)? Align if needed in a separate story.
