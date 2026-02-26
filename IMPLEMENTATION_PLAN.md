# Client Encoder/Decoder — Production-Ready RFC Implementation Plan

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

- [ ] `7230-enc-001` **P0** — Request-line uses `HTTP/1.1` · `[DisplayName("7230-enc-001: Request-line uses HTTP/1.1")]`
- [ ] `7230-3.1.1-002` **P0** — Lowercase method causes exception · `[DisplayName("7230-3.1.1-002: Lowercase method rejected by HTTP/1.1 encoder")]`
- [ ] `7230-3.1.1-004` **P0** — Every request-line ends with `\r\n` · `[DisplayName("7230-3.1.1-004: Every request-line ends with CRLF")]`
- [ ] `enc3-m-001` **P0** `[T]` — All 9 HTTP methods (GET/POST/PUT/DELETE/PATCH/HEAD/OPTIONS/TRACE/CONNECT) · `[DisplayName("enc3-m-001: All HTTP methods produce correct request-line [{method}]")]`
- [ ] `enc3-uri-001` **P0** — `OPTIONS * HTTP/1.1` encoded correctly · `[DisplayName("enc3-uri-001: OPTIONS * HTTP/1.1 encoded correctly")]`
- [ ] `enc3-uri-002` **P0** — Absolute-URI preserved for proxy request · `[DisplayName("enc3-uri-002: Absolute-URI preserved for proxy request")]`
- [ ] `enc3-uri-003` **P0** — Missing path normalized to `/` · `[DisplayName("enc3-uri-003: Missing path normalized to /")]`
- [ ] `enc3-uri-004` **P0** — Query string preserved verbatim · `[DisplayName("enc3-uri-004: Query string preserved verbatim")]`
- [ ] `enc3-uri-005` **P0** — Fragment stripped from request-target · `[DisplayName("enc3-uri-005: Fragment stripped from request-target")]`
- [ ] `enc3-uri-006` **P1** — Existing percent-encoding not re-encoded · `[DisplayName("enc3-uri-006: Existing percent-encoding not re-encoded")]`

### Mandatory Host Header

- [ ] `9112-enc-001` **P0** — `Host:` always present · `[DisplayName("RFC 9112 §5.4: Host header mandatory in HTTP/1.1")]`
- [ ] `9112-enc-002` **P0** — `Host:` emitted exactly once · `[DisplayName("RFC 9112 §5.4: Host header emitted exactly once")]`
- [ ] `enc3-host-001` **P0** — Non-standard port included in `Host` value · `[DisplayName("enc3-host-001: Host with non-standard port includes port")]`
- [ ] `enc3-host-002` **P0** — IPv6 host literal bracketed: `Host: [::1]` · `[DisplayName("enc3-host-002: IPv6 host literal bracketed correctly")]`
- [ ] `enc3-host-003` **P0** — Default port 80 omitted from `Host` header · `[DisplayName("enc3-host-003: Default port 80 omitted from Host header")]`

### Header Encoding (RFC 7230 §3.2)

- [ ] `7230-3.2-001` **P0** — Header format is `Name: SP value CRLF` · `[DisplayName("7230-3.2-001: Header field format is Name: SP value CRLF")]`
- [ ] `7230-3.2-002` **P0** — No spurious whitespace added to header values · `[DisplayName("7230-3.2-002: No spurious whitespace added to header values")]`
- [ ] `7230-3.2-007` **P0** — Header name casing preserved (not lowercased) · `[DisplayName("7230-3.2-007: Header name casing preserved in output")]`
- [ ] `enc3-hdr-001` **P0** — NUL byte in header value throws `ArgumentException` · `[DisplayName("enc3-hdr-001: NUL byte in header value throws exception")]`
- [ ] `enc3-hdr-002` **P1** — `Content-Type` with semicolon parameters preserved · `[DisplayName("enc3-hdr-002: Content-Type with charset parameter preserved")]`
- [ ] `enc3-hdr-003` **P1** — All custom headers appear in output · `[DisplayName("enc3-hdr-003: All custom headers appear in output")]`
- [ ] `enc3-hdr-004` **P1** — `Accept-Encoding: gzip, deflate` encoded · `[DisplayName("enc3-hdr-004: Accept-Encoding gzip,deflate encoded")]`
- [ ] `enc3-hdr-005` **P1** — `Authorization: Bearer …` preserved verbatim · `[DisplayName("enc3-hdr-005: Authorization header preserved verbatim")]`

### Connection Management

- [ ] `7230-enc-003` **P0** — `Connection: keep-alive` default · `[DisplayName("7230-enc-003: Connection keep-alive default in HTTP/1.1")]`
- [ ] `7230-enc-004` **P0** — `Connection: close` when explicitly set · `[DisplayName("7230-enc-004: Connection close encoded when set")]`
- [ ] `7230-6.1-005` **P1** — Multiple `Connection` tokens encoded · `[DisplayName("7230-6.1-005: Multiple Connection tokens encoded")]`
- [ ] `9112-enc-003` **P0** — `TE`, `Trailers`, `Keep-Alive` stripped · `[DisplayName("RFC 9112: Connection-specific headers stripped")]`

### Body Encoding

- [ ] `7230-enc-006` **P0** — No `Content-Length` for bodyless GET · `[DisplayName("7230-enc-006: No Content-Length for bodyless GET")]`
- [ ] `7230-enc-008` **P0** — `Content-Length` set for POST body · `[DisplayName("7230-enc-008: Content-Length set for POST body")]`
- [ ] `7230-enc-009` **P1** — `Transfer-Encoding: chunked` + correct chunks · `[DisplayName("7230-enc-009: Chunked Transfer-Encoding for streamed body")]`
- [ ] `enc3-body-001` **P0** `[T]` — POST/PUT/PATCH each get `Content-Length` (× 3) · `[DisplayName("enc3-body-001: {method} with body gets Content-Length [{method}]")]`
- [ ] `enc3-body-002` **P0** `[T]` — GET/HEAD/DELETE omit `Content-Length` (× 3) · `[DisplayName("enc3-body-002: {method} without body omits Content-Length [{method}]")]`
- [ ] `enc3-body-003` **P0** — `\r\n\r\n` separates headers from body · `[DisplayName("enc3-body-003: Empty line separates headers from body")]`
- [ ] `enc3-body-004` **P0** — Binary body with null bytes preserved · `[DisplayName("enc3-body-004: Binary body with null bytes preserved")]`
- [ ] `enc3-body-005` **P1** — Chunked body ends with `0\r\n\r\n` · `[DisplayName("enc3-body-005: Chunked body terminated with final 0-chunk")]`
- [ ] `enc3-body-006` **P0** — No `Content-Length` when chunked · `[DisplayName("enc3-body-006: Content-Length absent when Transfer-Encoding is chunked")]`

---

## Phase 4: HTTP/1.1 (RFC 9112 / RFC 7230) — Client Decoder

**File:** `src/TurboHttp.Tests/Http11DecoderTests.cs`

### Status-Line (RFC 7231 §6.1)

- [ ] `7231-6.1-002a` **P0** `[T]` — All 2xx codes (200,201,202,203,204,205,206,207 × 8) · `[DisplayName("7231-6.1-002: 2xx status code {code} parsed correctly")]`
- [ ] `7231-6.1-003a` **P0** `[T]` — All 3xx codes (300,301,302,303,304,307,308 × 7) · `[DisplayName("7231-6.1-003: 3xx status code {code} parsed correctly")]`
- [ ] `7231-6.1-004a` **P0** `[T]` — All 4xx codes (400,401,403,404,405,408,409,410,413,415,422,429 × 12) · `[DisplayName("7231-6.1-004: 4xx status code {code} parsed correctly")]`
- [ ] `7231-6.1-005a` **P0** `[T]` — All 5xx codes (500,501,502,503,504 × 5) · `[DisplayName("7231-6.1-005: 5xx status code {code} parsed correctly")]`
- [ ] `7231-6.1-001` **P0** — 1xx informational response has no body · `[DisplayName("7231-6.1-001: 1xx Informational response has no body")]`
- [ ] `dec4-1xx-001` **P0** `[T]` — All 1xx codes individually (100,101,102,103 × 4) · `[DisplayName("dec4-1xx-001: 1xx code {code} parsed with no body")]`
- [ ] `dec4-1xx-002` **P0** — 100 Continue before 200 OK decoded correctly · `[DisplayName("dec4-1xx-002: 100 Continue before 200 OK decoded correctly")]`
- [ ] `dec4-1xx-003` **P0** — Multiple 1xx interim responses then 200 · `[DisplayName("dec4-1xx-003: Multiple 1xx interim responses before 200")]`
- [ ] `7231-6.1-006` **P1** — Custom status code 599 parsed · `[DisplayName("7231-6.1-006: Custom status code 599 parsed")]`
- [ ] `7231-6.1-007` **P0** — Status > 599 rejected · `[DisplayName("7231-6.1-007: Status code >599 is a parse error")]`
- [ ] `7231-6.1-008` **P0** — Empty reason phrase valid · `[DisplayName("7231-6.1-008: Empty reason phrase is valid")]`

