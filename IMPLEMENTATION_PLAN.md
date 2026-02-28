# Client — Production-Ready RFC Implementation Plan

> **Scope:** Client-side only.
> **Encoders:** `HttpRequestMessage → bytes` (client sends request)
> **Decoders:** `bytes → HttpResponseMessage` (client receives response)
> Server-side is out of scope.

---

## How to Use This Document

- Work top-to-bottom, phase by phase.
- Mark `[ ]` → `[x]` as test cases are implemented.
- RFC Test IDs appear in test `[DisplayName]` attributes.
- Priority: **P0** = release blocker, **P1** = production, **P2** = full compliance.
- `[T]` = implement as `[Theory]/[InlineData]` for multiple data rows.

> **Note on existing test files:** The implementation already has tests in
> `Http10EncoderTests.cs` (61), `Http10DecoderTests.cs` (52), `Http10RoundTripTests.cs` (7),
> `Http11EncoderTests.cs` (13), `Http11DecoderTests.cs` (34), `Http2EncoderTests.cs` (24),
> `Http2DecoderTests.cs` (16), `HpackTests.cs` (15), `HuffmanTests.cs` (16), `Http2FrameTests.cs` (6).
> The tasks below describe **gaps only** — do not re-implement what already exists.

---

## Phase 1: HTTP/1.0 (RFC 1945) — Client Encoder

**File:** `src/TurboHttp.Tests/Http10EncoderTests.cs`

### Request-Line (RFC 1945 §5.1)

- [x] `1945-enc-001` **P0** — Request-line starts with `METHOD path HTTP/1.0\r\n` · `[DisplayName("1945-enc-001: Request-line uses HTTP/1.0")]`
- [x] `1945-enc-007` **P0** — Path and query string preserved in request-line · `[DisplayName("1945-enc-007: Path-and-query preserved in request-line")]`
- [x] `1945-5.1-004` **P0** — Lowercase method causes exception · `[DisplayName("1945-5.1-004: Lowercase method rejected by HTTP/1.0 encoder")]`
- [x] `1945-5.1-005` **P1** — Absolute URI encoded in request-line · `[DisplayName("1945-5.1-005: Absolute URI encoded in request-line")]`
- [x] `enc1-m-001` **P0** `[T]` — All HTTP methods uppercase (GET/POST/PUT/DELETE/PATCH/HEAD/OPTIONS/TRACE × 8) · `[DisplayName("enc1-m-001: All HTTP methods produce correct uppercase request-line")]`
- [x] `enc1-uri-001` **P0** — Missing path normalized to `/` · `[DisplayName("enc1-uri-001: Missing path normalized to /")]`
- [x] `enc1-uri-002` **P0** — Query string preserved verbatim · `[DisplayName("enc1-uri-002: Query string preserved in request-target")]`
- [x] `enc1-uri-003` **P1** — Percent-encoded chars not double-encoded · `[DisplayName("enc1-uri-003: Percent-encoded chars not double-encoded")]`
- [x] `enc1-uri-004` **P1** — URI fragment stripped · `[DisplayName("enc1-uri-004: URI fragment stripped from request-target")]`

### Header Suppression

- [x] `1945-enc-002` **P0** — `Host:` absent in HTTP/1.0 request · `[DisplayName("1945-enc-002: Host header absent in HTTP/1.0 request")]`
- [x] `1945-enc-003` **P0** — `Transfer-Encoding:` absent · `[DisplayName("1945-enc-003: Transfer-Encoding absent in HTTP/1.0 request")]`
- [x] `1945-enc-004` **P0** — `Connection:` absent · `[DisplayName("1945-enc-004: Connection header absent in HTTP/1.0 request")]`
- [x] `enc1-hdr-001` **P0** — Every header line ends with `\r\n`, no bare `\n` · `[DisplayName("enc1-hdr-001: Every header line terminated with CRLF")]`
- [x] `enc1-hdr-002` **P1** — Custom header name casing preserved · `[DisplayName("enc1-hdr-002: Custom header name casing preserved")]`
- [x] `enc1-hdr-003` **P1** — Multiple custom headers all emitted · `[DisplayName("enc1-hdr-003: Multiple custom headers all emitted")]`
- [x] `enc1-hdr-004` **P1** — Semicolon in header value preserved verbatim · `[DisplayName("enc1-hdr-004: Semicolon in header value preserved verbatim")]`
- [x] `enc1-hdr-005` **P0** — NUL byte in header value throws `ArgumentException` · `[DisplayName("enc1-hdr-005: NUL byte in header value throws exception")]`

### Body Encoding

- [x] `1945-enc-005` **P0** — `Content-Length` set for POST body · `[DisplayName("1945-enc-005: Content-Length present for POST body")]`
- [x] `1945-enc-006` **P0** — `Content-Length` absent for bodyless GET · `[DisplayName("1945-enc-006: Content-Length absent for bodyless GET")]`
- [x] `1945-enc-008` **P0** — Binary body bytes match input exactly · `[DisplayName("1945-enc-008: Binary POST body encoded verbatim")]`
- [x] `1945-enc-009` **P0** — UTF-8 JSON body encoded correctly · `[DisplayName("1945-enc-009: UTF-8 JSON body encoded correctly")]`
- [x] `enc1-body-001` **P0** — Body with null bytes not truncated · `[DisplayName("enc1-body-001: Body with null bytes not truncated")]`
- [x] `enc1-body-002` **P1** — 2 MB body with correct `Content-Length` · `[DisplayName("enc1-body-002: 2 MB body encoded with correct Content-Length")]`
- [x] `enc1-body-003` **P0** — `\r\n\r\n` separates headers from body exactly · `[DisplayName("enc1-body-003: CRLFCRLF separates headers from body")]`

---

## Phase 2: HTTP/1.0 (RFC 1945) — Client Decoder

**File:** `src/TurboHttp.Tests/Http10DecoderTests.cs`

### Status-Line (RFC 1945 §6)

- [x] `1945-dec-003a` **P0** `[T]` — Each of 15 RFC 1945 status codes (200,201,202,204,301,302,304,400,401,403,404,500,501,502,503) · `[DisplayName("1945-dec-003: RFC 1945 status code {code} parsed")]`
- [x] `dec1-sl-001` **P0** — Unknown status code 299 accepted · `[DisplayName("dec1-sl-001: Unknown status code 299 accepted")]`
- [x] `dec1-sl-002` **P0** — Status code 99 rejected · `[DisplayName("dec1-sl-002: Status code 99 rejected")]`
- [x] `dec1-sl-003` **P0** — Status code 1000 rejected · `[DisplayName("dec1-sl-003: Status code 1000 rejected")]`
- [x] `dec1-sl-004` **P0** — LF-only line endings accepted (HTTP/1.0 permissive) · `[DisplayName("dec1-sl-004: LF-only line endings accepted in HTTP/1.0")]`
- [x] `dec1-sl-005` **P0** — Empty reason phrase accepted · `[DisplayName("dec1-sl-005: Empty reason phrase after status code accepted")]`

### Header Parsing (RFC 1945 §4)

- [x] `1945-4-002` **P0** — Obs-fold continuation accepted and merged · `[DisplayName("1945-4-002: Obs-fold continuation accepted in HTTP/1.0")]`
- [x] `1945-4-002b` **P1** — Double obs-fold merged into single value · `[DisplayName("1945-4-002b: Double obs-fold line merged into single value")]`
- [x] `1945-4-003` **P0** — Duplicate response headers both accessible · `[DisplayName("1945-4-003: Duplicate response headers both accessible")]`
- [x] `1945-4-004` **P0** — Header without colon → `InvalidHeader` error · `[DisplayName("1945-4-004: Header without colon causes parse error")]`
- [x] `1945-4-005` **P0** — Header name lookup case-insensitive · `[DisplayName("1945-4-005: CONTENT-LENGTH and Content-Length are equivalent")]`
- [x] `1945-4-006` **P0** — Leading/trailing whitespace trimmed from value · `[DisplayName("1945-4-006: Header value whitespace trimmed")]`
- [x] `1945-4-007` **P0** — Space in header name → `InvalidFieldName` · `[DisplayName("1945-4-007: Space in header name causes parse error")]`
- [x] `dec1-hdr-001` **P1** — Tab in header value accepted · `[DisplayName("dec1-hdr-001: Tab character in header value accepted")]`
- [x] `dec1-hdr-002` **P0** — Response with zero headers accepted · `[DisplayName("dec1-hdr-002: Response with no headers except status-line accepted")]`

### No-Body Responses

- [x] `1945-dec-004` **P0** — 304 Not Modified with Content-Length: body empty · `[DisplayName("1945-dec-004: 304 Not Modified ignores Content-Length body")]`
- [x] `1945-dec-004b` **P0** — 304 Not Modified without Content-Length: body empty · `[DisplayName("1945-dec-004b: 304 Not Modified without Content-Length has empty body")]`
- [x] `dec1-nb-001` **P0** — 204 No Content: body length = 0 · `[DisplayName("dec1-nb-001: 204 No Content has empty body")]`

### Body Parsing (RFC 1945 §7)

- [x] `1945-7-001` **P0** — Content-Length body decoded to exact byte count · `[DisplayName("1945-7-001: Content-Length body decoded to exact byte count")]`
- [x] `1945-7-002` **P0** — Zero Content-Length → empty body · `[DisplayName("1945-7-002: Zero Content-Length produces empty body")]`
- [x] `1945-7-003` **P0** — No Content-Length → read until EOF via `TryDecodeEof` · `[DisplayName("1945-7-003: Body without Content-Length read via TryDecodeEof")]`
- [x] `1945-7-005` **P0** — Two different Content-Length values → error · `[DisplayName("1945-7-005: Two different Content-Length values rejected")]`
- [x] `1945-7-005b` **P1** — Two identical Content-Length values accepted · `[DisplayName("1945-7-005b: Two identical Content-Length values accepted")]`
- [x] `1945-7-006` **P0** — Negative Content-Length → `InvalidContentLength` · `[DisplayName("1945-7-006: Negative Content-Length is parse error")]`
- [x] `dec1-body-001` **P0** — Binary body with null bytes decoded intact · `[DisplayName("dec1-body-001: Body with null bytes decoded intact")]`
- [x] `dec1-body-002` **P1** — 2 MB body decoded correctly · `[DisplayName("dec1-body-002: 2 MB body decoded with correct Content-Length")]`
- [x] `1945-dec-006` **P1** — Chunked transfer treated as raw bytes · `[DisplayName("1945-dec-006: Transfer-Encoding chunked is raw body in HTTP/1.0")]`

### Connection Semantics (RFC 1945 §8)

- [x] `1945-8-001` **P0** — Default connection is close · `[DisplayName("1945-8-001: HTTP/1.0 default connection is close")]`
- [x] `1945-8-002` **P1** — `Connection: keep-alive` recognized · `[DisplayName("1945-8-002: Connection: keep-alive recognized in HTTP/1.0")]`
- [x] `1945-8-003` **P1** — Keep-Alive timeout/max parameters parsed · `[DisplayName("1945-8-003: Keep-Alive timeout and max parameters parsed")]`
- [x] `1945-8-004` **P0** — HTTP/1.0 does not default to keep-alive · `[DisplayName("1945-8-004: HTTP/1.0 does not default to keep-alive")]`
- [x] `1945-8-005` **P0** — `Connection: close` sets close flag · `[DisplayName("1945-8-005: Connection: close signals close after response")]`

### TCP Fragmentation

- [x] `dec1-frag-001` **P0** — Status-line split at byte 1 reassembled · `[DisplayName("dec1-frag-001: Status-line split at byte 1 reassembled")]`
- [x] `dec1-frag-002` **P0** — Status-line split inside version reassembled · `[DisplayName("dec1-frag-002: Status-line split inside HTTP/1.0 version reassembled")]`
- [x] `dec1-frag-003` **P0** — Header name split across two reads · `[DisplayName("dec1-frag-003: Header name split across two reads")]`
- [x] `dec1-frag-004` **P0** — Header value split across two reads · `[DisplayName("dec1-frag-004: Header value split across two reads")]`
- [x] `dec1-frag-005` **P0** — Body split mid-content reassembled · `[DisplayName("dec1-frag-005: Body split mid-content reassembled")]`

---

## Phase 3: HTTP/1.1 (RFC 9112 / RFC 7230) — Client Encoder

**File:** `src/TurboHttp.Tests/Http11EncoderTests.cs`

### Request-Line (RFC 7230 §3.1.1)

- [x] `7230-enc-001` **P0** — Request-line uses `HTTP/1.1` · `[DisplayName("7230-enc-001: Request-line uses HTTP/1.1")]`
- [x] `7230-3.1.1-002` **P0** — Lowercase method causes exception · `[DisplayName("7230-3.1.1-002: Lowercase method rejected by HTTP/1.1 encoder")]`
- [x] `7230-3.1.1-004` **P0** — Every request-line ends with `\r\n` · `[DisplayName("7230-3.1.1-004: Every request-line ends with CRLF")]`
- [x] `enc3-m-001` **P0** `[T]` — All 9 HTTP methods (GET/POST/PUT/DELETE/PATCH/HEAD/OPTIONS/TRACE/CONNECT) · `[DisplayName("enc3-m-001: All HTTP methods produce correct request-line [{method}]")]`
- [x] `enc3-uri-001` **P0** — `OPTIONS * HTTP/1.1` encoded correctly · `[DisplayName("enc3-uri-001: OPTIONS * HTTP/1.1 encoded correctly")]`
- [x] `enc3-uri-002` **P0** — Absolute-URI preserved for proxy request · `[DisplayName("enc3-uri-002: Absolute-URI preserved for proxy request")]`
- [x] `enc3-uri-003` **P0** — Missing path normalized to `/` · `[DisplayName("enc3-uri-003: Missing path normalized to /")]`
- [x] `enc3-uri-004` **P0** — Query string preserved verbatim · `[DisplayName("enc3-uri-004: Query string preserved verbatim")]`
- [x] `enc3-uri-005` **P0** — Fragment stripped from request-target · `[DisplayName("enc3-uri-005: Fragment stripped from request-target")]`
- [x] `enc3-uri-006` **P1** — Existing percent-encoding not re-encoded · `[DisplayName("enc3-uri-006: Existing percent-encoding not re-encoded")]`

