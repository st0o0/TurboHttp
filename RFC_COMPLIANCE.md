# RFC Compliance Matrix — TurboHttp

**Generated:** 2026-03-12 (TASK-048 Validation Gate)
**Total Tests:** 2803 (2158 unit + 411 stream + 234 integration)

## Summary

| RFC | Standard | Coverage | Unit Tests | Stream Tests | Integration Tests |
|-----|----------|----------|-----------|-------------|-------------------|
| RFC 1945 | HTTP/1.0 | 100% | 211 | ~60 | 46 |
| RFC 9112 | HTTP/1.1 Message Framing | 100% | 286 | ~80 | 89 |
| RFC 9113 | HTTP/2 | 100% | 659 | ~120 | 66 |
| RFC 7541 | HPACK Header Compression | 100% | 242 | (via H2 stages) | (via H2 integration) |
| RFC 9110 | HTTP Semantics | 100% | 41 | 37 | (via redirect/retry) |
| RFC 6265 | HTTP Cookies | 100% | (via Integration/) | 12 | (via cookie integration) |
| RFC 9111 | HTTP Caching | 100% | 66 | 24 | (via cache integration) |

## RFC 1945 — HTTP/1.0 (Encoder, Decoder, Round-Trip)

- **§4 Request/Response**: Request-line format, Status-line parsing
- **§5 Request Methods**: GET, HEAD, POST encoding
- **§6 Response**: Status codes, headers, body parsing
- **§10 Headers**: Content-Length, Content-Type, Date, Connection
- **Test files**: `TurboHttp.Tests/RFC1945/` (01–17), `StreamTests/Http10/`
- **Integration**: `IntegrationTests/Http10/` (basic, headers, body, redirect, cookie, retry, cache, encoding)

## RFC 9112 — HTTP/1.1 Message Framing

- **§2.1 Request-Line**: Method SP Request-Target SP HTTP-Version CRLF
- **§3 Status-Line**: HTTP-Version SP Status-Code SP Reason-Phrase CRLF
- **§5 Message Body**: Content-Length, message body determination
- **§7 Transfer-Encoding**: Chunked transfer coding, chunk-size, trailer
- **§7.1 Chunked Transfer**: Chunk extensions, last-chunk, trailer-section
- **§7.2 Host Header**: MUST be present in HTTP/1.1 requests
- **§9 Connection Management**: Keep-Alive, Close, connection reuse
- **§9.3 Pipelining**: FIFO request-response correlation
- **Test files**: `TurboHttp.Tests/RFC9112/` (01–21), `StreamTests/Http11/`
- **Integration**: `IntegrationTests/Http11/` (basic, headers, body, chunked, redirect, cookie, retry, cache, encoding, connection mgmt)

## RFC 9113 — HTTP/2

- **§3.4 Connection Preface**: Client preface (PRI * ... magic + SETTINGS)
- **§4 Framing**: 9-byte frame header, length/type/flags/stream-ID
- **§4.1 Frame Types**: DATA, HEADERS, PRIORITY, RST_STREAM, SETTINGS, PUSH_PROMISE, PING, GOAWAY, WINDOW_UPDATE, CONTINUATION
- **§5 Streams**: Stream states, stream ID allocation (odd for client)
- **§5.1 Flow Control**: WINDOW_UPDATE, initial window size, connection/stream level
- **§6.5 SETTINGS**: Parameter negotiation, ACK handling
- **§6.7 PING**: Round-trip measurement, ACK
- **§6.8 GOAWAY**: Graceful shutdown, last-stream-ID
- **§8 HTTP Message Exchanges**: Pseudo-headers (:method, :scheme, :authority, :path, :status)
- **Test files**: `TurboHttp.Tests/RFC9113/` (01–20), `StreamTests/Http20/`
- **Integration**: `IntegrationTests/Http20/` (basic, headers, body, redirect, cookie, retry, cache, encoding, connection mgmt)

## RFC 7541 — HPACK Header Compression

- **§2 Compression Process**: Encoder/decoder with dynamic table
- **§2.3 Dynamic Table**: FIFO eviction, 32-byte overhead per entry
- **§4 Integer Representation**: Variable-length encoding with prefix
- **§5 Huffman Coding**: Static Huffman table encoding/decoding
- **§6.1 Indexed Header Field**: Static/dynamic table lookup
- **§6.2 Literal Header Field**: With/without indexing, never-indexed
- **Sensitive headers**: Authorization, Cookie use NeverIndex automatically
- **Test files**: `TurboHttp.Tests/RFC7541/` (01–06 + HpackTests)

## RFC 9110 — HTTP Semantics

- **§9.2 Idempotent Methods**: GET, HEAD, PUT, DELETE retry-safe
- **§15.4 Redirections**: 301, 302, 303, 307, 308 with method rewriting
  - 301/302: GET/HEAD preserved, others → GET (303 semantics for legacy compat)
  - 303: Always → GET
  - 307/308: Method preserved
  - HTTPS→HTTP downgrade protection
  - Infinite loop detection, max redirect count
- **Content Negotiation**: Accept-Encoding (gzip, deflate, br)
- **Test files**: `TurboHttp.Tests/RFC9110/` (01–03), `StreamTests/Streams/RedirectStageTests.cs`, `RetryStageTests.cs`

## RFC 6265 — HTTP Cookies

- **§4 Cookie Processing**: Set-Cookie header parsing and storage
- **§5.1 Domain Matching**: Domain attribute matching rules
- **§5.2 Path Matching**: Path attribute matching rules
- **§5.3 Cookie Attributes**: Secure, HttpOnly, SameSite, Max-Age, Expires
- **§5.4 Cookie Header**: Serialization for outgoing requests
- **Implementation**: `CookieJar` class with `AddCookiesToRequest` / `ProcessResponse`
- **Test files**: `TurboHttp.Tests/Integration/` (CookieJar tests), `StreamTests/Streams/CookieInjectionStageTests.cs`, `CookieStorageStageTests.cs`

## RFC 9111 — HTTP Caching

- **§3 Cache Storage**: Thread-safe in-memory LRU cache with Vary support
- **§4.2 Freshness**: Freshness lifetime calculation (s-maxage > max-age > Expires > heuristic)
- **§4.2.1 Current Age**: Age calculation per RFC algorithm
- **§4.3 Validation**: Conditional requests (If-None-Match, If-Modified-Since)
- **§4.3.4 304 Merge**: Update stored response from 304 Not Modified
- **§5.2 Cache-Control**: Directive parsing (no-cache, no-store, max-age, s-maxage, must-revalidate, public, private)
- **Implementation**: `HttpCacheStore`, `CacheFreshnessEvaluator`, `CacheValidationRequestBuilder`, `CacheControlParser`
- **Test files**: `TurboHttp.Tests/RFC9111/` (01–05), `StreamTests/Streams/CacheLookupStageTests.cs`, `CacheStorageStageTests.cs`

## Pipeline Stages (New in Plan 2)

| Stage | RFC | Tests |
|-------|-----|-------|
| CookieInjectionStage | RFC 6265 | 6 |
| CookieStorageStage | RFC 6265 | 6 |
| DecompressionStage | RFC 9110 | 10 |
| CacheLookupStage | RFC 9111 | 12 |
| CacheStorageStage | RFC 9111 | 12 |
| RedirectStage | RFC 9110 §15.4 | 15 |
| RetryStage | RFC 9110 §9.2 | 12 |
| ConnectionReuseStage | RFC 9112 §9 | 10 |
| **Total** | | **83** |