### Header Parsing (RFC 7230 §3.2)

- [ ] `7230-3.2-001` **P0** — Standard `Name: value\r\n` parsed · `[DisplayName("7230-3.2-001: Standard header field Name: value parsed")]`
- [ ] `7230-3.2-002` **P0** — OWS trimmed from header value · `[DisplayName("7230-3.2-002: OWS trimmed from header value")]`
- [ ] `7230-3.2-003` **P0** — Empty header value accepted · `[DisplayName("7230-3.2-003: Empty header value accepted")]`
- [ ] `7230-3.2-004` **P0** — Multiple same-name headers both accessible · `[DisplayName("7230-3.2-004: Multiple same-name headers both accessible")]`
- [ ] `7230-3.2-005` **P0** — Obs-fold rejected in HTTP/1.1 → `ObsoleteFoldingDetected` · `[DisplayName("7230-3.2-005: Obs-fold rejected in HTTP/1.1")]`
- [ ] `7230-3.2-006` **P0** — Header without colon → `InvalidHeader` · `[DisplayName("7230-3.2-006: Header without colon is parse error")]`
- [ ] `7230-3.2-007` **P0** — Case-insensitive header name lookup · `[DisplayName("7230-3.2-007: Header name lookup case-insensitive")]`
- [ ] `7230-3.2-008` **P0** — Space in header name → `InvalidFieldName` · `[DisplayName("7230-3.2-008: Space in header name is parse error")]`
- [ ] `dec4-hdr-001` **P1** — Tab in header value accepted · `[DisplayName("dec4-hdr-001: Tab character in header value accepted")]`
- [ ] `dec4-hdr-002` **P0** — Quoted-string header value parsed · `[DisplayName("dec4-hdr-002: Quoted-string header value parsed")]`
- [ ] `dec4-hdr-003` **P0** — `Content-Type` with parameters parsed · `[DisplayName("dec4-hdr-003: Content-Type: text/html; charset=utf-8 accessible")]`

### Message Body (RFC 7230 §3.3)

- [ ] `7230-3.3-001` **P0** — Content-Length body decoded to exact byte count · `[DisplayName("7230-3.3-001: Content-Length body decoded to exact byte count")]`
- [ ] `7230-3.3-002` **P0** — Zero Content-Length → empty body · `[DisplayName("7230-3.3-002: Zero Content-Length produces empty body")]`
- [ ] `7230-3.3-003` **P0** — Chunked response body decoded · `[DisplayName("7230-3.3-003: Chunked response body decoded correctly")]`
- [ ] `7230-3.3-004` **P0** — Transfer-Encoding chunked takes priority over CL · `[DisplayName("7230-3.3-004: Transfer-Encoding chunked takes priority over CL")]`
- [ ] `7230-3.3-005` **P0** — Multiple Content-Length values rejected · `[DisplayName("7230-3.3-005: Multiple Content-Length values rejected")]`
- [ ] `7230-3.3-006` **P0** — Negative Content-Length rejected · `[DisplayName("7230-3.3-006: Negative Content-Length is parse error")]`
- [ ] `7230-3.3-007` **P0** — Response without body framing has empty body (204/304) · `[DisplayName("7230-3.3-007: Response without body framing has empty body")]`
- [ ] `dec4-body-001` **P0** — 10 MB body decoded correctly · `[DisplayName("dec4-body-001: 10 MB body decoded with correct Content-Length")]`
- [ ] `dec4-body-002` **P0** — Binary body with null bytes intact · `[DisplayName("dec4-body-002: Binary body with null bytes intact")]`

### Chunked Transfer Encoding (RFC 7230 §4.1)

- [ ] `7230-4.1-001` **P0** — Single chunk decoded: `5\r\nHello\r\n0\r\n\r\n` → `Hello` · `[DisplayName("7230-4.1-001: Single chunk body decoded")]`
- [ ] `7230-4.1-002` **P0** — Multiple chunks concatenated correctly · `[DisplayName("7230-4.1-002: Multiple chunks concatenated")]`
- [ ] `7230-4.1-003` **P1** — Chunk extensions silently ignored · `[DisplayName("7230-4.1-003: Chunk extension silently ignored")]`
- [ ] `7230-4.1-004` **P1** — Trailer fields after final chunk accessible · `[DisplayName("7230-4.1-004: Trailer fields after final chunk")]`
- [ ] `7230-4.1-005` **P0** — Non-hex chunk size → `InvalidChunkedEncoding` · `[DisplayName("7230-4.1-005: Non-hex chunk size is parse error")]`
- [ ] `7230-4.1-006` **P0** — Missing final chunk → `NeedMoreData` / `Incomplete()` · `[DisplayName("7230-4.1-006: Missing final chunk is NeedMoreData")]`
- [ ] `7230-4.1-007` **P0** — `0\r\n\r\n` terminates chunked body · `[DisplayName("7230-4.1-007: 0\\r\\n\\r\\n terminates chunked body")]`
- [ ] `7230-4.1-008` **P0** — Chunk size overflow → parse error · `[DisplayName("7230-4.1-008: Chunk size overflow is parse error")]`
- [ ] `dec4-chk-001` **P0** — 1-byte chunk decoded: `1\r\nX\r\n0\r\n\r\n` → `X` · `[DisplayName("dec4-chk-001: 1-byte chunk decoded")]`
- [ ] `dec4-chk-002` **P0** — Uppercase hex chunk size accepted · `[DisplayName("dec4-chk-002: Uppercase hex chunk size accepted")]`
- [ ] `dec4-chk-003` **P1** — Empty chunk before terminator accepted · `[DisplayName("dec4-chk-003: Empty chunk (0 data bytes) before terminator accepted")]`

### No-Body Responses

- [ ] `7230-nb-001` **P0** — 204 No Content: empty body · `[DisplayName("RFC 7230: 204 No Content has empty body")]`
- [ ] `7230-nb-002` **P0** — 304 Not Modified: empty body · `[DisplayName("RFC 7230: 304 Not Modified has empty body")]`
- [ ] `dec4-nb-001` **P0** `[T]` — 204/205/304 always empty body (× 3) · `[DisplayName("dec4-nb-001: Status {code} always has empty body")]`
- [ ] `dec4-nb-002` **P0** — HEAD response: `Content-Length` present, no body bytes · `[DisplayName("dec4-nb-002: HEAD response has Content-Length header but empty body")]`

### Connection Semantics (RFC 7230 §6.1)

- [ ] `7230-6.1-001` **P0** — `Connection: close` signals connection close · `[DisplayName("7230-6.1-001: Connection: close signals connection close")]`
- [ ] `7230-6.1-002` **P1** — `Connection: keep-alive` signals reuse · `[DisplayName("7230-6.1-002: Connection: keep-alive signals reuse")]`
- [ ] `7230-6.1-003` **P0** — HTTP/1.1 default connection is keep-alive · `[DisplayName("7230-6.1-003: HTTP/1.1 default connection is keep-alive")]`
- [ ] `7230-6.1-004` **P0** — HTTP/1.0 default connection is close · `[DisplayName("7230-6.1-004: HTTP/1.0 connection defaults to close")]`
- [ ] `7230-6.1-005` **P1** — Multiple `Connection` tokens all recognized · `[DisplayName("7230-6.1-005: Multiple Connection tokens all recognized")]`

### Date/Time Parsing (RFC 7231 §7.1.1.1)

- [ ] `7231-7.1.1-001` **P1** — IMF-fixdate parsed to `DateTimeOffset` · `[DisplayName("7231-7.1.1-001: IMF-fixdate Date header parsed")]`
- [ ] `7231-7.1.1-002` **P1** — RFC 850 obsolete format accepted · `[DisplayName("7231-7.1.1-002: RFC 850 Date format accepted")]`
- [ ] `7231-7.1.1-003` **P1** — ANSI C asctime format accepted · `[DisplayName("7231-7.1.1-003: ANSI C asctime Date format accepted")]`
- [ ] `7231-7.1.1-004` **P1** — Non-GMT timezone rejected or ignored · `[DisplayName("7231-7.1.1-004: Non-GMT timezone in Date rejected")]`
- [ ] `7231-7.1.1-005` **P1** — Invalid Date value handled gracefully · `[DisplayName("7231-7.1.1-005: Invalid Date header value rejected")]`