### Mandatory Host Header

- [x] `9112-enc-001` **P0** — `Host:` always present · `[DisplayName("RFC 9112 §5.4: Host header mandatory in HTTP/1.1")]`
- [x] `9112-enc-002` **P0** — `Host:` emitted exactly once · `[DisplayName("RFC 9112 §5.4: Host header emitted exactly once")]`
- [x] `enc3-host-001` **P0** — Non-standard port included in `Host` value · `[DisplayName("enc3-host-001: Host with non-standard port includes port")]`
- [x] `enc3-host-002` **P0** — IPv6 host literal bracketed: `Host: [::1]` · `[DisplayName("enc3-host-002: IPv6 host literal bracketed correctly")]`
- [x] `enc3-host-003` **P0** — Default port 80 omitted from `Host` header · `[DisplayName("enc3-host-003: Default port 80 omitted from Host header")]`

### Header Encoding (RFC 7230 §3.2)

- [x] `7230-3.2-001` **P0** — Header format is `Name: SP value CRLF` · `[DisplayName("7230-3.2-001: Header field format is Name: SP value CRLF")]`
- [x] `7230-3.2-002` **P0** — No spurious whitespace added to header values · `[DisplayName("7230-3.2-002: No spurious whitespace added to header values")]`
- [x] `7230-3.2-007` **P0** — Header name casing preserved (not lowercased) · `[DisplayName("7230-3.2-007: Header name casing preserved in output")]`
- [x] `enc3-hdr-001` **P0** — NUL byte in header value throws `ArgumentException` · `[DisplayName("enc3-hdr-001: NUL byte in header value throws exception")]`
- [x] `enc3-hdr-002` **P1** — `Content-Type` with semicolon parameters preserved · `[DisplayName("enc3-hdr-002: Content-Type with charset parameter preserved")]`
- [x] `enc3-hdr-003` **P1** — All custom headers appear in output · `[DisplayName("enc3-hdr-003: All custom headers appear in output")]`
- [x] `enc3-hdr-004` **P1** — `Accept-Encoding: gzip, deflate` encoded · `[DisplayName("enc3-hdr-004: Accept-Encoding gzip,deflate encoded")]`
- [x] `enc3-hdr-005` **P1** — `Authorization: Bearer …` preserved verbatim · `[DisplayName("enc3-hdr-005: Authorization header preserved verbatim")]`

### Connection Management

- [x] `7230-enc-003` **P0** — `Connection: keep-alive` default · `[DisplayName("7230-enc-003: Connection keep-alive default in HTTP/1.1")]`
- [x] `7230-enc-004` **P0** — `Connection: close` when explicitly set · `[DisplayName("7230-enc-004: Connection close encoded when set")]`
- [x] `7230-6.1-005` **P1** — Multiple `Connection` tokens encoded · `[DisplayName("7230-6.1-005: Multiple Connection tokens encoded")]`
- [x] `9112-enc-003` **P0** — `TE`, `Trailers`, `Keep-Alive` stripped · `[DisplayName("RFC 9112: Connection-specific headers stripped")]`

### Body Encoding

- [x] `7230-enc-006` **P0** — No `Content-Length` for bodyless GET · `[DisplayName("7230-enc-006: No Content-Length for bodyless GET")]`
- [x] `7230-enc-008` **P0** — `Content-Length` set for POST body · `[DisplayName("7230-enc-008: Content-Length set for POST body")]`
- [x] `7230-enc-009` **P1** — `Transfer-Encoding: chunked` + correct chunks · `[DisplayName("7230-enc-009: Chunked Transfer-Encoding for streamed body")]`
- [x] `enc3-body-001` **P0** `[T]` — POST/PUT/PATCH each get `Content-Length` (× 3) · `[DisplayName("enc3-body-001: {method} with body gets Content-Length [{method}]")]`
- [x] `enc3-body-002` **P0** `[T]` — GET/HEAD/DELETE omit `Content-Length` (× 3) · `[DisplayName("enc3-body-002: {method} without body omits Content-Length [{method}]")]`
- [x] `enc3-body-003` **P0** — `\r\n\r\n` separates headers from body · `[DisplayName("enc3-body-003: Empty line separates headers from body")]`
- [x] `enc3-body-004` **P0** — Binary body with null bytes preserved · `[DisplayName("enc3-body-004: Binary body with null bytes preserved")]`
- [x] `enc3-body-005` **P1** — Chunked body ends with `0\r\n\r\n` · `[DisplayName("enc3-body-005: Chunked body terminated with final 0-chunk")]`
- [x] `enc3-body-006` **P0** — No `Content-Length` when chunked · `[DisplayName("enc3-body-006: Content-Length absent when Transfer-Encoding is chunked")]`

---

## Phase 4: HTTP/1.1 (RFC 9112 / RFC 7230) — Client Decoder

**File:** `src/TurboHttp.Tests/Http11DecoderTests.cs`

### Status-Line (RFC 7231 §6.1)

- [x] `7231-6.1-002a` **P0** `[T]` — All 2xx codes (200,201,202,203,204,205,206,207 × 8) · `[DisplayName("7231-6.1-002: 2xx status code {code} parsed correctly")]`
- [x] `7231-6.1-003a` **P0** `[T]` — All 3xx codes (300,301,302,303,304,307,308 × 7) · `[DisplayName("7231-6.1-003: 3xx status code {code} parsed correctly")]`
- [x] `7231-6.1-004a` **P0** `[T]` — All 4xx codes (400,401,403,404,405,408,409,410,413,415,422,429 × 12) · `[DisplayName("7231-6.1-004: 4xx status code {code} parsed correctly")]`
- [x] `7231-6.1-005a` **P0** `[T]` — All 5xx codes (500,501,502,503,504 × 5) · `[DisplayName("7231-6.1-005: 5xx status code {code} parsed correctly")]`
- [x] `7231-6.1-001` **P0** — 1xx informational response has no body · `[DisplayName("7231-6.1-001: 1xx Informational response has no body")]`
- [x] `dec4-1xx-001` **P0** `[T]` — All 1xx codes individually (100,101,102,103 × 4) · `[DisplayName("dec4-1xx-001: 1xx code {code} parsed with no body")]`
- [x] `dec4-1xx-002` **P0** — 100 Continue before 200 OK decoded correctly · `[DisplayName("dec4-1xx-002: 100 Continue before 200 OK decoded correctly")]`
- [x] `dec4-1xx-003` **P0** — Multiple 1xx interim responses then 200 · `[DisplayName("dec4-1xx-003: Multiple 1xx interim responses before 200")]`
- [x] `7231-6.1-006` **P1** — Custom status code 599 parsed · `[DisplayName("7231-6.1-006: Custom status code 599 parsed")]`
- [x] `7231-6.1-007` **P0** — Status > 599 rejected · `[DisplayName("7231-6.1-007: Status code >599 is a parse error")]`
- [x] `7231-6.1-008` **P0** — Empty reason phrase valid · `[DisplayName("7231-6.1-008: Empty reason phrase is valid")]`

### Header Parsing (RFC 7230 §3.2)

- [x] `7230-3.2-001` **P0** — Standard `Name: value\r\n` parsed · `[DisplayName("7230-3.2-001: Standard header field Name: value parsed")]`
- [x] `7230-3.2-002` **P0** — OWS trimmed from header value · `[DisplayName("7230-3.2-002: OWS trimmed from header value")]`
- [x] `7230-3.2-003` **P0** — Empty header value accepted · `[DisplayName("7230-3.2-003: Empty header value accepted")]`
- [x] `7230-3.2-004` **P0** — Multiple same-name headers both accessible · `[DisplayName("7230-3.2-004: Multiple same-name headers both accessible")]`
- [x] `7230-3.2-005` **P0** — Obs-fold rejected in HTTP/1.1 → `ObsoleteFoldingDetected` · `[DisplayName("7230-3.2-005: Obs-fold rejected in HTTP/1.1")]`
- [x] `7230-3.2-006` **P0** — Header without colon → `InvalidHeader` · `[DisplayName("7230-3.2-006: Header without colon is parse error")]`
- [x] `7230-3.2-007` **P0** — Case-insensitive header name lookup · `[DisplayName("7230-3.2-007: Header name lookup case-insensitive")]`
- [x] `7230-3.2-008` **P0** — Space in header name → `InvalidFieldName` · `[DisplayName("7230-3.2-008: Space in header name is parse error")]`
- [x] `dec4-hdr-001` **P1** — Tab in header value accepted · `[DisplayName("dec4-hdr-001: Tab character in header value accepted")]`
- [x] `dec4-hdr-002` **P0** — Quoted-string header value parsed · `[DisplayName("dec4-hdr-002: Quoted-string header value parsed")]`
- [x] `dec4-hdr-003` **P0** — `Content-Type` with parameters parsed · `[DisplayName("dec4-hdr-003: Content-Type: text/html; charset=utf-8 accessible")]`

### Message Body (RFC 7230 §3.3)

- [x] `7230-3.3-001` **P0** — Content-Length body decoded to exact byte count · `[DisplayName("7230-3.3-001: Content-Length body decoded to exact byte count")]`
- [x] `7230-3.3-002` **P0** — Zero Content-Length → empty body · `[DisplayName("7230-3.3-002: Zero Content-Length produces empty body")]`
- [x] `7230-3.3-003` **P0** — Chunked response body decoded · `[DisplayName("7230-3.3-003: Chunked response body decoded correctly")]`
- [x] `7230-3.3-004` **P0** — Transfer-Encoding chunked takes priority over CL · `[DisplayName("7230-3.3-004: Transfer-Encoding chunked takes priority over CL")]`
- [x] `7230-3.3-005` **P0** — Multiple Content-Length values rejected · `[DisplayName("7230-3.3-005: Multiple Content-Length values rejected")]`
- [x] `7230-3.3-006` **P0** — Negative Content-Length rejected · `[DisplayName("7230-3.3-006: Negative Content-Length is parse error")]`
- [x] `7230-3.3-007` **P0** — Response without body framing has empty body (204/304) · `[DisplayName("7230-3.3-007: Response without body framing has empty body")]`
- [x] `dec4-body-001` **P0** — 10 MB body decoded correctly · `[DisplayName("dec4-body-001: 10 MB body decoded with correct Content-Length")]`
- [x] `dec4-body-002` **P0** — Binary body with null bytes intact · `[DisplayName("dec4-body-002: Binary body with null bytes intact")]`

### Chunked Transfer Encoding (RFC 7230 §4.1)

- [x] `7230-4.1-001` **P0** — Single chunk decoded: `5\r\nHello\r\n0\r\n\r\n` → `Hello` · `[DisplayName("7230-4.1-001: Single chunk body decoded")]`
- [x] `7230-4.1-002` **P0** — Multiple chunks concatenated correctly · `[DisplayName("7230-4.1-002: Multiple chunks concatenated")]`
- [x] `7230-4.1-003` **P1** — Chunk extensions silently ignored · `[DisplayName("7230-4.1-003: Chunk extension silently ignored")]`
- [x] `7230-4.1-004` **P1** — Trailer fields after final chunk accessible · `[DisplayName("7230-4.1-004: Trailer fields after final chunk")]`
- [x] `7230-4.1-005` **P0** — Non-hex chunk size → `InvalidChunkedEncoding` · `[DisplayName("7230-4.1-005: Non-hex chunk size is parse error")]`
- [x] `7230-4.1-006` **P0** — Missing final chunk → `NeedMoreData` / `Incomplete()` · `[DisplayName("7230-4.1-006: Missing final chunk is NeedMoreData")]`
- [x] `7230-4.1-007` **P0** — `0\r\n\r\n` terminates chunked body · `[DisplayName("7230-4.1-007: 0\\r\\n\\r\\n terminates chunked body")]`
- [x] `7230-4.1-008` **P0** — Chunk size overflow → parse error · `[DisplayName("7230-4.1-008: Chunk size overflow is parse error")]`
- [x] `dec4-chk-001` **P0** — 1-byte chunk decoded: `1\r\nX\r\n0\r\n\r\n` → `X` · `[DisplayName("dec4-chk-001: 1-byte chunk decoded")]`
- [x] `dec4-chk-002` **P0** — Uppercase hex chunk size accepted · `[DisplayName("dec4-chk-002: Uppercase hex chunk size accepted")]`
- [x] `dec4-chk-003` **P1** — Empty chunk before terminator accepted · `[DisplayName("dec4-chk-003: Empty chunk (0 data bytes) before terminator accepted")]`

### No-Body Responses