### Pipelining

- [ ] `7230-pipe-001` **P1** — Two pipelined responses decoded · `[DisplayName("RFC 7230: Two pipelined responses decoded")]`
- [ ] `7230-pipe-002` **P1** — Partial second response buffered as remainder · `[DisplayName("RFC 7230: Partial second response held in remainder")]`
- [ ] `dec4-pipe-001` **P1** — Three pipelined responses decoded in order · `[DisplayName("dec4-pipe-001: Three pipelined responses decoded in order")]`

### TCP Fragmentation (HTTP/1.1)

- [ ] `dec4-frag-001` **P0** — Status-line split at byte 1 reassembled · `[DisplayName("dec4-frag-001: Status-line split byte 1 reassembled")]`
- [ ] `dec4-frag-002` **P0** — Status-line split inside `HTTP/1.1` version · `[DisplayName("dec4-frag-002: Status-line split inside HTTP/1.1 version")]`
- [ ] `dec4-frag-003` **P0** — Header split at colon · `[DisplayName("dec4-frag-003: Header name:value split at colon")]`
- [ ] `dec4-frag-004` **P0** — Split at `\r\n\r\n` header-body boundary · `[DisplayName("dec4-frag-004: Split at CRLFCRLF header-body boundary")]`
- [ ] `dec4-frag-005` **P0** — Chunk-size line split across two reads · `[DisplayName("dec4-frag-005: Chunk-size line split across two reads")]`
- [ ] `dec4-frag-006` **P0** — Chunk data split mid-content · `[DisplayName("dec4-frag-006: Chunk data split mid-content")]`
- [ ] `dec4-frag-007` **P0** — Response delivered 1 byte at a time assembles correctly · `[DisplayName("dec4-frag-007: Response delivered 1 byte at a time assembles correctly")]`

---

## Phase 4b: Range Requests (RFC 7233) — Encoder + Decoder

**Files:** `Http11EncoderTests.cs`, `Http11DecoderTests.cs`

### Range Header Encoding (RFC 7233 §2.1)

- [ ] `7233-2.1-001` **P2** — `Range: bytes=0-499` encoded · `[DisplayName("7233-2.1-001: Range: bytes=0-499 encoded")]`
- [ ] `7233-2.1-002` **P2** — Suffix range `bytes=-500` encoded · `[DisplayName("7233-2.1-002: Range: bytes=-500 suffix encoded")]`
- [ ] `7233-2.1-003` **P2** — Open-ended range `bytes=500-` encoded · `[DisplayName("7233-2.1-003: Range: bytes=500- open range encoded")]`
- [ ] `7233-2.1-004` **P2** — Multi-range comma-separated encoded · `[DisplayName("7233-2.1-004: Multi-range bytes=0-499,1000-1499 encoded")]`
- [ ] `7233-2.1-005` **P2** — Invalid range rejected · `[DisplayName("7233-2.1-005: Invalid range bytes=abc-xyz rejected")]`

### 206 Partial Content Decoding (RFC 7233 §4.1)

- [ ] `7233-4.1-001` **P2** — `Content-Range: bytes 0-499/1000` parsed · `[DisplayName("7233-4.1-001: Content-Range: bytes 0-499/1000 accessible")]`
- [ ] `7233-4.1-002` **P2** — 206 Partial Content with Content-Range decoded · `[DisplayName("7233-4.1-002: 206 Partial Content with Content-Range decoded")]`
- [ ] `7233-4.1-003` **P2** — `multipart/byteranges` body decoded · `[DisplayName("7233-4.1-003: 206 multipart/byteranges body decoded")]`
- [ ] `7233-4.1-004` **P2** — Unknown total length (`*`) accepted · `[DisplayName("7233-4.1-004: Content-Range: bytes 0-499/* unknown total")]`

---

## Phase 5: HTTP/2 (RFC 7540) — Client Encoder

**File:** `src/TurboHttp.Tests/Http2EncoderTests.cs`

### Connection Preface (RFC 7540 §3.5)

- [ ] `7540-3.5-001` **P0** — Client preface bytes = `PRI * HTTP/2.0\r\n\r\nSM\r\n\r\n` · `[DisplayName("7540-3.5-001: Client preface is PRI * HTTP/2.0 SM")]`
- [ ] `7540-3.5-003` **P0** — SETTINGS frame immediately follows client preface · `[DisplayName("7540-3.5-003: SETTINGS frame immediately follows client preface")]`

### Pseudo-Headers (RFC 7540 §8.1.2)

- [ ] `7540-8.1-001` **P0** — All four pseudo-headers emitted (`:method` `:scheme` `:authority` `:path`) · `[DisplayName("7540-8.1-001: All four pseudo-headers emitted")]`
- [ ] `7540-8.1-002` **P0** — Pseudo-headers precede all regular headers · `[DisplayName("7540-8.1-002: Pseudo-headers precede regular headers")]`
- [ ] `7540-8.1-003` **P0** — No duplicate pseudo-headers · `[DisplayName("7540-8.1-003: No duplicate pseudo-headers")]`
- [ ] `7540-8.1-004` **P0** — `Connection`, `Keep-Alive`, `Upgrade` absent in HTTP/2 · `[DisplayName("7540-8.1-004: Connection-specific headers absent in HTTP/2")]`
- [ ] `enc5-ph-001` **P0** `[T]` — `:method` correct for all HTTP methods (× 9) · `[DisplayName("enc5-ph-001: :method pseudo-header correct for [{method}]")]`
- [ ] `enc5-ph-002` **P0** — `:scheme http` and `:scheme https` reflect URI scheme · `[DisplayName("enc5-ph-002: :scheme reflects request URI scheme")]`

### SETTINGS Frame (RFC 7540 §6.5)

- [ ] `enc5-set-001` **P0** `[T]` — All 6 SETTINGS parameters encoded correctly (× 6) · `[DisplayName("enc5-set-001: SETTINGS parameter {param} encoded correctly")]`
- [ ] `enc5-set-002` **P0** — SETTINGS ACK has type=`0x04` flags=`0x01` stream=0 · `[DisplayName("enc5-set-002: SETTINGS ACK frame has type=0x04 flags=0x01 stream=0")]`

### Stream IDs (RFC 7540 §5.1)

- [ ] `7540-5.1-001` **P0** — First request uses stream ID 1 · `[DisplayName("7540-5.1-001: First request uses stream ID 1")]`
- [ ] `7540-5.1-002` **P0** — Stream IDs increment as 1, 3, 5, … · `[DisplayName("7540-5.1-002: Stream IDs increment (1,3,5,...)")]`
- [ ] `enc5-sid-001` **P0** — Client never produces even stream IDs · `[DisplayName("enc5-sid-001: Client never produces even stream IDs")]`
- [ ] `enc5-sid-002` **P1** — Stream ID near 2^31 handled gracefully · `[DisplayName("enc5-sid-002: Stream ID approaching 2^31 handled gracefully")]`

### HEADERS Frame (RFC 7540 §6.2)

- [ ] `7540-6.2-001` **P0** — HEADERS frame has correct 9-byte header, type `0x01` · `[DisplayName("7540-6.2-001: HEADERS frame has correct 9-byte header and payload")]`
- [ ] `7540-6.2-002` **P0** — END_STREAM set on HEADERS for GET (bodyless) · `[DisplayName("7540-6.2-002: END_STREAM flag set on HEADERS for GET")]`
- [ ] `7540-6.2-003` **P0** — END_HEADERS set on single HEADERS frame · `[DisplayName("7540-6.2-003: END_HEADERS flag set on single HEADERS frame")]`

### CONTINUATION Frames (RFC 7540 §6.9)

- [ ] `7540-6.9-001` **P0** — Headers exceeding max frame size split into CONTINUATION · `[DisplayName("7540-6.9-001: Headers exceeding max frame size split into CONTINUATION")]`
- [ ] `7540-6.9-002` **P0** — END_HEADERS on final CONTINUATION frame · `[DisplayName("7540-6.9-002: END_HEADERS on final CONTINUATION frame")]`
- [ ] `7540-6.9-003` **P1** — Multiple CONTINUATION frames for very large headers · `[DisplayName("7540-6.9-003: Multiple CONTINUATION frames for very large headers")]`

### DATA Frames (RFC 7540 §6.1)