- [x] `7230-nb-001` **P0** — 204 No Content: empty body · `[DisplayName("RFC 7230: 204 No Content has empty body")]`
- [x] `7230-nb-002` **P0** — 304 Not Modified: empty body · `[DisplayName("RFC 7230: 304 Not Modified has empty body")]`
- [x] `dec4-nb-001` **P0** `[T]` — 204/205/304 always empty body (× 3) · `[DisplayName("dec4-nb-001: Status {code} always has empty body")]`
- [x] `dec4-nb-002` **P0** — HEAD response: `Content-Length` present, no body bytes · `[DisplayName("dec4-nb-002: HEAD response has Content-Length header but empty body")]`

### Connection Semantics (RFC 7230 §6.1)

- [x] `7230-6.1-001` **P0** — `Connection: close` signals connection close · `[DisplayName("7230-6.1-001: Connection: close signals connection close")]`
- [x] `7230-6.1-002` **P1** — `Connection: keep-alive` signals reuse · `[DisplayName("7230-6.1-002: Connection: keep-alive signals reuse")]`
- [x] `7230-6.1-003` **P0** — HTTP/1.1 default connection is keep-alive · `[DisplayName("7230-6.1-003: HTTP/1.1 default connection is keep-alive")]`
- [x] `7230-6.1-004` **P0** — HTTP/1.0 default connection is close · `[DisplayName("7230-6.1-004: HTTP/1.0 connection defaults to close")]`
- [x] `7230-6.1-005` **P1** — Multiple `Connection` tokens all recognized · `[DisplayName("7230-6.1-005: Multiple Connection tokens all recognized")]`

### Date/Time Parsing (RFC 7231 §7.1.1.1)

- [x] `7231-7.1.1-001` **P1** — IMF-fixdate parsed to `DateTimeOffset` · `[DisplayName("7231-7.1.1-001: IMF-fixdate Date header parsed")]`
- [x] `7231-7.1.1-002` **P1** — RFC 850 obsolete format accepted · `[DisplayName("7231-7.1.1-002: RFC 850 Date format accepted")]`
- [x] `7231-7.1.1-003` **P1** — ANSI C asctime format accepted · `[DisplayName("7231-7.1.1-003: ANSI C asctime Date format accepted")]`
- [x] `7231-7.1.1-004` **P1** — Non-GMT timezone rejected or ignored · `[DisplayName("7231-7.1.1-004: Non-GMT timezone in Date rejected")]`
- [x] `7231-7.1.1-005` **P1** — Invalid Date value handled gracefully · `[DisplayName("7231-7.1.1-005: Invalid Date header value rejected")]`

### Pipelining

- [x] `7230-pipe-001` **P1** — Two pipelined responses decoded · `[DisplayName("RFC 7230: Two pipelined responses decoded")]`
- [x] `7230-pipe-002` **P1** — Partial second response buffered as remainder · `[DisplayName("RFC 7230: Partial second response held in remainder")]`
- [x] `dec4-pipe-001` **P1** — Three pipelined responses decoded in order · `[DisplayName("dec4-pipe-001: Three pipelined responses decoded in order")]`

### TCP Fragmentation (HTTP/1.1)

- [x] `dec4-frag-001` **P0** — Status-line split at byte 1 reassembled · `[DisplayName("dec4-frag-001: Status-line split byte 1 reassembled")]`
- [x] `dec4-frag-002` **P0** — Status-line split inside `HTTP/1.1` version · `[DisplayName("dec4-frag-002: Status-line split inside HTTP/1.1 version")]`
- [x] `dec4-frag-003` **P0** — Header split at colon · `[DisplayName("dec4-frag-003: Header name:value split at colon")]`
- [x] `dec4-frag-004` **P0** — Split at `\r\n\r\n` header-body boundary · `[DisplayName("dec4-frag-004: Split at CRLFCRLF header-body boundary")]`
- [x] `dec4-frag-005` **P0** — Chunk-size line split across two reads · `[DisplayName("dec4-frag-005: Chunk-size line split across two reads")]`
- [x] `dec4-frag-006` **P0** — Chunk data split mid-content · `[DisplayName("dec4-frag-006: Chunk data split mid-content")]`
- [x] `dec4-frag-007` **P0** — Response delivered 1 byte at a time assembles correctly · `[DisplayName("dec4-frag-007: Response delivered 1 byte at a time assembles correctly")]`

---

## Phase 4b: Range Requests (RFC 7233) — Encoder + Decoder

**Files:** `Http11EncoderTests.cs`, `Http11DecoderTests.cs`

### Range Header Encoding (RFC 7233 §2.1)

- [x] `7233-2.1-001` **P2** — `Range: bytes=0-499` encoded · `[DisplayName("7233-2.1-001: Range: bytes=0-499 encoded")]`
- [x] `7233-2.1-002` **P2** — Suffix range `bytes=-500` encoded · `[DisplayName("7233-2.1-002: Range: bytes=-500 suffix encoded")]`
- [x] `7233-2.1-003` **P2** — Open-ended range `bytes=500-` encoded · `[DisplayName("7233-2.1-003: Range: bytes=500- open range encoded")]`
- [x] `7233-2.1-004` **P2** — Multi-range comma-separated encoded · `[DisplayName("7233-2.1-004: Multi-range bytes=0-499,1000-1499 encoded")]`
- [x] `7233-2.1-005` **P2** — Invalid range rejected · `[DisplayName("7233-2.1-005: Invalid range bytes=abc-xyz rejected")]`

### 206 Partial Content Decoding (RFC 7233 §4.1)

- [x] `7233-4.1-001` **P2** — `Content-Range: bytes 0-499/1000` parsed · `[DisplayName("7233-4.1-001: Content-Range: bytes 0-499/1000 accessible")]`
- [x] `7233-4.1-002` **P2** — 206 Partial Content with Content-Range decoded · `[DisplayName("7233-4.1-002: 206 Partial Content with Content-Range decoded")]`
- [x] `7233-4.1-003` **P2** — `multipart/byteranges` body decoded · `[DisplayName("7233-4.1-003: 206 multipart/byteranges body decoded")]`
- [x] `7233-4.1-004` **P2** — Unknown total length (`*`) accepted · `[DisplayName("7233-4.1-004: Content-Range: bytes 0-499/* unknown total")]`

---

## Phase 5: HTTP/2 (RFC 7540) — Client Encoder

**File:** `src/TurboHttp.Tests/Http2EncoderTests.cs`

### Connection Preface (RFC 7540 §3.5)

- [x] `7540-3.5-001` **P0** — Client preface bytes = `PRI * HTTP/2.0\r\n\r\nSM\r\n\r\n` · `[DisplayName("7540-3.5-001: Client preface is PRI * HTTP/2.0 SM")]`
- [x] `7540-3.5-003` **P0** — SETTINGS frame immediately follows client preface · `[DisplayName("7540-3.5-003: SETTINGS frame immediately follows client preface")]`

### Pseudo-Headers (RFC 7540 §8.1.2)

- [x] `7540-8.1-001` **P0** — All four pseudo-headers emitted (`:method` `:scheme` `:authority` `:path`) · `[DisplayName("7540-8.1-001: All four pseudo-headers emitted")]`
- [x] `7540-8.1-002` **P0** — Pseudo-headers precede all regular headers · `[DisplayName("7540-8.1-002: Pseudo-headers precede regular headers")]`
- [x] `7540-8.1-003` **P0** — No duplicate pseudo-headers · `[DisplayName("7540-8.1-003: No duplicate pseudo-headers")]`
- [x] `7540-8.1-004` **P0** — `Connection`, `Keep-Alive`, `Upgrade` absent in HTTP/2 · `[DisplayName("7540-8.1-004: Connection-specific headers absent in HTTP/2")]`
- [x] `enc5-ph-001` **P0** `[T]` — `:method` correct for all HTTP methods (× 9) · `[DisplayName("enc5-ph-001: :method pseudo-header correct for [{method}]")]`
- [x] `enc5-ph-002` **P0** — `:scheme http` and `:scheme https` reflect URI scheme · `[DisplayName("enc5-ph-002: :scheme reflects request URI scheme")]`

### SETTINGS Frame (RFC 7540 §6.5)

- [x] `enc5-set-001` **P0** `[T]` — All 6 SETTINGS parameters encoded correctly (× 6) · `[DisplayName("enc5-set-001: SETTINGS parameter {param} encoded correctly")]`
- [x] `enc5-set-002` **P0** — SETTINGS ACK has type=`0x04` flags=`0x01` stream=0 · `[DisplayName("enc5-set-002: SETTINGS ACK frame has type=0x04 flags=0x01 stream=0")]`

### Stream IDs (RFC 7540 §5.1)

- [x] `7540-5.1-001` **P0** — First request uses stream ID 1 · `[DisplayName("7540-5.1-001: First request uses stream ID 1")]`
- [x] `7540-5.1-002` **P0** — Stream IDs increment as 1, 3, 5, … · `[DisplayName("7540-5.1-002: Stream IDs increment (1,3,5,...)")]`
- [x] `enc5-sid-001` **P0** — Client never produces even stream IDs · `[DisplayName("enc5-sid-001: Client never produces even stream IDs")]`
- [x] `enc5-sid-002` **P1** — Stream ID near 2^31 handled gracefully · `[DisplayName("enc5-sid-002: Stream ID approaching 2^31 handled gracefully")]`

### HEADERS Frame (RFC 7540 §6.2)

- [x] `7540-6.2-001` **P0** — HEADERS frame has correct 9-byte header, type `0x01` · `[DisplayName("7540-6.2-001: HEADERS frame has correct 9-byte header and payload")]`
- [x] `7540-6.2-002` **P0** — END_STREAM set on HEADERS for GET (bodyless) · `[DisplayName("7540-6.2-002: END_STREAM flag set on HEADERS for GET")]`
- [x] `7540-6.2-003` **P0** — END_HEADERS set on single HEADERS frame · `[DisplayName("7540-6.2-003: END_HEADERS flag set on single HEADERS frame")]`

### CONTINUATION Frames (RFC 7540 §6.9)

- [x] `7540-6.9-001` **P0** — Headers exceeding max frame size split into CONTINUATION · `[DisplayName("7540-6.9-001: Headers exceeding max frame size split into CONTINUATION")]`
- [x] `7540-6.9-002` **P0** — END_HEADERS on final CONTINUATION frame · `[DisplayName("7540-6.9-002: END_HEADERS on final CONTINUATION frame")]`
- [x] `7540-6.9-003` **P1** — Multiple CONTINUATION frames for very large headers · `[DisplayName("7540-6.9-003: Multiple CONTINUATION frames for very large headers")]`

### DATA Frames (RFC 7540 §6.1)

- [x] `7540-6.1-002enc` **P0** — END_STREAM set on final DATA frame · `[DisplayName("7540-6.1-enc-002: END_STREAM set on final DATA frame")]`
- [x] `7540-6.1-003enc` **P0** — GET uses END_STREAM on HEADERS, no DATA · `[DisplayName("7540-6.1-enc-003: GET END_STREAM on HEADERS frame")]`
- [x] `enc5-data-001` **P0** — DATA frame type byte = `0x00` · `[DisplayName("enc5-data-001: DATA frame has type byte 0x00")]`
- [x] `enc5-data-002` **P0** — DATA frame carries correct stream ID · `[DisplayName("enc5-data-002: DATA frame carries correct stream ID")]`
- [x] `enc5-data-003` **P1** — Body > MAX_FRAME_SIZE split into multiple DATA frames · `[DisplayName("enc5-data-003: Body exceeding MAX_FRAME_SIZE split into multiple DATA frames")]`

### Flow Control — Encoder Side (RFC 7540 §5.2)

- [x] `7540-5.2-001enc` **P0** — Encoder does not exceed initial 65535-byte window · `[DisplayName("7540-5.2-enc-001: Encoder does not exceed initial 65535-byte window")]`
- [x] `7540-5.2-002enc` **P0** — WINDOW_UPDATE allows more DATA to be sent · `[DisplayName("7540-5.2-enc-002: WINDOW_UPDATE allows more DATA to be sent")]`
- [x] `7540-5.2-005enc` **P0** — Encoder blocks when window is zero · `[DisplayName("7540-5.2-enc-005: Encoder blocks when window is zero")]`
- [x] `7540-5.2-006enc` **P0** — Connection-level window limits total DATA · `[DisplayName("7540-5.2-enc-006: Connection-level window limits total DATA")]`
- [x] `7540-5.2-007enc` **P0** — Per-stream window limits DATA on that stream · `[DisplayName("7540-5.2-enc-007: Per-stream window limits DATA on that stream")]`

---

## Phase 6: HTTP/2 (RFC 7540) — Client Decoder

**File:** `src/TurboHttp.Tests/Http2DecoderTests.cs`

### Connection Preface (RFC 7540 §3.5)

- [x] `7540-3.5-002` **P0** — Invalid server preface → `PROTOCOL_ERROR` · `[DisplayName("7540-3.5-002: Invalid server preface causes PROTOCOL_ERROR")]`
- [x] `7540-3.5-004` **P0** — Missing SETTINGS after preface → error · `[DisplayName("7540-3.5-004: Missing SETTINGS after preface causes error")]`

### Frame Header (RFC 7540 §4.1)

- [x] `7540-4.1-001` **P0** — Valid 9-byte frame header decoded correctly · `[DisplayName("7540-4.1-001: Valid 9-byte frame header decoded correctly")]`
- [x] `7540-4.1-002` **P0** — 24-bit length field parsed (lengths > 65535) · `[DisplayName("7540-4.1-002: Frame length uses 24-bit field")]`
- [x] `7540-4.1-003` **P0** `[T]` — All frame types 0x00–0x09 dispatched (× 10) · `[DisplayName("7540-4.1-003: Frame type {type} dispatched to correct handler")]`
- [x] `7540-4.1-004` **P0** — Unknown frame type 0x0A ignored · `[DisplayName("7540-4.1-004: Unknown frame type 0x0A is ignored")]`
- [x] `7540-4.1-005` **P0** — R-bit masked out when reading stream ID · `[DisplayName("7540-4.1-005: R-bit masked out when reading stream ID")]`
- [x] `7540-4.1-006` **P0** — R-bit set → `PROTOCOL_ERROR` · `[DisplayName("7540-4.1-006: R-bit set in stream ID causes PROTOCOL_ERROR")]`
- [x] `7540-4.1-007` **P0** — Frame > MAX_FRAME_SIZE → `FRAME_SIZE_ERROR` · `[DisplayName("7540-4.1-007: Oversized frame causes FRAME_SIZE_ERROR")]`

### All 14 HTTP/2 Error Codes (RFC 7540 §7)

- [x] `7540-err-000` **P0** — `NO_ERROR (0x0)` in GOAWAY decoded · `[DisplayName("7540-err-000: NO_ERROR (0x0) in GOAWAY decoded")]`
- [x] `7540-err-001` **P0** — `PROTOCOL_ERROR (0x1)` in RST_STREAM decoded · `[DisplayName("7540-err-001: PROTOCOL_ERROR (0x1) in RST_STREAM decoded")]`
- [x] `7540-err-002` **P0** — `INTERNAL_ERROR (0x2)` in GOAWAY decoded · `[DisplayName("7540-err-002: INTERNAL_ERROR (0x2) in GOAWAY decoded")]`
- [x] `7540-err-003` **P0** — `FLOW_CONTROL_ERROR (0x3)` in GOAWAY decoded · `[DisplayName("7540-err-003: FLOW_CONTROL_ERROR (0x3) in GOAWAY decoded")]`
- [x] `7540-err-004` **P0** — `SETTINGS_TIMEOUT (0x4)` in GOAWAY decoded · `[DisplayName("7540-err-004: SETTINGS_TIMEOUT (0x4) in GOAWAY decoded")]`
- [x] `7540-err-005` **P0** — `STREAM_CLOSED (0x5)` in RST_STREAM decoded · `[DisplayName("7540-err-005: STREAM_CLOSED (0x5) in RST_STREAM decoded")]`
- [x] `7540-err-006` **P0** — `FRAME_SIZE_ERROR (0x6)` decoded · `[DisplayName("7540-err-006: FRAME_SIZE_ERROR (0x6) decoded")]`
- [x] `7540-err-007` **P0** — `REFUSED_STREAM (0x7)` in RST_STREAM decoded · `[DisplayName("7540-err-007: REFUSED_STREAM (0x7) in RST_STREAM decoded")]`
- [x] `7540-err-008` **P0** — `CANCEL (0x8)` in RST_STREAM decoded · `[DisplayName("7540-err-008: CANCEL (0x8) in RST_STREAM decoded")]`
- [x] `7540-err-009` **P0** — `COMPRESSION_ERROR (0x9)` in GOAWAY decoded · `[DisplayName("7540-err-009: COMPRESSION_ERROR (0x9) in GOAWAY decoded")]`
- [x] `7540-err-00a` **P1** — `CONNECT_ERROR (0xa)` decoded · `[DisplayName("7540-err-00a: CONNECT_ERROR (0xa) in RST_STREAM decoded")]`
- [x] `7540-err-00b` **P1** — `ENHANCE_YOUR_CALM (0xb)` decoded · `[DisplayName("7540-err-00b: ENHANCE_YOUR_CALM (0xb) in GOAWAY decoded")]`
- [x] `7540-err-00c` **P1** — `INADEQUATE_SECURITY (0xc)` decoded · `[DisplayName("7540-err-00c: INADEQUATE_SECURITY (0xc) decoded")]`
- [x] `7540-err-00d` **P0** — `HTTP_1_1_REQUIRED (0xd)` in GOAWAY decoded · `[DisplayName("7540-err-00d: HTTP_1_1_REQUIRED (0xd) in GOAWAY decoded")]`

### Stream States (RFC 7540 §5.1)

- [x] `7540-5.1-003` **P0** — END_STREAM on incoming DATA → half-closed remote · `[DisplayName("7540-5.1-003: END_STREAM on incoming DATA moves stream to half-closed remote")]`
- [x] `7540-5.1-004` **P0** — Both sides END_STREAM → stream fully closed · `[DisplayName("7540-5.1-004: Both sides END_STREAM closes stream")]`
- [x] `7540-5.1-005` **P1** — PUSH_PROMISE → reserved remote state · `[DisplayName("7540-5.1-005: PUSH_PROMISE moves pushed stream to reserved remote")]`
- [x] `7540-5.1-006` **P0** — DATA on closed stream → `STREAM_CLOSED` · `[DisplayName("7540-5.1-006: DATA on closed stream causes STREAM_CLOSED error")]`
- [x] `7540-5.1-007` **P0** — Reusing closed stream ID → `PROTOCOL_ERROR` · `[DisplayName("7540-5.1-007: Reusing closed stream ID causes PROTOCOL_ERROR")]`
- [x] `7540-5.1-008` **P0** — Even stream ID from client → `PROTOCOL_ERROR` · `[DisplayName("7540-5.1-008: Client even stream ID causes PROTOCOL_ERROR")]`

### Flow Control — Decoder Side (RFC 7540 §5.2)

- [x] `7540-5.2-001dec` **P0** — New stream initial window = 65535 · `[DisplayName("7540-5.2-dec-001: New stream initial window is 65535")]`
- [x] `7540-5.2-002dec` **P0** — WINDOW_UPDATE decoded, window updated · `[DisplayName("7540-5.2-dec-002: WINDOW_UPDATE decoded and window updated")]`
- [x] `7540-5.2-003dec` **P0** — Peer DATA beyond window → `FLOW_CONTROL_ERROR` · `[DisplayName("7540-5.2-dec-003: Peer DATA beyond window causes FLOW_CONTROL_ERROR")]`
- [x] `7540-5.2-004dec` **P0** — WINDOW_UPDATE overflow → `FLOW_CONTROL_ERROR` · `[DisplayName("7540-5.2-dec-004: WINDOW_UPDATE overflow causes FLOW_CONTROL_ERROR")]`
- [x] `7540-5.2-008dec` **P0** — WINDOW_UPDATE increment=0 → `PROTOCOL_ERROR` · `[DisplayName("7540-5.2-dec-008: WINDOW_UPDATE increment=0 causes PROTOCOL_ERROR")]`

### DATA Frame (RFC 7540 §6.1)

- [x] `7540-6.1-001` **P0** — DATA frame payload decoded correctly · `[DisplayName("7540-6.1-001: DATA frame payload decoded correctly")]`
- [x] `7540-6.1-002` **P0** — END_STREAM on DATA marks stream complete · `[DisplayName("7540-6.1-002: END_STREAM on DATA marks stream closed")]`
- [x] `7540-6.1-003` **P1** — PADDED DATA: padding stripped · `[DisplayName("7540-6.1-003: Padded DATA frame padding stripped")]`
- [x] `7540-6.1-004` **P0** — DATA on stream 0 → `PROTOCOL_ERROR` · `[DisplayName("7540-6.1-004: DATA on stream 0 is PROTOCOL_ERROR")]`
- [x] `7540-6.1-005` **P0** — DATA on closed stream → `STREAM_CLOSED` · `[DisplayName("7540-6.1-005: DATA on closed stream causes STREAM_CLOSED")]`
- [x] `7540-6.1-006` **P0** — Empty DATA + END_STREAM: empty body, response complete · `[DisplayName("7540-6.1-006: Empty DATA frame with END_STREAM valid")]`

### HEADERS Frame (RFC 7540 §6.2)

- [x] `7540-6.2-001` **P0** — HEADERS frame decoded into response headers · `[DisplayName("7540-6.2-001: HEADERS frame decoded into response headers")]`
- [x] `7540-6.2-002` **P0** — END_STREAM on HEADERS closes stream immediately · `[DisplayName("7540-6.2-002: END_STREAM on HEADERS closes stream immediately")]`
- [x] `7540-6.2-003` **P0** — END_HEADERS marks header block complete · `[DisplayName("7540-6.2-003: END_HEADERS on HEADERS marks complete block")]`
- [x] `7540-6.2-004` **P1** — PADDED HEADERS: padding stripped · `[DisplayName("7540-6.2-004: Padded HEADERS padding stripped")]`
- [x] `7540-6.2-005` **P1** — PRIORITY flag consumed correctly · `[DisplayName("7540-6.2-005: PRIORITY flag in HEADERS consumed correctly")]`
- [x] `7540-6.2-006` **P0** — HEADERS without END_HEADERS waits for CONTINUATION · `[DisplayName("7540-6.2-006: HEADERS without END_HEADERS waits for CONTINUATION")]`
- [x] `7540-6.2-007` **P0** — HEADERS on stream 0 → `PROTOCOL_ERROR` · `[DisplayName("7540-6.2-007: HEADERS on stream 0 is PROTOCOL_ERROR")]`

### CONTINUATION Frame (RFC 7540 §6.9)

- [x] `7540-6.9-001` **P0** — CONTINUATION appended to HEADERS block · `[DisplayName("7540-6.9-001: CONTINUATION appended to HEADERS block")]`
- [x] `7540-6.9-002dec` **P0** — END_HEADERS on final CONTINUATION completes block · `[DisplayName("7540-6.9-dec-002: END_HEADERS on final CONTINUATION completes block")]`
- [x] `7540-6.9-003` **P0** — Multiple CONTINUATION frames all merged · `[DisplayName("7540-6.9-003: Multiple CONTINUATION frames all merged")]`
- [x] `7540-6.9-004` **P0** — CONTINUATION on wrong stream → `PROTOCOL_ERROR` · `[DisplayName("7540-6.9-004: CONTINUATION on wrong stream is PROTOCOL_ERROR")]`
- [x] `7540-6.9-005` **P0** — Non-CONTINUATION after HEADERS → `PROTOCOL_ERROR` · `[DisplayName("7540-6.9-005: Non-CONTINUATION after HEADERS is PROTOCOL_ERROR")]`
- [x] `7540-6.9-006` **P0** — CONTINUATION on stream 0 → `PROTOCOL_ERROR` · `[DisplayName("7540-6.9-006: CONTINUATION on stream 0 is PROTOCOL_ERROR")]`
- [x] `dec6-cont-001` **P0** — CONTINUATION without preceding HEADERS → `PROTOCOL_ERROR` · `[DisplayName("dec6-cont-001: CONTINUATION without HEADERS is PROTOCOL_ERROR")]`

### SETTINGS, PING, GOAWAY, RST_STREAM

- [x] `7540-set-001` **P0** — Server SETTINGS decoded (`HasNewSettings = true`) · `[DisplayName("RFC 7540: Server SETTINGS decoded")]`
- [x] `7540-set-002` **P0** — SETTINGS ACK generated after SETTINGS received · `[DisplayName("RFC 7540: SETTINGS ACK generated after SETTINGS")]`
- [x] `7540-set-003` **P0** — MAX_FRAME_SIZE applied from SETTINGS · `[DisplayName("RFC 7540: MAX_FRAME_SIZE applied from SETTINGS")]`
- [x] `dec6-set-001` **P0** `[T]` — All 6 SETTINGS parameters decoded (× 6) · `[DisplayName("dec6-set-001: SETTINGS parameter {param} decoded")]`
- [x] `dec6-set-002` **P0** — SETTINGS ACK with payload → `FRAME_SIZE_ERROR` · `[DisplayName("dec6-set-002: SETTINGS ACK with non-empty payload is FRAME_SIZE_ERROR")]`
- [x] `dec6-set-003` **P1** — Unknown SETTINGS ID accepted and ignored · `[DisplayName("dec6-set-003: Unknown SETTINGS parameter ID accepted and ignored")]`
- [x] `7540-ping-001` **P1** — PING request from server decoded · `[DisplayName("RFC 7540: PING request from server decoded")]`
- [x] `7540-ping-002` **P1** — PING ACK produced for server PING · `[DisplayName("RFC 7540: PING ACK produced for server PING")]`
- [x] `dec6-ping-001` **P1** — PING ACK carries same 8 payload bytes · `[DisplayName("dec6-ping-001: PING ACK carries same 8 payload bytes as request")]`
- [x] `7540-goaway-001` **P0** — GOAWAY decoded with last stream ID + error code · `[DisplayName("RFC 7540: GOAWAY with last stream ID and error code decoded")]`
- [x] `7540-goaway-002` **P0** — No new streams accepted after GOAWAY · `[DisplayName("RFC 7540: No new requests after GOAWAY")]`
- [x] `dec6-goaway-001` **P1** — GOAWAY debug data bytes accessible · `[DisplayName("dec6-goaway-001: GOAWAY debug data bytes accessible")]`
- [x] `7540-rst-001` **P0** — RST_STREAM decoded (`RstStreams` entry present) · `[DisplayName("RFC 7540: RST_STREAM decoded")]`
- [x] `7540-rst-002` **P0** — Stream closed after RST_STREAM · `[DisplayName("RFC 7540: Stream closed after RST_STREAM")]`

### TCP Fragmentation (HTTP/2)