- [ ] `7540-6.1-002enc` **P0** — END_STREAM set on final DATA frame · `[DisplayName("7540-6.1-enc-002: END_STREAM set on final DATA frame")]`
- [ ] `7540-6.1-003enc` **P0** — GET uses END_STREAM on HEADERS, no DATA · `[DisplayName("7540-6.1-enc-003: GET END_STREAM on HEADERS frame")]`
- [ ] `enc5-data-001` **P0** — DATA frame type byte = `0x00` · `[DisplayName("enc5-data-001: DATA frame has type byte 0x00")]`
- [ ] `enc5-data-002` **P0** — DATA frame carries correct stream ID · `[DisplayName("enc5-data-002: DATA frame carries correct stream ID")]`
- [ ] `enc5-data-003` **P1** — Body > MAX_FRAME_SIZE split into multiple DATA frames · `[DisplayName("enc5-data-003: Body exceeding MAX_FRAME_SIZE split into multiple DATA frames")]`

### Flow Control — Encoder Side (RFC 7540 §5.2)

- [ ] `7540-5.2-001enc` **P0** — Encoder does not exceed initial 65535-byte window · `[DisplayName("7540-5.2-enc-001: Encoder does not exceed initial 65535-byte window")]`
- [ ] `7540-5.2-002enc` **P0** — WINDOW_UPDATE allows more DATA to be sent · `[DisplayName("7540-5.2-enc-002: WINDOW_UPDATE allows more DATA to be sent")]`
- [ ] `7540-5.2-005enc` **P0** — Encoder blocks when window is zero · `[DisplayName("7540-5.2-enc-005: Encoder blocks when window is zero")]`
- [ ] `7540-5.2-006enc` **P0** — Connection-level window limits total DATA · `[DisplayName("7540-5.2-enc-006: Connection-level window limits total DATA")]`
- [ ] `7540-5.2-007enc` **P0** — Per-stream window limits DATA on that stream · `[DisplayName("7540-5.2-enc-007: Per-stream window limits DATA on that stream")]`

---

## Phase 6: HTTP/2 (RFC 7540) — Client Decoder

**File:** `src/TurboHttp.Tests/Http2DecoderTests.cs`

### Connection Preface (RFC 7540 §3.5)

- [ ] `7540-3.5-002` **P0** — Invalid server preface → `PROTOCOL_ERROR` · `[DisplayName("7540-3.5-002: Invalid server preface causes PROTOCOL_ERROR")]`
- [ ] `7540-3.5-004` **P0** — Missing SETTINGS after preface → error · `[DisplayName("7540-3.5-004: Missing SETTINGS after preface causes error")]`

### Frame Header (RFC 7540 §4.1)

- [ ] `7540-4.1-001` **P0** — Valid 9-byte frame header decoded correctly · `[DisplayName("7540-4.1-001: Valid 9-byte frame header decoded correctly")]`
- [ ] `7540-4.1-002` **P0** — 24-bit length field parsed (lengths > 65535) · `[DisplayName("7540-4.1-002: Frame length uses 24-bit field")]`
- [ ] `7540-4.1-003` **P0** `[T]` — All frame types 0x00–0x09 dispatched (× 10) · `[DisplayName("7540-4.1-003: Frame type {type} dispatched to correct handler")]`
- [ ] `7540-4.1-004` **P0** — Unknown frame type 0x0A ignored · `[DisplayName("7540-4.1-004: Unknown frame type 0x0A is ignored")]`
- [ ] `7540-4.1-005` **P0** — R-bit masked out when reading stream ID · `[DisplayName("7540-4.1-005: R-bit masked out when reading stream ID")]`
- [ ] `7540-4.1-006` **P0** — R-bit set → `PROTOCOL_ERROR` · `[DisplayName("7540-4.1-006: R-bit set in stream ID causes PROTOCOL_ERROR")]`
- [ ] `7540-4.1-007` **P0** — Frame > MAX_FRAME_SIZE → `FRAME_SIZE_ERROR` · `[DisplayName("7540-4.1-007: Oversized frame causes FRAME_SIZE_ERROR")]`

### All 14 HTTP/2 Error Codes (RFC 7540 §7)

- [ ] `7540-err-000` **P0** — `NO_ERROR (0x0)` in GOAWAY decoded · `[DisplayName("7540-err-000: NO_ERROR (0x0) in GOAWAY decoded")]`
- [ ] `7540-err-001` **P0** — `PROTOCOL_ERROR (0x1)` in RST_STREAM decoded · `[DisplayName("7540-err-001: PROTOCOL_ERROR (0x1) in RST_STREAM decoded")]`
- [ ] `7540-err-002` **P0** — `INTERNAL_ERROR (0x2)` in GOAWAY decoded · `[DisplayName("7540-err-002: INTERNAL_ERROR (0x2) in GOAWAY decoded")]`
- [ ] `7540-err-003` **P0** — `FLOW_CONTROL_ERROR (0x3)` in GOAWAY decoded · `[DisplayName("7540-err-003: FLOW_CONTROL_ERROR (0x3) in GOAWAY decoded")]`
- [ ] `7540-err-004` **P0** — `SETTINGS_TIMEOUT (0x4)` in GOAWAY decoded · `[DisplayName("7540-err-004: SETTINGS_TIMEOUT (0x4) in GOAWAY decoded")]`
- [ ] `7540-err-005` **P0** — `STREAM_CLOSED (0x5)` in RST_STREAM decoded · `[DisplayName("7540-err-005: STREAM_CLOSED (0x5) in RST_STREAM decoded")]`
- [ ] `7540-err-006` **P0** — `FRAME_SIZE_ERROR (0x6)` decoded · `[DisplayName("7540-err-006: FRAME_SIZE_ERROR (0x6) decoded")]`
- [ ] `7540-err-007` **P0** — `REFUSED_STREAM (0x7)` in RST_STREAM decoded · `[DisplayName("7540-err-007: REFUSED_STREAM (0x7) in RST_STREAM decoded")]`
- [ ] `7540-err-008` **P0** — `CANCEL (0x8)` in RST_STREAM decoded · `[DisplayName("7540-err-008: CANCEL (0x8) in RST_STREAM decoded")]`
- [ ] `7540-err-009` **P0** — `COMPRESSION_ERROR (0x9)` in GOAWAY decoded · `[DisplayName("7540-err-009: COMPRESSION_ERROR (0x9) in GOAWAY decoded")]`
- [ ] `7540-err-00a` **P1** — `CONNECT_ERROR (0xa)` decoded · `[DisplayName("7540-err-00a: CONNECT_ERROR (0xa) in RST_STREAM decoded")]`
- [ ] `7540-err-00b` **P1** — `ENHANCE_YOUR_CALM (0xb)` decoded · `[DisplayName("7540-err-00b: ENHANCE_YOUR_CALM (0xb) in GOAWAY decoded")]`
- [ ] `7540-err-00c` **P1** — `INADEQUATE_SECURITY (0xc)` decoded · `[DisplayName("7540-err-00c: INADEQUATE_SECURITY (0xc) decoded")]`
- [ ] `7540-err-00d` **P0** — `HTTP_1_1_REQUIRED (0xd)` in GOAWAY decoded · `[DisplayName("7540-err-00d: HTTP_1_1_REQUIRED (0xd) in GOAWAY decoded")]`

### Stream States (RFC 7540 §5.1)

- [ ] `7540-5.1-003` **P0** — END_STREAM on incoming DATA → half-closed remote · `[DisplayName("7540-5.1-003: END_STREAM on incoming DATA moves stream to half-closed remote")]`
- [ ] `7540-5.1-004` **P0** — Both sides END_STREAM → stream fully closed · `[DisplayName("7540-5.1-004: Both sides END_STREAM closes stream")]`
- [ ] `7540-5.1-005` **P1** — PUSH_PROMISE → reserved remote state · `[DisplayName("7540-5.1-005: PUSH_PROMISE moves pushed stream to reserved remote")]`
- [ ] `7540-5.1-006` **P0** — DATA on closed stream → `STREAM_CLOSED` · `[DisplayName("7540-5.1-006: DATA on closed stream causes STREAM_CLOSED error")]`
- [ ] `7540-5.1-007` **P0** — Reusing closed stream ID → `PROTOCOL_ERROR` · `[DisplayName("7540-5.1-007: Reusing closed stream ID causes PROTOCOL_ERROR")]`
- [ ] `7540-5.1-008` **P0** — Even stream ID from client → `PROTOCOL_ERROR` · `[DisplayName("7540-5.1-008: Client even stream ID causes PROTOCOL_ERROR")]`

### Flow Control — Decoder Side (RFC 7540 §5.2)