- [x] `dec6-frag-001` **P0** — Frame header split at byte 1 reassembled · `[DisplayName("dec6-frag-001: Frame header split at byte 1 reassembled")]`
- [x] `dec6-frag-002` **P0** — Frame header split at byte 5 reassembled · `[DisplayName("dec6-frag-002: Frame header split at byte 5 reassembled")]`
- [x] `dec6-frag-003` **P0** — DATA payload split across two reads · `[DisplayName("dec6-frag-003: DATA frame payload split across two reads")]`
- [x] `dec6-frag-004` **P0** — HPACK block split across two reads · `[DisplayName("dec6-frag-004: HPACK block split across two reads")]`
- [x] `dec6-frag-005` **P1** — Two complete frames in single read both decoded · `[DisplayName("dec6-frag-005: Two complete frames in single read both decoded")]`

---

## Phase 7: HPACK (RFC 7541) — Full Coverage

**File:** `src/TurboHttp.Tests/HpackTests.cs`

### All 61 Static Table Entries (RFC 7541 Appendix A)

- [x] `7541-st-001` **P0** `[T]` — All 61 static table entries round-trip (indices 1–61 × 61) · `[DisplayName("7541-st-001: Static table entry {index} [{name}:{value}] round-trips as indexed representation")]`

### Sensitive Headers — NeverIndexed (RFC 7541 §7.1.3)

- [x] `7541-ni-001` **P0** `[T]` — Sensitive headers use `0x10` NeverIndexed prefix (authorization, cookie, set-cookie, proxy-authorization × 4) · `[DisplayName("7541-ni-001: {header} encoded with NeverIndexed byte pattern (0x10)")]`
- [x] `7541-ni-002` **P0** `[T]` — Sensitive headers do NOT grow the dynamic table (× 4) · `[DisplayName("7541-ni-002: {header} with NeverIndexed does not grow dynamic table")]`
- [x] `7541-ni-003` **P0** — Decoded authorization header preserves NeverIndex flag · `[DisplayName("7541-ni-003: Decoded authorization header preserves NeverIndex flag")]`

### Dynamic Table (RFC 7541 §2.3)

- [x] `7541-2.3-001` **P0** — Incrementally indexed header added at dynamic index 62 · `[DisplayName("7541-2.3-001: Incrementally indexed header added at dynamic index 62")]`
- [x] `7541-2.3-002` **P0** — Oldest entry evicted when dynamic table is full · `[DisplayName("7541-2.3-002: Oldest entry evicted when dynamic table full")]`
- [x] `7541-2.3-003` **P0** — Dynamic table resized on `SETTINGS_HEADER_TABLE_SIZE` · `[DisplayName("7541-2.3-003: Dynamic table resized on SETTINGS_HEADER_TABLE_SIZE")]`
- [x] `7541-2.3-004` **P0** — Table size 0 evicts all entries · `[DisplayName("7541-2.3-004: Dynamic table size 0 evicts all entries")]`
- [x] `7541-2.3-005` **P0** — Table size exceeding maximum → `HpackException` · `[DisplayName("7541-2.3-005: Table size exceeding maximum causes COMPRESSION_ERROR")]`
- [x] `hpack-dt-001` **P0** — Entry size = name length + value length + 32 bytes · `[DisplayName("hpack-dt-001: Entry size counted as name + value + 32 overhead")]`
- [x] `hpack-dt-002` **P0** — Size update prefix emitted before first header after resize · `[DisplayName("hpack-dt-002: Size update prefix emitted when table resized")]`
- [x] `hpack-dt-003` **P0** — Three entries evicted in FIFO order · `[DisplayName("hpack-dt-003: Three entries evicted in FIFO order")]`

### Integer Representation (RFC 7541 §5.1)

- [x] `7541-5.1-001` **P0** — Integer smaller than prefix limit encodes in one byte · `[DisplayName("7541-5.1-001: Integer smaller than prefix limit encodes in one byte")]`
- [x] `7541-5.1-002` **P0** — Integer at prefix limit requires continuation bytes · `[DisplayName("7541-5.1-002: Integer at prefix limit requires continuation bytes")]`
- [x] `7541-5.1-003` **P0** — Maximum integer 2147483647 round-trips exactly · `[DisplayName("7541-5.1-003: Maximum integer 2147483647 round-trips")]`
- [x] `7541-5.1-004` **P0** — Integer exceeding 2^31-1 → `HpackException` · `[DisplayName("7541-5.1-004: Integer exceeding 2^31-1 causes COMPRESSION_ERROR")]`
- [x] `hpack-int-001` **P0** `[T]` — Boundary values for 1–7 bit prefixes (× 7) · `[DisplayName("hpack-int-001: Integer encoding with {bits}-bit prefix")]`

### String Representation (RFC 7541 §5.2)

- [x] `7541-5.2-001` **P0** — Plain string literal (H=0) decoded · `[DisplayName("7541-5.2-001: Plain string literal decoded")]`
- [x] `7541-5.2-002` **P0** — Huffman-encoded string (H=1) decoded · `[DisplayName("7541-5.2-002: Huffman-encoded string decoded")]`
- [x] `7541-5.2-003` **P0** — Empty string literal decoded · `[DisplayName("7541-5.2-003: Empty string literal decoded")]`
- [x] `7541-5.2-004` **P1** — String larger than 8 KB decoded without truncation · `[DisplayName("7541-5.2-004: String larger than 8KB decoded")]`
- [x] `7541-5.2-005` **P0** — Malformed Huffman data → `HpackException` · `[DisplayName("7541-5.2-005: Malformed Huffman data causes COMPRESSION_ERROR")]`
- [x] `hpack-str-001` **P0** — Non-1 EOS padding bits → `HpackException` · `[DisplayName("hpack-str-001: Non-1 EOS padding bits cause COMPRESSION_ERROR")]`
- [x] `hpack-str-002` **P0** — EOS padding > 7 bits → `HpackException` · `[DisplayName("hpack-str-002: EOS padding > 7 bits causes COMPRESSION_ERROR")]`

### Indexed Header Field (RFC 7541 §6.1)

- [x] `7541-6.1-002` **P0** — Dynamic table entry at index 62+ retrieved · `[DisplayName("7541-6.1-002: Dynamic table entry at index 62+ retrieved")]`
- [x] `7541-6.1-003` **P0** — Out-of-range index → `HpackException` · `[DisplayName("7541-6.1-003: Index out of range causes COMPRESSION_ERROR")]`
- [x] `hpack-idx-001` **P0** — Index 0 is invalid → `HpackException` · `[DisplayName("hpack-idx-001: Index 0 is invalid per RFC 7541 §6.1")]`

### Literal Header Field (RFC 7541 §6.2)

- [x] `7541-6.2-001` **P0** — Incremental indexing: entry added at index 62 · `[DisplayName("7541-6.2-001: Incremental indexing adds entry to dynamic table")]`
- [x] `7541-6.2-002` **P0** — Without-indexing: NOT added to dynamic table · `[DisplayName("7541-6.2-002: Without-indexing literal not added to dynamic table")]`
- [x] `7541-6.2-003` **P0** — Never-indexed: NOT added, flag preserved · `[DisplayName("7541-6.2-003: NeverIndexed literal not added to table")]`
- [x] `7541-6.2-004` **P0** — Indexed name + literal value decoded · `[DisplayName("7541-6.2-004: Literal with indexed name and literal value decoded")]`
- [x] `7541-6.2-005` **P0** — Both name and value as literals decoded · `[DisplayName("7541-6.2-005: Literal with literal name and literal value decoded")]`

### Appendix C — Byte-Exact RFC Vectors

- [x] `7541-C.2-001` **P0** — Appendix C.2.1: first request, no Huffman · `[DisplayName("7541-C.2-001: RFC 7541 Appendix C.2.1 decode")]`
- [x] `7541-C.2-002` **P0** — Appendix C.2.2: dynamic table first referenced entry · `[DisplayName("7541-C.2-002: RFC 7541 Appendix C.2.2 decode (dynamic table)")]`
- [x] `7541-C.2-003` **P0** — Appendix C.2.3: third request, table state correct · `[DisplayName("7541-C.2-003: RFC 7541 Appendix C.2.3 decode")]`
- [x] `7541-C.3-001` **P0** — Appendix C.3: requests with Huffman encoding · `[DisplayName("7541-C.3-001: RFC 7541 Appendix C.3 decode with Huffman")]`
- [x] `7541-C.4-001` **P0** — Appendix C.4.1: response, no Huffman · `[DisplayName("7541-C.4-001: RFC 7541 Appendix C.4.1 decode")]`
- [x] `7541-C.4-002` **P0** — Appendix C.4.2: response, dynamic table reused · `[DisplayName("7541-C.4-002: RFC 7541 Appendix C.4.2 decode (dynamic table reused)")]`
- [x] `7541-C.4-003` **P0** — Appendix C.4.3: response, table state after C.4.2 · `[DisplayName("7541-C.4-003: RFC 7541 Appendix C.4.3 decode")]`
- [x] `7541-C.5-001` **P0** — Appendix C.5: responses with Huffman · `[DisplayName("7541-C.5-001: RFC 7541 Appendix C.5 decode with Huffman")]`
- [x] `7541-C.6-001` **P1** — Appendix C.6: large cookie responses · `[DisplayName("7541-C.6-001: RFC 7541 Appendix C.6 large cookie responses")]`

---

## Phase 8: Security & Limits

**New files:** `src/TurboHttp.Tests/Http11SecurityTests.cs`, `src/TurboHttp.Tests/Http2SecurityTests.cs`

### HTTP/1.1 Input Limits

- [x] `sec-001a` **P0** — 100 headers accepted at default limit · `[DisplayName("SEC-001a: 100 headers accepted at default limit")]`
- [x] `sec-001b` **P0** — 101 headers rejected above default limit · `[DisplayName("SEC-001b: 101 headers rejected above default limit")]`
- [x] `sec-001c` **P1** — Custom header count limit respected · `[DisplayName("SEC-001c: Custom header count limit respected")]`
- [x] `sec-002a` **P0** — 8191-byte header block accepted · `[DisplayName("SEC-002a: Header block below 8KB limit accepted")]`
- [x] `sec-002b` **P0** — 8193-byte header block rejected · `[DisplayName("SEC-002b: Header block above 8KB limit rejected")]`
- [x] `sec-002c` **P0** — Single 9000-byte header value rejected · `[DisplayName("SEC-002c: Single header value exceeding limit rejected")]`
- [x] `sec-003a` **P0** — Body at 10 MB limit accepted · `[DisplayName("SEC-003a: Body at configurable limit accepted")]`
- [x] `sec-003b` **P0** — Body exceeding 10 MB rejected · `[DisplayName("SEC-003b: Body exceeding limit rejected")]`
- [x] `sec-003c` **P1** — Zero body limit rejects any body · `[DisplayName("SEC-003c: Zero body limit rejects any body")]`

### HTTP Smuggling

- [x] `sec-005a` **P0** — `Transfer-Encoding` + `Content-Length` conflict rejected · `[DisplayName("SEC-005a: Transfer-Encoding + Content-Length rejected")]`
- [x] `sec-005b` **P0** — CRLF injection in header value rejected → `InvalidFieldValue` · `[DisplayName("SEC-005b: CRLF injection in header value rejected")]`
- [x] `sec-005c` **P0** — NUL byte in decoded header value rejected → `InvalidFieldValue` · `[DisplayName("SEC-005c: NUL byte in decoded header value rejected")]`

### State Isolation

- [x] `sec-006a` **P0** — `Reset()` after partial headers restores clean state · `[DisplayName("SEC-006a: Reset() after partial headers restores clean state")]`
- [x] `sec-006b` **P0** — `Reset()` after partial body restores clean state · `[DisplayName("SEC-006b: Reset() after partial body restores clean state")]`

### HTTP/2 Security

- [x] `sec-h2-001` **P0** — HPACK literal name exceeding limit → `HpackException` · `[DisplayName("SEC-h2-001: HPACK literal name exceeding limit causes COMPRESSION_ERROR")]`
- [x] `sec-h2-002` **P0** — HPACK literal value exceeding limit → `HpackException` · `[DisplayName("SEC-h2-002: HPACK literal value exceeding limit causes COMPRESSION_ERROR")]`
- [x] `sec-h2-003` **P0** — Excessive CONTINUATION frames (1000) rejected · `[DisplayName("SEC-h2-003: Excessive CONTINUATION frames rejected")]`
- [x] `sec-h2-004` **P1** — 100 streams immediately RST'd triggers protection (CVE-2023-44487) · `[DisplayName("SEC-h2-004: Rapid RST_STREAM cycling triggers protection (CVE-2023-44487)")]`
- [x] `sec-h2-005` **P1** — 10000 zero-length DATA frames rejected · `[DisplayName("SEC-h2-005: Excessive zero-length DATA frames rejected")]`
- [x] `sec-h2-006` **P0** — `SETTINGS_ENABLE_PUSH` > 1 → `PROTOCOL_ERROR` · `[DisplayName("SEC-h2-006: SETTINGS_ENABLE_PUSH value >1 causes PROTOCOL_ERROR")]`
- [x] `sec-h2-007` **P0** — `SETTINGS_INITIAL_WINDOW_SIZE` > 2^31-1 → `FLOW_CONTROL_ERROR` · `[DisplayName("SEC-h2-007: SETTINGS_INITIAL_WINDOW_SIZE >2^31-1 causes FLOW_CONTROL_ERROR")]`
- [x] `sec-h2-008` **P1** — Unknown SETTINGS ID silently ignored · `[DisplayName("SEC-h2-008: Unknown SETTINGS ID silently ignored")]`

---

## Phase 9: Round-Trip Tests — Encode → Decode

**New files:** `src/TurboHttp.Tests/Http11RoundTripTests.cs`, `src/TurboHttp.Tests/Http2RoundTripTests.cs`

### HTTP/1.1 Round-Trip

- [x] `rt11-001` **P0** — GET → 200 OK round-trip · `[DisplayName("RT-11-001: HTTP/1.1 GET → 200 OK round-trip")]`
- [x] `rt11-002` **P0** — POST JSON → 201 Created round-trip · `[DisplayName("RT-11-002: HTTP/1.1 POST JSON → 201 Created round-trip")]`
- [x] `rt11-003` **P0** — PUT → 204 No Content round-trip · `[DisplayName("RT-11-003: HTTP/1.1 PUT → 204 No Content round-trip")]`
- [x] `rt11-004` **P0** — DELETE → 200 OK round-trip · `[DisplayName("RT-11-004: HTTP/1.1 DELETE → 200 OK round-trip")]`
- [x] `rt11-005` **P0** — PATCH → 200 OK round-trip · `[DisplayName("RT-11-005: HTTP/1.1 PATCH → 200 OK round-trip")]`
- [x] `rt11-006` **P1** — HEAD → Content-Length but no body · `[DisplayName("RT-11-006: HTTP/1.1 HEAD → Content-Length but no body")]`
- [x] `rt11-007` **P1** — OPTIONS → 200 with Allow header · `[DisplayName("RT-11-007: HTTP/1.1 OPTIONS → 200 with Allow header")]`
- [x] `rt11-008` **P0** — GET → 200 chunked response round-trip · `[DisplayName("RT-11-008: HTTP/1.1 GET → 200 chunked response round-trip")]`
- [x] `rt11-009` **P0** — GET → response with 5 chunks concatenated · `[DisplayName("RT-11-009: HTTP/1.1 GET → response with 5 chunks round-trip")]`
- [x] `rt11-010` **P1** — Chunked response with trailer accessible · `[DisplayName("RT-11-010: HTTP/1.1 chunked response with trailer round-trip")]`
- [x] `rt11-011` **P0** — GET → 301 with Location round-trip · `[DisplayName("RT-11-011: HTTP/1.1 GET → 301 with Location round-trip")]`
- [x] `rt11-012` **P0** — POST binary → 200 binary response round-trip · `[DisplayName("RT-11-012: HTTP/1.1 POST binary → 200 binary response round-trip")]`
- [x] `rt11-013` **P0** — GET → 404 Not Found round-trip · `[DisplayName("RT-11-013: HTTP/1.1 GET → 404 Not Found round-trip")]`
- [x] `rt11-014` **P0** — GET → 500 Internal Server Error round-trip · `[DisplayName("RT-11-014: HTTP/1.1 GET → 500 Internal Server Error round-trip")]`
- [x] `rt11-015` **P1** — Two pipelined requests and responses round-trip · `[DisplayName("RT-11-015: Two pipelined requests and responses round-trip")]`
- [x] `rt11-016` **P0** — 100 Continue before 200 OK round-trip · `[DisplayName("RT-11-016: 100 Continue before 200 OK round-trip")]`
- [x] `rt11-017` **P0** — 1 MB body round-trip · `[DisplayName("RT-11-017: HTTP/1.1 1 MB body round-trip")]`
- [x] `rt11-018` **P0** — Binary body with null bytes preserved · `[DisplayName("RT-11-018: HTTP/1.1 binary body with null bytes round-trip")]`
- [x] `rt11-019` **P1** — Two responses on keep-alive connection · `[DisplayName("RT-11-019: Two responses on keep-alive connection round-trip")]`
- [x] `rt11-020` **P1** — `Content-Type: application/json; charset=utf-8` preserved · `[DisplayName("RT-11-020: Content-Type: application/json; charset=utf-8 preserved")]`

### HTTP/2 Round-Trip

- [x] `rt2-001` **P0** — Connection preface + SETTINGS exchange · `[DisplayName("RT-2-001: HTTP/2 connection preface + SETTINGS exchange")]`
- [x] `rt2-001` **P0** — Connection preface + SETTINGS exchange · `[DisplayName("RT-2-001: HTTP/2 connection preface + SETTINGS exchange")]`
- [x] `rt2-002` **P0** — GET → 200 on stream 1 · `[DisplayName("RT-2-002: HTTP/2 GET → 200 on stream 1")]`
- [x] `rt2-003` **P0** — POST → HEADERS+DATA → 201 response · `[DisplayName("RT-2-003: HTTP/2 POST → HEADERS+DATA → 201 response")]`
- [x] `rt2-004` **P0** — Three concurrent streams each complete independently · `[DisplayName("RT-2-004: HTTP/2 three concurrent streams each complete independently")]`
- [x] `rt2-005` **P0** — HPACK dynamic table reused across three requests · `[DisplayName("RT-2-005: HTTP/2 HPACK dynamic table reused across three requests")]`
- [x] `rt2-006` **P0** — Server SETTINGS → client ACK → both sides updated · `[DisplayName("RT-2-006: HTTP/2 server SETTINGS → client ACK → both sides updated")]`
- [x] `rt2-007` **P1** — Server PING → client PONG with same payload · `[DisplayName("RT-2-007: HTTP/2 server PING → client PONG with same payload")]`
- [x] `rt2-008` **P0** — GOAWAY received → no new requests sent · `[DisplayName("RT-2-008: HTTP/2 GOAWAY received → no new requests sent")]`
- [x] `rt2-009` **P0** — RST_STREAM cancels stream, other streams continue · `[DisplayName("RT-2-009: HTTP/2 RST_STREAM → stream dropped, other streams continue")]`
- [x] `rt2-010` **P0** — Authorization NeverIndexed preserved in round-trip · `[DisplayName("RT-2-010: Authorization header NeverIndexed in HTTP/2 round-trip")]`
- [x] `rt2-011` **P1** — Cookie NeverIndexed preserved in round-trip · `[DisplayName("RT-2-011: Cookie header NeverIndexed in HTTP/2 round-trip")]`
- [x] `rt2-012` **P0** — Large headers via CONTINUATION, all decoded · `[DisplayName("RT-2-012: HTTP/2 request with headers exceeding frame size uses CONTINUATION")]`
- [x] `rt2-013` **P1** — Server PUSH_PROMISE decoded, pushed response received · `[DisplayName("RT-2-013: HTTP/2 server PUSH_PROMISE decoded, pushed response received")]`
- [x] `rt2-014` **P0** — POST body larger than initial window uses WINDOW_UPDATE · `[DisplayName("RT-2-014: HTTP/2 POST body larger than initial window uses WINDOW_UPDATE")]`
- [x] `rt2-015` **P1** — 404 response on stream decoded · `[DisplayName("RT-2-015: HTTP/2 request → 404 response on stream decoded")]`

---

## Phase 10: TCP Fragmentation — Systematic Matrix

**New file:** `src/TurboHttp.Tests/TcpFragmentationTests.cs`

> Pattern: feed bytes in two slices `data[..splitPoint]` and `data[splitPoint..]`.
> First call must return `NeedMoreData`. Second call returns complete response.

### HTTP/1.0 Fragmentation

- [x] `frag10-001` **P0** — Status-line split at byte 1 · `[DisplayName("FRAG-10-001: HTTP/1.0 status-line split at byte 1")]`
- [x] `frag10-002` **P0** — Status-line split at byte 8 (mid-version) · `[DisplayName("FRAG-10-002: HTTP/1.0 status-line split mid-version")]`
- [x] `frag10-003` **P0** — Header name split mid-word · `[DisplayName("FRAG-10-003: HTTP/1.0 header name split mid-word")]`
- [x] `frag10-004` **P0** — Body split at first byte · `[DisplayName("FRAG-10-004: HTTP/1.0 body split at first byte")]`
- [x] `frag10-005` **P0** — Body split at midpoint · `[DisplayName("FRAG-10-005: HTTP/1.0 body split at midpoint")]`

### HTTP/1.1 Fragmentation

- [x] `frag11-001` **P0** — Status-line split at byte 1 · `[DisplayName("FRAG-11-001: HTTP/1.1 status-line split at byte 1")]`
- [x] `frag11-002` **P0** — Status-line split mid-version · `[DisplayName("FRAG-11-002: HTTP/1.1 status-line split inside version")]`
- [x] `frag11-003` **P0** — Header split at colon · `[DisplayName("FRAG-11-003: HTTP/1.1 header split at colon")]`
- [x] `frag11-004` **P0** — Header-body boundary split · `[DisplayName("FRAG-11-004: HTTP/1.1 split at first byte of CRLFCRLF")]`
- [x] `frag11-005` **P0** — Chunked: chunk-size line split mid-hex · `[DisplayName("FRAG-11-005: HTTP/1.1 chunk-size line split mid-hex")]`
- [x] `frag11-006` **P0** — Chunked: chunk data split mid-content · `[DisplayName("FRAG-11-006: HTTP/1.1 chunk data split mid-content")]`
- [x] `frag11-007` **P0** — Chunked: final chunk split · `[DisplayName("FRAG-11-007: HTTP/1.1 final 0-chunk split")]`
- [x] `frag11-008` **P0** — Single-byte delivery assembles complete response · `[DisplayName("FRAG-11-008: HTTP/1.1 response delivered 1 byte at a time")]`

### HTTP/2 Fragmentation

- [x] `frag2-001` **P0** — Frame header split at byte 1 · `[DisplayName("FRAG-2-001: HTTP/2 frame header split at byte 1")]`
- [x] `frag2-002` **P0** — Frame header split at byte 3 (end of length) · `[DisplayName("FRAG-2-002: HTTP/2 frame header split at byte 3 (end of length)")]`
- [x] `frag2-003` **P0** — Frame header split at byte 5 (flags) · `[DisplayName("FRAG-2-003: HTTP/2 frame header split at byte 5 (flags)")]`
- [x] `frag2-004` **P0** — Frame header split at byte 8 (last stream byte) · `[DisplayName("FRAG-2-004: HTTP/2 frame header split at byte 8 (last stream byte)")]`
- [x] `frag2-005` **P0** — DATA payload split mid-content · `[DisplayName("FRAG-2-005: HTTP/2 DATA payload split mid-content")]`
- [x] `frag2-006` **P0** — HPACK block split mid-stream · `[DisplayName("FRAG-2-006: HTTP/2 HEADERS HPACK block split mid-stream")]`
- [x] `frag2-007` **P0** — Split between HEADERS and CONTINUATION frames · `[DisplayName("FRAG-2-007: HTTP/2 split between HEADERS and CONTINUATION frames")]`
- [x] `frag2-008` **P0** — Two complete frames in one buffer both processed · `[DisplayName("FRAG-2-008: Two complete HTTP/2 frames in one read both processed")]`
- [x] `frag2-009` **P0** — Second stream HEADERS split across reads · `[DisplayName("FRAG-2-009: Second stream's HEADERS split across reads while first stream active")]`

---

## Phase 11: Benchmarks

### New Project: `src/TurboHttp.Benchmarks/TurboHttp.Benchmarks.csproj`

- `net10.0`, `<Optimize>true</Optimize>`, `<AllowUnsafeBlocks>true</AllowUnsafeBlocks>`
- NuGet: `BenchmarkDotNet`
- Project reference to `TurboHttp`
- Add to `TurboHttp.sln`

### Files

**`Program.cs`**: `BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);`

**`EncoderBenchmarks.cs`** — `[MemoryDiagnoser]` `[ShortRunJob]`
- `Http10_Encode_SimpleGet`
- `Http10_Encode_WithHeaders_10`
- `Http11_Encode_SimpleGet`
- `Http11_Encode_Post_1KB`
- `Http11_Encode_Post_64KB`
- `Http11_Encode_WithHeaders_20`
- `Http2_Encode_ColdHpackTable`
- `Http2_Encode_WarmHpackTable_10Requests`

**`DecoderBenchmarks.cs`** — `[MemoryDiagnoser]` `[ShortRunJob]`
- `Http10_Decode_200_NoBody`
- `Http10_Decode_200_1KB`
- `Http11_Decode_200_NoBody`
- `Http11_Decode_200_1KB`
- `Http11_Decode_200_Chunked_8Chunks`
- `Http11_Decode_200_64KB`
- `Http2_Decode_SingleDataFrame`
- `Http2_Decode_8DataFrames`

**`HpackBenchmarks.cs`** — `[MemoryDiagnoser]` `[ShortRunJob]`
- `Encode_StaticOnly` / `Encode_Cold` / `Encode_Warm`
- `Decode_StaticOnly` / `Decode_Cold` / `Decode_Warm`
- `Huffman_Encode_16chars` / `Huffman_Encode_256chars`
- `Huffman_Decode_16chars` / `Huffman_Decode_256chars`

### IMPLEMENTATION_PLAN.md section

```
## Phase 11: Benchmarks
File: src/TurboHttp.Benchmarks/
- [x] Create TurboHttp.Benchmarks.csproj + add to solution
- [x] Program.cs
- [x] EncoderBenchmarks.cs  (8 benchmarks)
- [x] DecoderBenchmarks.cs  (8 benchmarks)
- [x] HpackBenchmarks.cs    (10 benchmarks)
```

---

## Phase 12: HTTP/1.0 Integration Tests (~90 tests)

**File**: `src/TurboHttp.IntegrationTests/Http10/`
**Fixture**: `KestrelFixture` (HTTP/1.0 or HTTP/1.1 server, both acceptable for 1.0 responses)
**ID prefix**: `IT-10-`
**Encoding**: All requests use `Http10Encoder`, all responses decoded by `Http10Decoder`.

### Test Classes