- [ ] `7540-5.2-001dec` **P0** — New stream initial window = 65535 · `[DisplayName("7540-5.2-dec-001: New stream initial window is 65535")]`
- [ ] `7540-5.2-002dec` **P0** — WINDOW_UPDATE decoded, window updated · `[DisplayName("7540-5.2-dec-002: WINDOW_UPDATE decoded and window updated")]`
- [ ] `7540-5.2-003dec` **P0** — Peer DATA beyond window → `FLOW_CONTROL_ERROR` · `[DisplayName("7540-5.2-dec-003: Peer DATA beyond window causes FLOW_CONTROL_ERROR")]`
- [ ] `7540-5.2-004dec` **P0** — WINDOW_UPDATE overflow → `FLOW_CONTROL_ERROR` · `[DisplayName("7540-5.2-dec-004: WINDOW_UPDATE overflow causes FLOW_CONTROL_ERROR")]`
- [ ] `7540-5.2-008dec` **P0** — WINDOW_UPDATE increment=0 → `PROTOCOL_ERROR` · `[DisplayName("7540-5.2-dec-008: WINDOW_UPDATE increment=0 causes PROTOCOL_ERROR")]`

### DATA Frame (RFC 7540 §6.1)

- [ ] `7540-6.1-001` **P0** — DATA frame payload decoded correctly · `[DisplayName("7540-6.1-001: DATA frame payload decoded correctly")]`
- [ ] `7540-6.1-002` **P0** — END_STREAM on DATA marks stream complete · `[DisplayName("7540-6.1-002: END_STREAM on DATA marks stream closed")]`
- [ ] `7540-6.1-003` **P1** — PADDED DATA: padding stripped · `[DisplayName("7540-6.1-003: Padded DATA frame padding stripped")]`
- [ ] `7540-6.1-004` **P0** — DATA on stream 0 → `PROTOCOL_ERROR` · `[DisplayName("7540-6.1-004: DATA on stream 0 is PROTOCOL_ERROR")]`
- [ ] `7540-6.1-005` **P0** — DATA on closed stream → `STREAM_CLOSED` · `[DisplayName("7540-6.1-005: DATA on closed stream causes STREAM_CLOSED")]`
- [ ] `7540-6.1-006` **P0** — Empty DATA + END_STREAM: empty body, response complete · `[DisplayName("7540-6.1-006: Empty DATA frame with END_STREAM valid")]`

### HEADERS Frame (RFC 7540 §6.2)

- [ ] `7540-6.2-001` **P0** — HEADERS frame decoded into response headers · `[DisplayName("7540-6.2-001: HEADERS frame decoded into response headers")]`
- [ ] `7540-6.2-002` **P0** — END_STREAM on HEADERS closes stream immediately · `[DisplayName("7540-6.2-002: END_STREAM on HEADERS closes stream immediately")]`
- [ ] `7540-6.2-003` **P0** — END_HEADERS marks header block complete · `[DisplayName("7540-6.2-003: END_HEADERS on HEADERS marks complete block")]`
- [ ] `7540-6.2-004` **P1** — PADDED HEADERS: padding stripped · `[DisplayName("7540-6.2-004: Padded HEADERS padding stripped")]`
- [ ] `7540-6.2-005` **P1** — PRIORITY flag consumed correctly · `[DisplayName("7540-6.2-005: PRIORITY flag in HEADERS consumed correctly")]`
- [ ] `7540-6.2-006` **P0** — HEADERS without END_HEADERS waits for CONTINUATION · `[DisplayName("7540-6.2-006: HEADERS without END_HEADERS waits for CONTINUATION")]`
- [ ] `7540-6.2-007` **P0** — HEADERS on stream 0 → `PROTOCOL_ERROR` · `[DisplayName("7540-6.2-007: HEADERS on stream 0 is PROTOCOL_ERROR")]`

### CONTINUATION Frame (RFC 7540 §6.9)

- [ ] `7540-6.9-001` **P0** — CONTINUATION appended to HEADERS block · `[DisplayName("7540-6.9-001: CONTINUATION appended to HEADERS block")]`
- [ ] `7540-6.9-002dec` **P0** — END_HEADERS on final CONTINUATION completes block · `[DisplayName("7540-6.9-dec-002: END_HEADERS on final CONTINUATION completes block")]`
- [ ] `7540-6.9-003` **P0** — Multiple CONTINUATION frames all merged · `[DisplayName("7540-6.9-003: Multiple CONTINUATION frames all merged")]`
- [ ] `7540-6.9-004` **P0** — CONTINUATION on wrong stream → `PROTOCOL_ERROR` · `[DisplayName("7540-6.9-004: CONTINUATION on wrong stream is PROTOCOL_ERROR")]`
- [ ] `7540-6.9-005` **P0** — Non-CONTINUATION after HEADERS → `PROTOCOL_ERROR` · `[DisplayName("7540-6.9-005: Non-CONTINUATION after HEADERS is PROTOCOL_ERROR")]`
- [ ] `7540-6.9-006` **P0** — CONTINUATION on stream 0 → `PROTOCOL_ERROR` · `[DisplayName("7540-6.9-006: CONTINUATION on stream 0 is PROTOCOL_ERROR")]`
- [ ] `dec6-cont-001` **P0** — CONTINUATION without preceding HEADERS → `PROTOCOL_ERROR` · `[DisplayName("dec6-cont-001: CONTINUATION without HEADERS is PROTOCOL_ERROR")]`

### SETTINGS, PING, GOAWAY, RST_STREAM

- [ ] `7540-set-001` **P0** — Server SETTINGS decoded (`HasNewSettings = true`) · `[DisplayName("RFC 7540: Server SETTINGS decoded")]`
- [ ] `7540-set-002` **P0** — SETTINGS ACK generated after SETTINGS received · `[DisplayName("RFC 7540: SETTINGS ACK generated after SETTINGS")]`
- [ ] `7540-set-003` **P0** — MAX_FRAME_SIZE applied from SETTINGS · `[DisplayName("RFC 7540: MAX_FRAME_SIZE applied from SETTINGS")]`
- [ ] `dec6-set-001` **P0** `[T]` — All 6 SETTINGS parameters decoded (× 6) · `[DisplayName("dec6-set-001: SETTINGS parameter {param} decoded")]`
- [ ] `dec6-set-002` **P0** — SETTINGS ACK with payload → `FRAME_SIZE_ERROR` · `[DisplayName("dec6-set-002: SETTINGS ACK with non-empty payload is FRAME_SIZE_ERROR")]`
- [ ] `dec6-set-003` **P1** — Unknown SETTINGS ID accepted and ignored · `[DisplayName("dec6-set-003: Unknown SETTINGS parameter ID accepted and ignored")]`
- [ ] `7540-ping-001` **P1** — PING request from server decoded · `[DisplayName("RFC 7540: PING request from server decoded")]`
- [ ] `7540-ping-002` **P1** — PING ACK produced for server PING · `[DisplayName("RFC 7540: PING ACK produced for server PING")]`
- [ ] `dec6-ping-001` **P1** — PING ACK carries same 8 payload bytes · `[DisplayName("dec6-ping-001: PING ACK carries same 8 payload bytes as request")]`
- [ ] `7540-goaway-001` **P0** — GOAWAY decoded with last stream ID + error code · `[DisplayName("RFC 7540: GOAWAY with last stream ID and error code decoded")]`
- [ ] `7540-goaway-002` **P0** — No new streams accepted after GOAWAY · `[DisplayName("RFC 7540: No new requests after GOAWAY")]`
- [ ] `dec6-goaway-001` **P1** — GOAWAY debug data bytes accessible · `[DisplayName("dec6-goaway-001: GOAWAY debug data bytes accessible")]`
- [ ] `7540-rst-001` **P0** — RST_STREAM decoded (`RstStreams` entry present) · `[DisplayName("RFC 7540: RST_STREAM decoded")]`
- [ ] `7540-rst-002` **P0** — Stream closed after RST_STREAM · `[DisplayName("RFC 7540: Stream closed after RST_STREAM")]`

### TCP Fragmentation (HTTP/2)

- [ ] `dec6-frag-001` **P0** — Frame header split at byte 1 reassembled · `[DisplayName("dec6-frag-001: Frame header split at byte 1 reassembled")]`
- [ ] `dec6-frag-002` **P0** — Frame header split at byte 5 reassembled · `[DisplayName("dec6-frag-002: Frame header split at byte 5 reassembled")]`
- [ ] `dec6-frag-003` **P0** — DATA payload split across two reads · `[DisplayName("dec6-frag-003: DATA frame payload split across two reads")]`
- [ ] `dec6-frag-004` **P0** — HPACK block split across two reads · `[DisplayName("dec6-frag-004: HPACK block split across two reads")]`
- [ ] `dec6-frag-005` **P1** — Two complete frames in single read both decoded · `[DisplayName("dec6-frag-005: Two complete frames in single read both decoded")]`

---

## Phase 7: HPACK (RFC 7541) — Full Coverage

**File:** `src/TurboHttp.Tests/HpackTests.cs`

### All 61 Static Table Entries (RFC 7541 Appendix A)

- [ ] `7541-st-001` **P0** `[T]` — All 61 static table entries round-trip (indices 1–61 × 61) · `[DisplayName("7541-st-001: Static table entry {index} [{name}:{value}] round-trips as indexed representation")]`

### Sensitive Headers — NeverIndexed (RFC 7541 §7.1.3)

- [ ] `7541-ni-001` **P0** `[T]` — Sensitive headers use `0x10` NeverIndexed prefix (authorization, cookie, set-cookie, proxy-authorization × 4) · `[DisplayName("7541-ni-001: {header} encoded with NeverIndexed byte pattern (0x10)")]`
- [ ] `7541-ni-002` **P0** `[T]` — Sensitive headers do NOT grow the dynamic table (× 4) · `[DisplayName("7541-ni-002: {header} with NeverIndexed does not grow dynamic table")]`
- [ ] `7541-ni-003` **P0** — Decoded authorization header preserves NeverIndex flag · `[DisplayName("7541-ni-003: Decoded authorization header preserves NeverIndex flag")]`

### Dynamic Table (RFC 7541 §2.3)

- [ ] `7541-2.3-001` **P0** — Incrementally indexed header added at dynamic index 62 · `[DisplayName("7541-2.3-001: Incrementally indexed header added at dynamic index 62")]`
- [ ] `7541-2.3-002` **P0** — Oldest entry evicted when dynamic table is full · `[DisplayName("7541-2.3-002: Oldest entry evicted when dynamic table full")]`
- [ ] `7541-2.3-003` **P0** — Dynamic table resized on `SETTINGS_HEADER_TABLE_SIZE` · `[DisplayName("7541-2.3-003: Dynamic table resized on SETTINGS_HEADER_TABLE_SIZE")]`
- [ ] `7541-2.3-004` **P0** — Table size 0 evicts all entries · `[DisplayName("7541-2.3-004: Dynamic table size 0 evicts all entries")]`
- [ ] `7541-2.3-005` **P0** — Table size exceeding maximum → `HpackException` · `[DisplayName("7541-2.3-005: Table size exceeding maximum causes COMPRESSION_ERROR")]`
- [ ] `hpack-dt-001` **P0** — Entry size = name length + value length + 32 bytes · `[DisplayName("hpack-dt-001: Entry size counted as name + value + 32 overhead")]`
- [ ] `hpack-dt-002` **P0** — Size update prefix emitted before first header after resize · `[DisplayName("hpack-dt-002: Size update prefix emitted when table resized")]`
- [ ] `hpack-dt-003` **P0** — Three entries evicted in FIFO order · `[DisplayName("hpack-dt-003: Three entries evicted in FIFO order")]`

### Integer Representation (RFC 7541 §5.1)

- [ ] `7541-5.1-001` **P0** — Integer smaller than prefix limit encodes in one byte · `[DisplayName("7541-5.1-001: Integer smaller than prefix limit encodes in one byte")]`
- [ ] `7541-5.1-002` **P0** — Integer at prefix limit requires continuation bytes · `[DisplayName("7541-5.1-002: Integer at prefix limit requires continuation bytes")]`
- [ ] `7541-5.1-003` **P0** — Maximum integer 2147483647 round-trips exactly · `[DisplayName("7541-5.1-003: Maximum integer 2147483647 round-trips")]`
- [ ] `7541-5.1-004` **P0** — Integer exceeding 2^31-1 → `HpackException` · `[DisplayName("7541-5.1-004: Integer exceeding 2^31-1 causes COMPRESSION_ERROR")]`
- [ ] `hpack-int-001` **P0** `[T]` — Boundary values for 1–7 bit prefixes (× 7) · `[DisplayName("hpack-int-001: Integer encoding with {bits}-bit prefix")]`

### String Representation (RFC 7541 §5.2)

- [ ] `7541-5.2-001` **P0** — Plain string literal (H=0) decoded · `[DisplayName("7541-5.2-001: Plain string literal decoded")]`
- [ ] `7541-5.2-002` **P0** — Huffman-encoded string (H=1) decoded · `[DisplayName("7541-5.2-002: Huffman-encoded string decoded")]`
- [ ] `7541-5.2-003` **P0** — Empty string literal decoded · `[DisplayName("7541-5.2-003: Empty string literal decoded")]`
- [ ] `7541-5.2-004` **P1** — String larger than 8 KB decoded without truncation · `[DisplayName("7541-5.2-004: String larger than 8KB decoded")]`
- [ ] `7541-5.2-005` **P0** — Malformed Huffman data → `HpackException` · `[DisplayName("7541-5.2-005: Malformed Huffman data causes COMPRESSION_ERROR")]`
- [ ] `hpack-str-001` **P0** — Non-1 EOS padding bits → `HpackException` · `[DisplayName("hpack-str-001: Non-1 EOS padding bits cause COMPRESSION_ERROR")]`
- [ ] `hpack-str-002` **P0** — EOS padding > 7 bits → `HpackException` · `[DisplayName("hpack-str-002: EOS padding > 7 bits causes COMPRESSION_ERROR")]`

### Indexed Header Field (RFC 7541 §6.1)

- [ ] `7541-6.1-002` **P0** — Dynamic table entry at index 62+ retrieved · `[DisplayName("7541-6.1-002: Dynamic table entry at index 62+ retrieved")]`
- [ ] `7541-6.1-003` **P0** — Out-of-range index → `HpackException` · `[DisplayName("7541-6.1-003: Index out of range causes COMPRESSION_ERROR")]`
- [ ] `hpack-idx-001` **P0** — Index 0 is invalid → `HpackException` · `[DisplayName("hpack-idx-001: Index 0 is invalid per RFC 7541 §6.1")]`

### Literal Header Field (RFC 7541 §6.2)

- [ ] `7541-6.2-001` **P0** — Incremental indexing: entry added at index 62 · `[DisplayName("7541-6.2-001: Incremental indexing adds entry to dynamic table")]`
- [ ] `7541-6.2-002` **P0** — Without-indexing: NOT added to dynamic table · `[DisplayName("7541-6.2-002: Without-indexing literal not added to dynamic table")]`
- [ ] `7541-6.2-003` **P0** — Never-indexed: NOT added, flag preserved · `[DisplayName("7541-6.2-003: NeverIndexed literal not added to table")]`
- [ ] `7541-6.2-004` **P0** — Indexed name + literal value decoded · `[DisplayName("7541-6.2-004: Literal with indexed name and literal value decoded")]`
- [ ] `7541-6.2-005` **P0** — Both name and value as literals decoded · `[DisplayName("7541-6.2-005: Literal with literal name and literal value decoded")]`

### Appendix C — Byte-Exact RFC Vectors

- [ ] `7541-C.2-001` **P0** — Appendix C.2.1: first request, no Huffman · `[DisplayName("7541-C.2-001: RFC 7541 Appendix C.2.1 decode")]`
- [ ] `7541-C.2-002` **P0** — Appendix C.2.2: dynamic table first referenced entry · `[DisplayName("7541-C.2-002: RFC 7541 Appendix C.2.2 decode (dynamic table)")]`
- [ ] `7541-C.2-003` **P0** — Appendix C.2.3: third request, table state correct · `[DisplayName("7541-C.2-003: RFC 7541 Appendix C.2.3 decode")]`
- [ ] `7541-C.3-001` **P0** — Appendix C.3: requests with Huffman encoding · `[DisplayName("7541-C.3-001: RFC 7541 Appendix C.3 decode with Huffman")]`
- [ ] `7541-C.4-001` **P0** — Appendix C.4.1: response, no Huffman · `[DisplayName("7541-C.4-001: RFC 7541 Appendix C.4.1 decode")]`
- [ ] `7541-C.4-002` **P0** — Appendix C.4.2: response, dynamic table reused · `[DisplayName("7541-C.4-002: RFC 7541 Appendix C.4.2 decode (dynamic table reused)")]`
- [ ] `7541-C.4-003` **P0** — Appendix C.4.3: response, table state after C.4.2 · `[DisplayName("7541-C.4-003: RFC 7541 Appendix C.4.3 decode")]`
- [ ] `7541-C.5-001` **P0** — Appendix C.5: responses with Huffman · `[DisplayName("7541-C.5-001: RFC 7541 Appendix C.5 decode with Huffman")]`
- [ ] `7541-C.6-001` **P1** — Appendix C.6: large cookie responses · `[DisplayName("7541-C.6-001: RFC 7541 Appendix C.6 large cookie responses")]`

---

## Phase 8: Security & Limits

**New files:** `src/TurboHttp.Tests/Http11SecurityTests.cs`, `src/TurboHttp.Tests/Http2SecurityTests.cs`