**`Http10BasicTests.cs`** (~25 tests)
- [x] GET /hello → 200, body "Hello World"
- [x] GET /hello — headers: Date present, Content-Length correct
- [x] GET /large/1 → 200, 1 KB body
- [x] GET /large/64 → 200, 64 KB body
- [x] GET /status/200, /201, /204, /301, /400, /404, /500
- [x] GET /ping → 200, body "pong"
- [x] GET /content/text%2Fhtml → Content-Type header parsed
- [x] GET /content/application%2Fjson → Content-Type correct
- [x] HEAD /hello → 200, no body, Content-Length present
- [x] GET /methods → body "GET"

**`Http10BodyTests.cs`** (~20 tests)
- [x] POST /echo small body → 200, body echoed
- [x] POST /echo 1KB body → correct
- [x] POST /echo 64KB body → correct
- [x] POST /echo empty body → 200, empty body
- [x] POST /echo binary body (0x00..0xFF) → byte-accurate round-trip
- [x] POST /echo body with CRLF in it
- [x] POST /echo body with null bytes
- [x] Body Content-Length matches actual byte count (5 variations)
- [x] POST /echo Content-Type mirrored correctly

**`Http10HeaderTests.cs`** (~20 tests)
- [x] GET /headers/echo with X-Test: value → echoed in response
- [x] GET /headers/echo multiple X-* headers → all echoed
- [x] GET /headers/echo header with Unicode value (Latin-1)
- [x] GET /auth without Authorization → 401
- [x] GET /auth with valid Authorization → 200
- [x] Response headers: Server, Date, Content-Type — presence/format
- [x] Custom response headers from /headers/set?Foo=Bar
- [x] Multiple values for same header name
- [x] Header names case-insensitive match
- [x] Content-Length vs actual body byte count

**`Http10StatusCodeTests.cs`** (~15 tests)
- [x] 200, 201, 204, 206, 301, 302, 400, 401, 403, 404, 405, 408, 500, 502, 503

**`Http10ConnectionTests.cs`** (~10 tests)
- [x] Connection closes after response (HTTP/1.0 default)
- [x] Multiple sequential requests need separate connections
- [x] Server-sent Connection:close respected
- [x] TryDecodeEof on closed connection succeeds
- [x] Partial response → decoder returns Incomplete

---

## Phase 13: HTTP/1.1 Integration Tests — Core (~110 tests)

**File**: `src/TurboHttp.IntegrationTests/Http11/`
**Fixture**: `KestrelFixture`
**ID prefix**: `IT-11-`
**Encoding**: `Http11Encoder` + `Http11Decoder`.

### Test Classes

**`Http11BasicTests.cs`** (~25 tests)
- [x] GET, POST, HEAD, PUT, DELETE, PATCH, OPTIONS (7 verbs × basic scenario)
- [x] GET /hello Host header required
- [x] GET /hello multiple status codes: 200..503 (8 codes)
- [x] GET /large/1, /large/64, /large/512 KB
- [x] GET /content/text%2Fplain, /content/application%2Fjson, /content/application%2Foctet-stream

**`Http11KeepAliveTests.cs`** (~20 tests)
- [x] 2 sequential requests on same connection
- [x] 5 sequential requests on same connection
- [x] 10 sequential requests on same connection
- [x] Server Connection:close terminates reuse
- [x] Request with Connection:close header
- [x] Mixed keep-alive + close on same socket
- [x] Decoder resets cleanly between requests
- [x] Keep-alive with varying body sizes
- [x] Keep-alive: GET then POST then GET
- [x] Pipeline depth 2: two requests in flight
- [x] Pipeline depth 5
- [x] Pipeline with mixed verbs
- [x] Responses arrive in request order
- [x] Keep-alive timeout (slow server → close)

**`Http11ChunkedTests.cs`** (~25 tests)
- [x] GET /chunked/1 → 1 KB chunked
- [x] GET /chunked/64 → 64 KB chunked
- [x] GET /chunked/512 → 512 KB chunked
- [x] Chunk count: 1 chunk / 4 chunks / 32 chunks
- [x] Chunk sizes: 1 byte, 128 bytes, 4 KB, 16 KB
- [x] Chunked body round-trip (POST /echo/chunked)
- [x] Chunked with trailer headers
- [x] Chunked then keep-alive (next request on same connection)
- [x] Chunked with empty last chunk only (valid zero-body)
- [x] Chunked body matches Content-MD5
- [x] Chunked response to HEAD is empty
- [x] Decode chunked across multiple TCP reads (fragmentation)
- [x] Last-chunk `0\r\n\r\n` immediately after data

**`Http11HeaderTests.cs`** (~20 tests)
- [x] 20 custom headers round-trip
- [x] Duplicate header names (List-append semantics)
- [x] Content-Type with charset parameter
- [x] Multi-value Accept header
- [x] Authorization header preserved
- [x] Cookie header preserved
- [x] Response Date header parses as RFC 7231 date
- [x] ETag / If-None-Match conditional 304
- [x] Cache-Control directives
- [x] X-* custom headers echoed
- [x] Very long header value (8 KB)
- [x] Header name case folding
- [x] Folded header value (obs-fold) rejected

**`Http11StatusAndErrorTests.cs`** (~20 tests)
- [x] All 2xx: 200, 201, 202, 204, 206
- [x] All 3xx: 301, 302, 303, 307, 308
- [x] All 4xx: 400, 401, 403, 404, 405, 408, 409, 410, 413, 429
- [x] All 5xx: 500, 501, 502, 503, 504

---

## Phase 14: HTTP/1.1 Integration Tests — Advanced (~90 tests)

**File**: `src/TurboHttp.IntegrationTests/Http11Advanced/`
**ID prefix**: `IT-11A-`

### Test Classes

**`Http11ContentNegotiationTests.cs`** (~20 tests)
- [x] Accept: application/json → Content-Type: application/json
- [x] Accept: text/html → Content-Type: text/html
- [x] Accept: */* → server default type
- [x] Accept-Charset header
- [x] Accept-Language header
- [x] Content-Type: multipart/form-data (server parses)
- [x] Content-Type: application/x-www-form-urlencoded
- [x] Content-Encoding: identity (default)
- [x] Request with Content-Encoding header
- [x] Response Content-Encoding:gzip decoded metadata (not body decompress — just header present)
- [x] Vary: Accept header in response

**`Http11RangeTests.cs`** (~20 tests)
- [x] Range: bytes=0-99 → 206, Content-Range header
- [x] Range: bytes=0-0 → 1 byte
- [x] Range: bytes=-100 → last 100 bytes
- [x] Range: bytes=100- → from byte 100 to end
- [x] Range on 1 KB body
- [x] Range on 64 KB body
- [x] Range: unsatisfiable range → 416
- [x] No Range header → 200 (full body)
- [x] If-Range with matching ETag
- [x] If-Range with non-matching ETag → 200 full
- [x] Range: bytes=0-49,50-99 (multi-range, server returns 200 or 206)

**`Http11CachingTests.cs`** (~20 tests)
- [x] If-None-Match matches ETag → 304 no body
- [x] If-None-Match no match → 200 full body
- [x] If-Modified-Since past → 200
- [x] If-Modified-Since future → 304
- [x] Cache-Control: no-cache in request
- [x] Cache-Control: max-age=0
- [x] Response Cache-Control: no-store
- [x] ETag format valid (quoted string)
- [x] Last-Modified in response
- [x] Expires header in response
- [x] Pragma: no-cache

**`Http11SecurityTests.cs`** (~15 tests)
- [x] Very large request body (10 MB) — decoder handles without OOM
- [x] Very many request headers (50 headers) — all preserved
- [x] Header injection attempt in value rejected by encoder
- [x] CRLF in body — body treated as opaque bytes, not parsed
- [x] Zero-length Content-Length body
- [x] Negative Content-Length → encoder rejects
- [x] Request URI > 8 KB — encoder encodes, decoder accepts
- [x] Slow response (server sends 1 byte/ms) — decoder accumulates

**`Http11EdgeCaseTests.cs`** (~15 tests)
- [x] Empty response body with Content-Length: 0
- [x] 204 No Content — no body, no Content-Length
- [x] 304 Not Modified — no body
- [x] Response with only headers (no body allowed)
- [x] Very short response (HTTP/1.1 200 OK\r\n\r\n)
- [x] Multiple CRLF between header and body
- [x] Response with unknown headers — preserved as-is
- [x] OPTIONS * (asterisk request target)
- [x] POST with empty body
- [x] PUT with binary body (0x00..0xFF)
- [x] PATCH with JSON body

---

## Phase 15: HTTP/2 Integration Tests — Core (~100 tests)

**File**: `src/TurboHttp.IntegrationTests/Http2/`
**Fixture**: `KestrelH2Fixture` (h2c cleartext)
**ID prefix**: `IT-2-`
**Encoding**: `Http2Encoder` + `Http2Decoder`; manual connection preface in test helper.

### Test Classes

**`Http2ConnectionTests.cs`** (~20 tests)
- [x] Connection preface sent and SETTINGS received
- [x] SETTINGS ACK sent and received
- [x] PING → PING ACK round-trip
- [x] Multiple PING frames
- [x] Initial WINDOW_SIZE from server SETTINGS
- [x] SETTINGS: HEADER_TABLE_SIZE negotiated
- [x] SETTINGS: MAX_CONCURRENT_STREAMS respected
- [x] SETTINGS: MAX_FRAME_SIZE negotiated
- [x] SETTINGS: INITIAL_WINDOW_SIZE update mid-connection
- [x] Idle connection — no frames for 5s — no error
- [x] GOAWAY received after connection error
- [x] Server sends GOAWAY on close
- [x] Client sends GOAWAY before disconnect
- [x] SETTINGS frame with zero parameters
- [x] Connection-level flow control initial value
- [x] WINDOW_UPDATE on connection level

**`Http2StreamTests.cs`** (~25 tests)
- [x] Stream 1: GET /hello → HEADERS(200) + DATA("Hello World") + END_STREAM
- [x] Stream 1: POST /echo → HEADERS + DATA(request body) → HEADERS(200) + DATA
- [x] Stream 1: HEAD /hello → HEADERS(200), no DATA frame
- [x] Stream with empty response body (204)
- [x] Stream 1 then stream 3 (sequential, odd IDs)
- [x] Three sequential streams (1, 3, 5)
- [x] Stream RST_STREAM: client cancels → RST received
- [x] Stream RST_STREAM: server rejects → RST received
- [x] END_STREAM on HEADERS frame (no body request)
- [x] END_STREAM on DATA frame
- [x] CONTINUATION frame for large HEADERS
- [x] Multiple CONTINUATION frames
- [x] Stream state: idle → open → half-closed → closed
- [x] Large response body (64 KB) across multiple DATA frames
- [x] Large request body (64 KB) via DATA frames

**`Http2HpackTests.cs`** (~25 tests)
- [x] First request: all headers literal
- [x] Second identical request: indexed headers (smaller HEADERS frame)
- [x] HPACK dynamic table grows across requests
- [x] HPACK: sensitive header never-index
- [x] HPACK: static table entries used (method, path, status)
- [x] HPACK: Huffman encoding on/off per field
- [x] HPACK: dynamic table eviction (size limit reached)
- [x] HPACK: SETTINGS_HEADER_TABLE_SIZE reduces table
- [x] 20 custom headers compressed across 3 requests
- [x] Cookie header split into multiple cookie-pairs
- [x] Authorization header never-indexed
- [x] Pseudo-headers order: :method, :path, :scheme, :authority
- [x] Pseudo-headers in response: :status only
- [x] Unknown pseudo-headers rejected by decoder
- [x] Decoder: indexed header block + literal + indexed mix

**`Http2DataFrameTests.cs`** (~15 tests)
- [x] DATA frame padding ignored
- [x] DATA frame PADDED flag handled
- [x] Empty DATA frame (0 bytes)
- [x] DATA frame exactly at MAX_FRAME_SIZE
- [x] DATA frame exceeding MAX_FRAME_SIZE split by encoder
- [x] Flow control: send exactly window size
- [x] Flow control: pause when window exhausted
- [x] WINDOW_UPDATE resumes flow
- [x] Stream-level flow control
- [x] Connection-level flow control
- [x] DATA frame with END_STREAM
- [x] Multiple DATA frames then END_STREAM
- [x] DATA fragments correctly reassembled (body matches)

**`Http2ErrorTests.cs`** (~15 tests)
- [x] GOAWAY with PROTOCOL_ERROR
- [x] GOAWAY with ENHANCE_YOUR_CALM
- [x] RST_STREAM with CANCEL
- [x] RST_STREAM with STREAM_CLOSED
- [x] Invalid HEADERS on closed stream
- [x] Decoder: unexpected frame type → protocol error
- [x] Server-initiated RST — client decodes cleanly
- [x] SETTINGS with invalid parameter → PROTOCOL_ERROR
- [x] Stream ID 0 used for DATA → connection error
- [x] Even stream ID from client → PROTOCOL_ERROR
- [x] HEADERS without :method → decoder flags error
- [x] HEADERS without :path → decoder flags error

---

## Phase 16: HTTP/2 Integration Tests — Advanced (~70 tests)

**File**: `src/TurboHttp.IntegrationTests/Http2Advanced/`
**ID prefix**: `IT-2A-`

### Test Classes

**`Http2MultiplexingTests.cs`** (~25 tests)
- [x] 2 concurrent streams on same connection
- [x] 4 concurrent streams on same connection
- [x] 8 concurrent streams on same connection
- [x] 16 concurrent streams on same connection
- [x] Streams interleaved: DATA frames from different streams
- [x] Streams complete out of order
- [x] High-priority stream completes before low-priority
- [x] Concurrent GET + POST
- [x] Stream 1 large body + stream 3 small body (interleaved DATA)
- [x] MAX_CONCURRENT_STREAMS = 1: second request waits
- [x] MAX_CONCURRENT_STREAMS = 4: fifth request queued
- [x] All concurrent streams return correct bodies
- [x] Concurrent streams with different response codes
- [x] Two streams with same request path
- [x] Stream reuse: connection used for 20 sequential+concurrent streams

**`Http2PushPromiseTests.cs`** (~10 tests)
- [x] PUSH_PROMISE received and decoded
- [x] Push stream ID is even (server-initiated)
- [x] PUSH_PROMISE headers decoded by HpackDecoder
- [x] Push stream DATA frames received
- [x] RST_STREAM on pushed stream (refuse push)
- [x] PUSH_PROMISE disabled via SETTINGS_ENABLE_PUSH=0
- [x] PUSH_PROMISE with :path and :status pseudo-headers
- [x] Multiple push promises in one response
- [x] Push promise on stream 1 → push stream 2
- [x] Push stream END_STREAM flag

**`Http2FlowControlTests.cs`** (~15 tests)
- [x] Stream window exhaustion: encoder pauses DATA
- [x] WINDOW_UPDATE received: encoder resumes
- [x] Connection window exhaustion
- [x] Connection WINDOW_UPDATE resumes
- [x] Mixed stream + connection flow control
- [x] Default stream window (65535 bytes)
- [x] Window overflow detection: > 2^31-1 → FLOW_CONTROL_ERROR
- [x] Zero WINDOW_UPDATE increment → PROTOCOL_ERROR (stream)
- [x] Zero WINDOW_UPDATE increment → PROTOCOL_ERROR (connection)
- [x] 64 KB body fits in one window
- [x] 128 KB body requires window update mid-transfer
- [x] Multiple WINDOW_UPDATE frames cumulative
- [x] Encoder correctly tracks remaining window

**`Http2LargePayloadTests.cs`** (~10 tests)
- [x] 1 MB response body decoded correctly
- [x] 4 MB response body decoded correctly
- [x] 1 MB request body encoded + sent correctly
- [x] Multiple DATA frames reassembly order preserved
- [x] Body matches SHA-256 of expected content
- [x] Large body + large headers (1 KB headers + 1 MB body)
- [x] Streaming decode: process frames as they arrive
- [x] Memory usage: no unbounded accumulation on large body

**`Http2EdgeCaseTests.cs`** (~10 tests)
- [x] Immediately closed stream (HEADERS + END_STREAM, no DATA)
- [x] SETTINGS with multiple parameters in one frame
- [x] PING with 8-byte opaque data round-trip
- [x] Decoder handles unknown frame type (ignore)
- [x] Decoder handles unknown flags (ignore)
- [x] GOAWAY received mid-connection: in-flight streams complete
- [x] Connection reuse after SETTINGS_MAX_CONCURRENT_STREAMS increase
- [x] Priority frames (PRIORITY) decoded without error (deprecated but valid)
- [x] Very long :path value (4 KB URI)
- [x] :authority with port number

---

## Phase 17: Stress & Production-Readiness (~55 tests)

**File**: `src/TurboHttp.IntegrationTests/Stress/`
**ID prefix**: `IT-STRESS-`
**Note**: These are slower tests; use `[Trait("Category", "Stress")]` to allow separate CI runs.

### Test Classes

**`Http11StressTests.cs`** (~20 tests)
- [x] 100 sequential GET requests on one connection — all 200
- [x] 1000 sequential GET requests — no memory growth
- [x] 100 POST /echo requests with varying body sizes
- [x] 50 concurrent connections × 1 request each
- [x] 10 concurrent connections × 10 requests each
- [x] Pipeline 10 requests × 10 iterations
- [x] Sustained keep-alive: 500 requests, connection never dropped
- [x] Large body stream: 10 × 512 KB = 5 MB total
- [x] Header stress: 50 custom headers × 100 requests
- [x] Mixed verbs 100 iterations: GET, POST, PUT, DELETE cycling
- [x] Decoder reset between requests: no state leakage
- [x] Memory: heap stable after 1000 requests (< 5 MB delta)
- [x] GC pressure: no LOH allocations in steady state
- [x] Throughput: > 10 MB/s encode+decode (measured, not asserted)

**`Http2StressTests.cs`** (~20 tests)
- [x] 100 sequential streams on one connection — all 200
- [x] 500 sequential streams — no state leakage
- [x] 32 concurrent streams × 10 iterations
- [x] HPACK table: 1000 unique headers — no corruption
- [x] HPACK table: 10 000 repeated headers — compression ratio > 80%
- [x] Flow control: 100 × 128 KB bodies, window updated correctly each time
- [x] 10 connections × 100 streams each (parallel connections)
- [x] Encoder stream IDs: 1000 requests → IDs 1,3,5…1999
- [x] Decoder: interleaved DATA frames from 16 streams — body integrity
- [x] Memory: stable after 500 streams
- [x] GOAWAY graceful shutdown after 100 streams
- [x] Throughput: > 10 MB/s encode+decode (measured)

**`Http11DecoderRobustnessTests.cs`** (~15 tests)
- [x] Decoder receives response 1 byte at a time — eventually succeeds
- [x] Decoder receives response in 2-byte chunks
- [x] Decoder receives headers in 1 chunk, body in 1000 tiny chunks
- [x] Decoder on 10 000 fragmentation patterns (fuzz-style)
- [x] EOF mid-header → TryDecodeEof returns error
- [x] EOF mid-body (no chunked) → TryDecodeEof uses remaining bytes
- [x] EOF mid-chunk → TryDecodeEof error
- [x] Decoder Reset() clears all state — next response decoded fresh
- [x] Decoder handles response with no headers except mandatory ones
- [x] Decoder handles HTTP/1.0 response on HTTP/1.1 connection
- [x] Content-Length mismatch: server sends fewer bytes → incomplete
- [x] Two responses in one TCP segment — both decoded
- [x] Interleaved partial responses from two connections

---

## Phase 18: Core Performance Validation (~20 Benchmarks)

**File**: `benchmarks/Core/`
**ID prefix**: `BM-CORE-`

---

### Request Performance

- [x] P50 / P99 latency — Warm requests
- [x] Cold start request latency
- [x] Throughput (Requests/sec)
- [x] Roundtrip latency over localhost
- [x] Roundtrip latency over simulated WAN

---

### Memory Efficiency

- [x] Bytes allocated per request
- [x] GC Gen0 collections per 10k requests
- [x] LOH allocation detection
- [x] Peak heap size during burst load

---

### Connection Handling

- [x] Connection reuse ratio
- [x] TLS session reuse cost
- [x] Connection acquisition latency
- [x] Idle connection retention performance

---

---

## Phase 19: Streaming & Protocol Efficiency (~25 Benchmarks)

**File**: `benchmarks/Protocol/`
**ID prefix**: `BM-PROTO-`

---

### HTTP/1.1 Efficiency

- [ ] Chunked encoding throughput
- [ ] Header parsing latency
- [ ] Large header sets parsing cost
- [ ] Pipeline request throughput
- [ ] Mixed verb workload performance

---

### HTTP/2 Multiplexing

- [ ] Concurrent stream throughput
- [ ] Stream scheduling overhead
- [ ] HPACK compression efficiency
- [ ] Frame decoding throughput
- [ ] Flow control window behavior

---

### Serialization Paths

- [ ] Small payload path (<128 bytes)
- [ ] Medium payload path (~1 MB)
- [ ] Large payload streaming (>5 MB)
- [ ] Zero-copy path validation

---

---

## Phase 20: Concurrency & Production Load Simulation (~25 Benchmarks)

**File**: `benchmarks/Concurrency/`
**ID prefix**: `BM-CONC-`

---

### Scaling Behavior

- [ ] 100 → 10k concurrent requests scaling curve
- [ ] ThreadPool saturation point
- [ ] Request scheduling fairness
- [ ] Async continuation overhead

---

### Burst Traffic Simulation

- [ ] Spike load (0 → 5000 RPS → 0)
- [ ] Request queue backpressure performance
- [ ] Timeout handling cost

---

### Failure Recovery Performance

- [ ] Retry latency overhead
- [ ] Circuit breaker recovery cost
- [ ] Cancellation propagation performance

---

---

## Phase 21: Enterprise Stability & Real World Patterns (~30 Benchmarks)

**File**: `benchmarks/Stability/`
**ID prefix**: `BM-ENT-`

---

### Long Running Stability

- [ ] 24 hour sustained load
- [ ] 10M total requests
- [ ] Memory growth slope < linear

---

### Network Variability Simulation

- [ ] Latency jitter tolerance
- [ ] Packet fragmentation handling
- [ ] Connection reset recovery

---

## Cloud / Microservice Patterns

- [ ] Gateway-style request patterns
- [ ] Authentication token refresh load
- [ ] Telemetry streaming workload

---

## Context

Phases 1–10 (RFC compliance for HTTP/1.0, 1.1, 2.0 encoders/decoders, HPACK, security, TCP fragmentation) are all complete.

The following phases extend the plan with:
1. **Phase 11 — Benchmarks**: BenchmarkDotNet performance suite for all encoder/decoder paths
2. **Phases 12–17 — Integration Tests**: ~515 tests across multiple phases, using real Kestrel TCP + raw `System.Net.Sockets.TcpClient` transport
3. **Phases 18-21 - TurboHttp vs HttpCliennt**: 

**Scope**: Encoders/decoders are **client-side only** (encode request → decode response).
**Transport**: Plain `System.Net.Sockets.TcpClient` + `NetworkStream` — no TurboHttp IO layer.
**Server**: In-process ASP.NET Minimal API on `localhost:0` (Kestrel, real TCP socket).

---

## Shared Infrastructure (prerequisite for Phases 12–17)

### `TurboHttp.IntegrationTests.csproj` patches

Add to existing empty `.csproj`:
- `<FrameworkReference Include="Microsoft.AspNetCore.App" />` — Kestrel + Minimal API
- Project reference to `TurboHttp`
- NuGet: `Microsoft.Extensions.Logging.Abstractions` (suppress Kestrel log noise in tests)
- NuGet: `xunit`

### `TestServer/` folder

**`KestrelFixture.cs`** — `IAsyncLifetime` base fixture
- `WebApplication _app`; `int Port`; `string Host = "127.0.0.1"`
- `StartAsync()`: `WebApplication.CreateBuilder()` → Kestrel `Listen(IPAddress.Loopback, 0)` → `MapRoutes()` → `app.StartAsync()`
- `DisposeAsync()`: `await _app.StopAsync()`
- `GetPort()`: reads from `_app.Urls` after start
- `NewTcpStream()`: returns open `NetworkStream` to `Host:Port`
- `SendRaw(byte[] request) → byte[]`: writes request, reads until EOF or disconnect

**`KestrelH2Fixture.cs`** — same but `HttpProtocols.Http2` (h2c cleartext)

**`TestRoutes.cs`** — static helper `MapAllRoutes(WebApplication app)`:
```
GET  /ping                   → 200 "pong"
GET  /hello                  → 200 "Hello World"  text/plain
POST /echo                   → 200, echoes body, mirrors Content-Type
POST /echo/chunked           → 200, echoes body, Transfer-Encoding: chunked
GET  /headers/echo           → 200, echoes all X-* request headers as response headers
GET  /headers/set            → 200, sets headers from query string ?name=value
GET  /status/{code:int}      → {code} (no body)
GET  /status/{code:int}/body → {code} + body "status {code}"
GET  /large/{kb:int}         → 200, body = {kb} KB of repeating bytes
GET  /chunked/{kb:int}       → 200 Transfer-Encoding:chunked, {kb} KB
GET  /redirect/{code:int}    → {code} Location:/hello
GET  /redirect/chain         → 301 → 302 → /hello
GET  /content/{type}         → 200 Content-Type:{type}, body = "body"
GET  /auth                   → 200 if Authorization present, else 401
GET  /date                   → 200 Date: header
GET  /etag                   → 200 ETag: "abc123"
GET  /conditional            → 200 or 304 depending on If-None-Match
GET  /range                  → 206 if Range header present, else 200
GET  /slow/{ms:int}          → waits {ms} ms, then 200
GET  /close                  → 200 Connection:close
GET  /keepalive              → 200 Connection:keep-alive
GET  /methods                → 200 body = request method name
HEAD /hello                  → 200 no body
OPTIONS /hello               → 200 Allow: GET,HEAD,POST,OPTIONS
DELETE /resource             → 204
PUT  /resource               → 201 or 200
PATCH /resource              → 200
GET  /error                  → 500 "Internal Server Error"
GET  /gzip                   → 200 Content-Encoding:gzip (pre-gzipped body)
GET  /trailer                → 200 chunked + Trailer: X-Checksum
```

## Verification

```bash
# Build everything
dotnet build --configuration Release ./src/TurboHttp.sln

# Run integration tests (fast tests only, no Stress category)
dotnet test ./src/TurboHttp.IntegrationTests/TurboHttp.IntegrationTests.csproj \
  --filter "Category!=Stress"

# Run stress tests separately
dotnet test ./src/TurboHttp.IntegrationTests/TurboHttp.IntegrationTests.csproj \
  --filter "Category=Stress"

# Full suite — all unit + integration (must be zero regressions)
dotnet test ./src/TurboHttp.sln

# Benchmarks (dry-run validation, no timing assertions)
dotnet run --configuration Release --project ./src/TurboHttp.Benchmarks -- \
  --job dry --filter "*"
```