### HTTP/1.1 Input Limits

- [ ] `sec-001a` **P0** — 100 headers accepted at default limit · `[DisplayName("SEC-001a: 100 headers accepted at default limit")]`
- [ ] `sec-001b` **P0** — 101 headers rejected above default limit · `[DisplayName("SEC-001b: 101 headers rejected above default limit")]`
- [ ] `sec-001c` **P1** — Custom header count limit respected · `[DisplayName("SEC-001c: Custom header count limit respected")]`
- [ ] `sec-002a` **P0** — 8191-byte header block accepted · `[DisplayName("SEC-002a: Header block below 8KB limit accepted")]`
- [ ] `sec-002b` **P0** — 8193-byte header block rejected · `[DisplayName("SEC-002b: Header block above 8KB limit rejected")]`
- [ ] `sec-002c` **P0** — Single 9000-byte header value rejected · `[DisplayName("SEC-002c: Single header value exceeding limit rejected")]`
- [ ] `sec-003a` **P0** — Body at 10 MB limit accepted · `[DisplayName("SEC-003a: Body at configurable limit accepted")]`
- [ ] `sec-003b` **P0** — Body exceeding 10 MB rejected · `[DisplayName("SEC-003b: Body exceeding limit rejected")]`
- [ ] `sec-003c` **P1** — Zero body limit rejects any body · `[DisplayName("SEC-003c: Zero body limit rejects any body")]`

### HTTP Smuggling

- [ ] `sec-005a` **P0** — `Transfer-Encoding` + `Content-Length` conflict rejected · `[DisplayName("SEC-005a: Transfer-Encoding + Content-Length rejected")]`
- [ ] `sec-005b` **P0** — CRLF injection in header value rejected → `InvalidFieldValue` · `[DisplayName("SEC-005b: CRLF injection in header value rejected")]`
- [ ] `sec-005c` **P0** — NUL byte in decoded header value rejected → `InvalidFieldValue` · `[DisplayName("SEC-005c: NUL byte in decoded header value rejected")]`

### State Isolation

- [ ] `sec-006a` **P0** — `Reset()` after partial headers restores clean state · `[DisplayName("SEC-006a: Reset() after partial headers restores clean state")]`
- [ ] `sec-006b` **P0** — `Reset()` after partial body restores clean state · `[DisplayName("SEC-006b: Reset() after partial body restores clean state")]`

### HTTP/2 Security

- [ ] `sec-h2-001` **P0** — HPACK literal name exceeding limit → `HpackException` · `[DisplayName("SEC-h2-001: HPACK literal name exceeding limit causes COMPRESSION_ERROR")]`
- [ ] `sec-h2-002` **P0** — HPACK literal value exceeding limit → `HpackException` · `[DisplayName("SEC-h2-002: HPACK literal value exceeding limit causes COMPRESSION_ERROR")]`
- [ ] `sec-h2-003` **P0** — Excessive CONTINUATION frames (1000) rejected · `[DisplayName("SEC-h2-003: Excessive CONTINUATION frames rejected")]`
- [ ] `sec-h2-004` **P1** — 100 streams immediately RST'd triggers protection (CVE-2023-44487) · `[DisplayName("SEC-h2-004: Rapid RST_STREAM cycling triggers protection (CVE-2023-44487)")]`
- [ ] `sec-h2-005` **P1** — 10000 zero-length DATA frames rejected · `[DisplayName("SEC-h2-005: Excessive zero-length DATA frames rejected")]`
- [ ] `sec-h2-006` **P0** — `SETTINGS_ENABLE_PUSH` > 1 → `PROTOCOL_ERROR` · `[DisplayName("SEC-h2-006: SETTINGS_ENABLE_PUSH value >1 causes PROTOCOL_ERROR")]`
- [ ] `sec-h2-007` **P0** — `SETTINGS_INITIAL_WINDOW_SIZE` > 2^31-1 → `FLOW_CONTROL_ERROR` · `[DisplayName("SEC-h2-007: SETTINGS_INITIAL_WINDOW_SIZE >2^31-1 causes FLOW_CONTROL_ERROR")]`
- [ ] `sec-h2-008` **P1** — Unknown SETTINGS ID silently ignored · `[DisplayName("SEC-h2-008: Unknown SETTINGS ID silently ignored")]`

---

## Phase 9: Round-Trip Tests — Encode → Decode

**New files:** `src/TurboHttp.Tests/Http11RoundTripTests.cs`, `src/TurboHttp.Tests/Http2RoundTripTests.cs`

### HTTP/1.1 Round-Trip

- [ ] `rt11-001` **P0** — GET → 200 OK round-trip · `[DisplayName("RT-11-001: HTTP/1.1 GET → 200 OK round-trip")]`
- [ ] `rt11-002` **P0** — POST JSON → 201 Created round-trip · `[DisplayName("RT-11-002: HTTP/1.1 POST JSON → 201 Created round-trip")]`
- [ ] `rt11-003` **P0** — PUT → 204 No Content round-trip · `[DisplayName("RT-11-003: HTTP/1.1 PUT → 204 No Content round-trip")]`
- [ ] `rt11-004` **P0** — DELETE → 200 OK round-trip · `[DisplayName("RT-11-004: HTTP/1.1 DELETE → 200 OK round-trip")]`
- [ ] `rt11-005` **P0** — PATCH → 200 OK round-trip · `[DisplayName("RT-11-005: HTTP/1.1 PATCH → 200 OK round-trip")]`
- [ ] `rt11-006` **P1** — HEAD → Content-Length but no body · `[DisplayName("RT-11-006: HTTP/1.1 HEAD → Content-Length but no body")]`
- [ ] `rt11-007` **P1** — OPTIONS → 200 with Allow header · `[DisplayName("RT-11-007: HTTP/1.1 OPTIONS → 200 with Allow header")]`
- [ ] `rt11-008` **P0** — GET → 200 chunked response round-trip · `[DisplayName("RT-11-008: HTTP/1.1 GET → 200 chunked response round-trip")]`
- [ ] `rt11-009` **P0** — GET → response with 5 chunks concatenated · `[DisplayName("RT-11-009: HTTP/1.1 GET → response with 5 chunks round-trip")]`
- [ ] `rt11-010` **P1** — Chunked response with trailer accessible · `[DisplayName("RT-11-010: HTTP/1.1 chunked response with trailer round-trip")]`
- [ ] `rt11-011` **P0** — GET → 301 with Location round-trip · `[DisplayName("RT-11-011: HTTP/1.1 GET → 301 with Location round-trip")]`
- [ ] `rt11-012` **P0** — POST binary → 200 binary response round-trip · `[DisplayName("RT-11-012: HTTP/1.1 POST binary → 200 binary response round-trip")]`
- [ ] `rt11-013` **P0** — GET → 404 Not Found round-trip · `[DisplayName("RT-11-013: HTTP/1.1 GET → 404 Not Found round-trip")]`
- [ ] `rt11-014` **P0** — GET → 500 Internal Server Error round-trip · `[DisplayName("RT-11-014: HTTP/1.1 GET → 500 Internal Server Error round-trip")]`
- [ ] `rt11-015` **P1** — Two pipelined requests and responses round-trip · `[DisplayName("RT-11-015: Two pipelined requests and responses round-trip")]`
- [ ] `rt11-016` **P0** — 100 Continue before 200 OK round-trip · `[DisplayName("RT-11-016: 100 Continue before 200 OK round-trip")]`
- [ ] `rt11-017` **P0** — 1 MB body round-trip · `[DisplayName("RT-11-017: HTTP/1.1 1 MB body round-trip")]`
- [ ] `rt11-018` **P0** — Binary body with null bytes preserved · `[DisplayName("RT-11-018: HTTP/1.1 binary body with null bytes round-trip")]`
- [ ] `rt11-019` **P1** — Two responses on keep-alive connection · `[DisplayName("RT-11-019: Two responses on keep-alive connection round-trip")]`
- [ ] `rt11-020` **P1** — `Content-Type: application/json; charset=utf-8` preserved · `[DisplayName("RT-11-020: Content-Type: application/json; charset=utf-8 preserved")]`

### HTTP/2 Round-Trip

- [ ] `rt2-001` **P0** — Connection preface + SETTINGS exchange · `[DisplayName("RT-2-001: HTTP/2 connection preface + SETTINGS exchange")]`
- [ ] `rt2-002` **P0** — GET → 200 on stream 1 · `[DisplayName("RT-2-002: HTTP/2 GET → 200 on stream 1")]`
- [ ] `rt2-003` **P0** — POST → HEADERS+DATA → 201 response · `[DisplayName("RT-2-003: HTTP/2 POST → HEADERS+DATA → 201 response")]`
- [ ] `rt2-004` **P0** — Three concurrent streams each complete independently · `[DisplayName("RT-2-004: HTTP/2 three concurrent streams each complete independently")]`
- [ ] `rt2-005` **P0** — HPACK dynamic table reused across three requests · `[DisplayName("RT-2-005: HTTP/2 HPACK dynamic table reused across three requests")]`
- [ ] `rt2-006` **P0** — Server SETTINGS → client ACK → both sides updated · `[DisplayName("RT-2-006: HTTP/2 server SETTINGS → client ACK → both sides updated")]`
- [ ] `rt2-007` **P1** — Server PING → client PONG with same payload · `[DisplayName("RT-2-007: HTTP/2 server PING → client PONG with same payload")]`
- [ ] `rt2-008` **P0** — GOAWAY received → no new requests sent · `[DisplayName("RT-2-008: HTTP/2 GOAWAY received → no new requests sent")]`
- [ ] `rt2-009` **P0** — RST_STREAM cancels stream, other streams continue · `[DisplayName("RT-2-009: HTTP/2 RST_STREAM → stream dropped, other streams continue")]`
- [ ] `rt2-010` **P0** — Authorization NeverIndexed preserved in round-trip · `[DisplayName("RT-2-010: Authorization header NeverIndexed in HTTP/2 round-trip")]`
- [ ] `rt2-011` **P1** — Cookie NeverIndexed preserved in round-trip · `[DisplayName("RT-2-011: Cookie header NeverIndexed in HTTP/2 round-trip")]`
- [ ] `rt2-012` **P0** — Large headers via CONTINUATION, all decoded · `[DisplayName("RT-2-012: HTTP/2 request with headers exceeding frame size uses CONTINUATION")]`
- [ ] `rt2-013` **P1** — Server PUSH_PROMISE decoded, pushed response received · `[DisplayName("RT-2-013: HTTP/2 server PUSH_PROMISE decoded, pushed response received")]`
- [ ] `rt2-014` **P0** — POST body larger than initial window uses WINDOW_UPDATE · `[DisplayName("RT-2-014: HTTP/2 POST body larger than initial window uses WINDOW_UPDATE")]`
- [ ] `rt2-015` **P1** — 404 response on stream decoded · `[DisplayName("RT-2-015: HTTP/2 request → 404 response on stream decoded")]`

---

## Phase 10: TCP Fragmentation — Systematic Matrix

**New file:** `src/TurboHttp.Tests/TcpFragmentationTests.cs`

> Pattern: feed bytes in two slices `data[..splitPoint]` and `data[splitPoint..]`.
> First call must return `NeedMoreData`. Second call returns complete response.

### HTTP/1.0 Fragmentation

- [ ] `frag10-001` **P0** — Status-line split at byte 1 · `[DisplayName("FRAG-10-001: HTTP/1.0 status-line split at byte 1")]`
- [ ] `frag10-002` **P0** — Status-line split at byte 8 (mid-version) · `[DisplayName("FRAG-10-002: HTTP/1.0 status-line split mid-version")]`
- [ ] `frag10-003` **P0** — Header name split mid-word · `[DisplayName("FRAG-10-003: HTTP/1.0 header name split mid-word")]`
- [ ] `frag10-004` **P0** — Body split at first byte · `[DisplayName("FRAG-10-004: HTTP/1.0 body split at first byte")]`
- [ ] `frag10-005` **P0** — Body split at midpoint · `[DisplayName("FRAG-10-005: HTTP/1.0 body split at midpoint")]`

### HTTP/1.1 Fragmentation

- [ ] `frag11-001` **P0** — Status-line split at byte 1 · `[DisplayName("FRAG-11-001: HTTP/1.1 status-line split at byte 1")]`
- [ ] `frag11-002` **P0** — Status-line split mid-version · `[DisplayName("FRAG-11-002: HTTP/1.1 status-line split inside version")]`
- [ ] `frag11-003` **P0** — Header split at colon · `[DisplayName("FRAG-11-003: HTTP/1.1 header split at colon")]`
- [ ] `frag11-004` **P0** — Header-body boundary split · `[DisplayName("FRAG-11-004: HTTP/1.1 split at first byte of CRLFCRLF")]`
- [ ] `frag11-005` **P0** — Chunked: chunk-size line split mid-hex · `[DisplayName("FRAG-11-005: HTTP/1.1 chunk-size line split mid-hex")]`
- [ ] `frag11-006` **P0** — Chunked: chunk data split mid-content · `[DisplayName("FRAG-11-006: HTTP/1.1 chunk data split mid-content")]`
- [ ] `frag11-007` **P0** — Chunked: final chunk split · `[DisplayName("FRAG-11-007: HTTP/1.1 final 0-chunk split")]`
- [ ] `frag11-008` **P0** — Single-byte delivery assembles complete response · `[DisplayName("FRAG-11-008: HTTP/1.1 response delivered 1 byte at a time")]`

### HTTP/2 Fragmentation

- [ ] `frag2-001` **P0** — Frame header split at byte 1 · `[DisplayName("FRAG-2-001: HTTP/2 frame header split at byte 1")]`
- [ ] `frag2-002` **P0** — Frame header split at byte 3 (end of length) · `[DisplayName("FRAG-2-002: HTTP/2 frame header split at byte 3 (end of length)")]`
- [ ] `frag2-003` **P0** — Frame header split at byte 5 (flags) · `[DisplayName("FRAG-2-003: HTTP/2 frame header split at byte 5 (flags)")]`
- [ ] `frag2-004` **P0** — Frame header split at byte 8 (last stream byte) · `[DisplayName("FRAG-2-004: HTTP/2 frame header split at byte 8 (last stream byte)")]`
- [ ] `frag2-005` **P0** — DATA payload split mid-content · `[DisplayName("FRAG-2-005: HTTP/2 DATA payload split mid-content")]`
- [ ] `frag2-006` **P0** — HPACK block split mid-stream · `[DisplayName("FRAG-2-006: HTTP/2 HEADERS HPACK block split mid-stream")]`
- [ ] `frag2-007` **P0** — Split between HEADERS and CONTINUATION frames · `[DisplayName("FRAG-2-007: HTTP/2 split between HEADERS and CONTINUATION frames")]`
- [ ] `frag2-008` **P0** — Two complete frames in one buffer both processed · `[DisplayName("FRAG-2-008: Two complete HTTP/2 frames in one read both processed")]`
- [ ] `frag2-009` **P0** — Second stream HEADERS split across reads · `[DisplayName("FRAG-2-009: Second stream's HEADERS split across reads while first stream active")]`

---

## Progress Tracker

```
Phase 1:  HTTP/1.0 Encoder                 [ 0/23 ] ░░░░░░░░░░
Phase 2:  HTTP/1.0 Decoder                 [ 0/32 ] ░░░░░░░░░░
Phase 3:  HTTP/1.1 Encoder                 [ 0/34 ] ░░░░░░░░░░
Phase 4:  HTTP/1.1 Decoder                 [ 0/57 ] ░░░░░░░░░░
Phase 4b: Range Requests (RFC 7233)        [ 0/9  ] ░░░░░░░░░░
Phase 5:  HTTP/2 Encoder                   [ 0/27 ] ░░░░░░░░░░
Phase 6:  HTTP/2 Decoder                   [ 0/57 ] ░░░░░░░░░░
Phase 7:  HPACK (RFC 7541 full)            [ 0/30 ] ░░░░░░░░░░
Phase 8:  Security & Limits                [ 0/17 ] ░░░░░░░░░░
Phase 9:  Round-Trip Tests                 [ 0/35 ] ░░░░░░░░░░
Phase 10: TCP Fragmentation                [ 0/22 ] ░░░░░░░░░░
────────────────────────────────────────────────────────────────
Total (list items)                         [ 0/343]
Note: [T] items with InlineData expand to more xUnit results.
With all [InlineData] rows: ~644 effective test results.
```

---

## New Test Files Required

| File | Phase | Purpose |
|------|-------|---------|
| `src/TurboHttp.Tests/Http11SecurityTests.cs` | 8 | HTTP/1.1 security limits and smuggling |
| `src/TurboHttp.Tests/Http2SecurityTests.cs` | 8 | HTTP/2 protocol protection |
| `src/TurboHttp.Tests/Http11RoundTripTests.cs` | 9 | HTTP/1.1 encode→decode end-to-end |
| `src/TurboHttp.Tests/Http2RoundTripTests.cs` | 9 | HTTP/2 encode→decode end-to-end |
| `src/TurboHttp.Tests/TcpFragmentationTests.cs` | 10 | Systematic TCP split-point matrix |