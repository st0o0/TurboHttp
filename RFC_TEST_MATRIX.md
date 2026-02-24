# RFC Conformance Test Matrix

> **Scope: Client-side only.**
> Encoders: `HttpRequestMessage → bytes`. Decoders: `bytes → HttpResponseMessage`.
> Server-side request parsing and response encoding are out of scope.

---

## 📋 RFC 1945 – HTTP/1.0

## §5.1 Request-Line

| Test ID      | Requirement                            | Priority | Test Case                              | Expected Result                           |
| ------------ | -------------------------------------- | -------- | -------------------------------------- | ----------------------------------------- |
| 1945-5.1-001 | Valid HTTP/1.0 request-line            | MUST     | `GET / HTTP/1.0\r\n`                   | Parse success                             |
| 1945-5.1-002 | HTTP-Version must be 1.0 (in 1.0 mode) | MUST     | `GET / HTTP/1.1\r\n`                   | Parse error                               |
| 1945-5.1-003 | Simple-Request (no version)            | SHOULD   | `GET /\r\n`                            | Parse success (if 0.9 fallback supported) |
| 1945-5.1-004 | Method is case-sensitive               | MUST     | `get / HTTP/1.0\r\n`                   | Parse error                               |
| 1945-5.1-005 | Absolute URI allowed                   | SHOULD   | `GET http://example.com/ HTTP/1.0\r\n` | Parse success                             |
| 1945-5.1-006 | Missing SP between elements            | MUST     | `GET/ HTTP/1.0\r\n`                    | Parse error                               |
| 1945-5.1-007 | Invalid HTTP version format            | MUST     | `GET / HTTP/1\r\n`                     | Parse error                               |

---

## §4 Header Fields

| Test ID    | Requirement                         | Priority | Test Case                            | Expected Result |
| ---------- | ----------------------------------- | -------- | ------------------------------------ | --------------- |
| 1945-4-001 | Host header NOT required            | MUST     | No Host header                       | Parse success   |
| 1945-4-002 | Header folding allowed (obs-fold)   | MUST     | `Header: value\r\n continuation\r\n` | Parse success   |
| 1945-4-003 | Multiple headers same name          | MUST     | Duplicate headers                    | Accept          |
| 1945-4-004 | Header without colon                | MUST     | `InvalidHeader\r\n`                  | Parse error     |
| 1945-4-005 | Header name case-insensitive        | MUST     | `HOST: example.com\r\n`              | Parse success   |
| 1945-4-006 | Leading/trailing whitespace trimmed | MUST     | `Header:  value  \r\n`               | Value trimmed   |
| 1945-4-007 | Invalid header name (space inside)  | MUST     | `Invalid Header: value\r\n`          | Parse error     |

---

## §7 Entity Body

| Test ID    | Requirement                     | Priority | Test Case                        | Expected Result             |
| ---------- | ------------------------------- | -------- | -------------------------------- | --------------------------- |
| 1945-7-001 | Content-Length body             | MUST     | `Content-Length: 5\r\n\r\nHello` | Parse 5 bytes               |
| 1945-7-002 | Zero Content-Length             | MUST     | `Content-Length: 0\r\n\r\n`      | No body                     |
| 1945-7-003 | No Content-Length               | MUST     | Body without length header       | Read until connection close |
| 1945-7-004 | Chunked encoding NOT supported  | MUST     | `Transfer-Encoding: chunked`     | Error                       |
| 1945-7-005 | Multiple Content-Length headers | MUST     | Duplicate Content-Length         | Reject for safety           |
| 1945-7-006 | Negative Content-Length         | MUST     | `Content-Length: -5`             | Error                       |

---

## §8 Connection Management

| Test ID    | Requirement                                 | Priority | Test Case                          | Expected Result           |
| ---------- | ------------------------------------------- | -------- | ---------------------------------- | ------------------------- |
| 1945-8-001 | Default connection is close                 | MUST     | HTTP/1.0 without Connection header | Close after response      |
| 1945-8-002 | Keep-Alive extension                        | SHOULD   | `Connection: keep-alive`           | Keep connection open      |
| 1945-8-003 | Keep-Alive header parameters                | SHOULD   | `Keep-Alive: timeout=5, max=100`   | Parse parameters          |
| 1945-8-004 | HTTP/1.1 keep-alive behavior must NOT apply | MUST     | No Connection header               | Do NOT default keep-alive |
| 1945-8-005 | Explicit Connection: close                  | MUST     | `Connection: close`                | Close connection          |

### §5 — Encoder: HttpRequestMessage → bytes (client sends request)

| Test ID | Requirement | Priority | Covered? | Test Case | Expected Result |
| --- | --- | --- | --- | --- | --- |
| 1945-enc-001 | Request-line uses HTTP/1.0 | MUST | ❌ | `Http10Encoder.Encode(GET /path)` | Starts with `GET /path HTTP/1.0\r\n` |
| 1945-enc-002 | Host header NOT emitted | MUST | ❌ | Request with Host set | Host absent in output |
| 1945-enc-003 | Transfer-Encoding NOT emitted | MUST | ❌ | Request with TE set | TE absent in output |
| 1945-enc-004 | Connection NOT emitted | MUST | ❌ | Request with Connection set | Connection absent in output |
| 1945-enc-005 | Content-Length set for body | MUST | ❌ | POST with 5-byte body | `Content-Length: 5` present |
| 1945-enc-006 | No Content-Length for empty body | MUST | ❌ | GET without body | Content-Length absent |
| 1945-enc-007 | Path-and-query preserved | MUST | ❌ | `GET /search?q=hello` | Request-target is `/search?q=hello` |
| 1945-enc-008 | Binary body preserved exactly | MUST | ❌ | POST with byte[] body | Encoded body bytes match input |
| 1945-enc-009 | POST with UTF-8 body | MUST | ❌ | POST with StringContent(json) | Body bytes match UTF-8 encoded JSON |

### §6 — Decoder: bytes → HttpResponseMessage (client receives response)

| Test ID | Requirement | Priority | Covered? | Test Case | Expected Result |
| --- | --- | --- | --- | --- | --- |
| 1945-dec-001 | Status-line HTTP/1.0 parsed | MUST | ✅ | `HTTP/1.0 200 OK\r\n\r\n` | StatusCode=200 |
| 1945-dec-002 | Version set to 1.0 | MUST | ✅ | Any valid response | response.Version == 1.0 |
| 1945-dec-003 | All RFC 1945 defined codes | MUST | partial | 200,201,202,204,301,302,304,400,401,403,404,500,501,502,503 | All parsed |
| 1945-dec-004 | 304 Not Modified — no body | MUST | ❌ | `HTTP/1.0 304 Not Modified\r\n\r\n` | Content.Length == 0 |
| 1945-dec-005 | LF-only line endings accepted | SHOULD | ✅ | `HTTP/1.0 200 OK\nContent-Length: 0\n\n` | Parsed successfully |
| 1945-dec-006 | Chunked treated as raw body | MUST | ❌ | `Transfer-Encoding: chunked` + chunk data | Raw bytes returned, not parsed as chunks |
| 1945-dec-007 | EOF body via TryDecodeEof | MUST | ✅ | No Content-Length → TryDecodeEof | Full body on connection close |

---

## 📋 RFC 7230 - HTTP/1.1 Message Syntax and Routing

### §3.1.1 Request Line

| Test ID | Requirement | Priority | Test Case | Expected Result |
|---------|-------------|----------|-----------|-----------------|
| 7230-3.1.1-001 | Valid request-line | MUST | `GET /path HTTP/1.1\r\n` | Parse success |
| 7230-3.1.1-002 | Method is case-sensitive | MUST | `get /path HTTP/1.1\r\n` | Parse error |
| 7230-3.1.1-003 | SP between components | MUST | `GET/path HTTP/1.1\r\n` | Parse error |
| 7230-3.1.1-004 | CRLF line ending | MUST | `GET /path HTTP/1.1\r\n` | Parse success |
| 7230-3.1.1-005 | OPTIONS with asterisk | MUST | `OPTIONS * HTTP/1.1\r\n` | Parse success |
| 7230-3.1.1-006 | Absolute URI | SHOULD | `GET http://example.com/path HTTP/1.1\r\n` | Parse success |
| 7230-3.1.1-007 | Missing HTTP version | MUST | `GET /path\r\n` | Parse error |
| 7230-3.1.1-008 | Invalid HTTP version | MUST | `GET /path HTTP/2.0\r\n` | Parse error (for HTTP/1.1 parser) |

### §3.2 Header Fields

| Test ID | Requirement | Priority | Test Case | Expected Result |
|---------|-------------|----------|-----------|-----------------|
| 7230-3.2-001 | Header field format | MUST | `Host: example.com\r\n` | Parse success |
| 7230-3.2-002 | Optional whitespace | MUST | `Host:  example.com  \r\n` | Parse success, trim value |
| 7230-3.2-003 | Empty header value | MUST | `X-Empty:\r\n` | Parse success, empty value |
| 7230-3.2-004 | Multiple header values | MUST | `Accept: text/html\r\nAccept: text/plain\r\n` | Combine or keep separate |
| 7230-3.2-005 | Obs-fold (obsolete) | MUST | `Header: value\r\n continuation\r\n` | Parse success (HTTP/1.0) or error (HTTP/1.1) |
| 7230-3.2-006 | Header without colon | MUST | `InvalidHeader\r\n` | Parse error |
| 7230-3.2-007 | Header name case | MUST | `HOST: example.com\r\n` | Parse success (case-insensitive) |
| 7230-3.2-008 | Invalid header name | MUST | `Invalid Header: value\r\n` | Parse error (space in name) |

### §3.3 Message Body

| Test ID | Requirement | Priority | Test Case | Expected Result |
|---------|-------------|----------|-----------|-----------------|
| 7230-3.3-001 | Content-Length body | MUST | `Content-Length: 5\r\n\r\nHello` | Parse 5 bytes |
| 7230-3.3-002 | Zero Content-Length | MUST | `Content-Length: 0\r\n\r\n` | No body |
| 7230-3.3-003 | Chunked encoding | MUST | `Transfer-Encoding: chunked\r\n\r\n5\r\nHello\r\n0\r\n\r\n` | Parse chunks |
| 7230-3.3-004 | Conflicting headers | MUST | `Content-Length: 5\r\nTransfer-Encoding: chunked\r\n` | Error or prefer chunked |
| 7230-3.3-005 | Multiple Content-Length | MUST | `Content-Length: 5\r\nContent-Length: 6\r\n` | Error |
| 7230-3.3-006 | Negative Content-Length | MUST | `Content-Length: -5\r\n` | Error |
| 7230-3.3-007 | No body indicators | MUST | No Content-Length or Transfer-Encoding | No body (for request) |

### §4.1 Chunked Transfer Encoding

| Test ID | Requirement | Priority | Test Case | Expected Result |
|---------|-------------|----------|-----------|-----------------|
| 7230-4.1-001 | Simple chunk | MUST | `5\r\nHello\r\n0\r\n\r\n` | Parse "Hello" |
| 7230-4.1-002 | Multiple chunks | MUST | `5\r\nHello\r\n6\r\n World\r\n0\r\n\r\n` | Parse "Hello World" |
| 7230-4.1-003 | Chunk extension | SHOULD | `5;ext=value\r\nHello\r\n0\r\n\r\n` | Parse "Hello", ignore extension |
| 7230-4.1-004 | Trailer fields | SHOULD | `5\r\nHello\r\n0\r\nX-Trailer: value\r\n\r\n` | Parse "Hello" + trailer |
| 7230-4.1-005 | Invalid chunk size | MUST | `xyz\r\nHello\r\n0\r\n\r\n` | Error |
| 7230-4.1-006 | Missing final chunk | MUST | `5\r\nHello\r\n` (no 0\r\n\r\n) | Error or incomplete |
| 7230-4.1-007 | Zero-size chunk | MUST | `0\r\n\r\n` | End of body |
| 7230-4.1-008 | Chunk size too large | MUST | `999999999999\r\n` | Error (overflow) |

### §6.1 Connection

| Test ID | Requirement | Priority | Test Case | Expected Result |
|---------|-------------|----------|-----------|-----------------|
| 7230-6.1-001 | Connection: close | MUST | HTTP/1.1 with `Connection: close` | Close after response |
| 7230-6.1-002 | Connection: keep-alive | SHOULD | HTTP/1.1 with `Connection: keep-alive` | Keep connection open |
| 7230-6.1-003 | No Connection header | MUST | HTTP/1.1 without Connection | Default keep-alive |
| 7230-6.1-004 | HTTP/1.0 default | MUST | HTTP/1.0 without Connection | Default close |
| 7230-6.1-005 | Multiple Connection tokens | MUST | `Connection: close, upgrade` | Parse all tokens |

---

## 📋 RFC 7231 - HTTP/1.1 Semantics and Content

### §6.1 Status Codes

| Test ID | Requirement | Priority | Test Case | Expected Result |
|---------|-------------|----------|-----------|-----------------|
| 7231-6.1-001 | 1xx Informational | MUST | `HTTP/1.1 100 Continue\r\n\r\n` | Parse, no body expected |
| 7231-6.1-002 | 2xx Success | MUST | `HTTP/1.1 200 OK\r\n\r\n` | Parse success |
| 7231-6.1-003 | 3xx Redirection | MUST | `HTTP/1.1 301 Moved Permanently\r\n` | Parse, check Location header |
| 7231-6.1-004 | 4xx Client Error | MUST | `HTTP/1.1 404 Not Found\r\n` | Parse error response |
| 7231-6.1-005 | 5xx Server Error | MUST | `HTTP/1.1 500 Internal Server Error\r\n` | Parse error response |
| 7231-6.1-006 | Custom status code | SHOULD | `HTTP/1.1 599 Custom Error\r\n` | Parse success |
| 7231-6.1-007 | Invalid status code | MUST | `HTTP/1.1 999 Invalid\r\n` | Error (>599) |
| 7231-6.1-008 | Empty reason phrase | MUST | `HTTP/1.1 200 \r\n` | Parse success |

### §7.1.1.1 Date/Time Formats

| Test ID | Requirement | Priority | Test Case | Expected Result |
|---------|-------------|----------|-----------|-----------------|
| 7231-7.1.1-001 | IMF-fixdate | MUST | `Sun, 06 Nov 1994 08:49:37 GMT` | Parse success |
| 7231-7.1.1-002 | RFC 850 (obsolete) | MUST | `Sunday, 06-Nov-94 08:49:37 GMT` | Parse success (receive) |
| 7231-7.1.1-003 | ANSI C asctime | MUST | `Sun Nov  6 08:49:37 1994` | Parse success (receive) |
| 7231-7.1.1-004 | Non-GMT timezone | MUST | `Sun, 06 Nov 1994 08:49:37 PST` | Error (only GMT allowed) |
| 7231-7.1.1-005 | Invalid date format | MUST | `Invalid Date` | Error |

---

## 📋 RFC 7233 - HTTP/1.1 Range Requests

### §2.1 Range Units

| Test ID | Requirement | Priority | Test Case | Expected Result |
|---------|-------------|----------|-----------|-----------------|
| 7233-2.1-001 | Simple byte range | MUST | `Range: bytes=0-499` | Parse 0-499 |
| 7233-2.1-002 | Suffix byte range | MUST | `Range: bytes=-500` | Parse last 500 bytes |
| 7233-2.1-003 | Open-ended range | MUST | `Range: bytes=500-` | Parse from 500 to end |
| 7233-2.1-004 | Multiple ranges | SHOULD | `Range: bytes=0-499,1000-1499` | Parse multiple ranges |
| 7233-2.1-005 | Invalid range | MUST | `Range: bytes=abc-xyz` | Error |
| 7233-2.1-006 | Unsatisfiable range | MUST | `Range: bytes=1000-2000` (file size 500) | 416 Range Not Satisfiable |

### §4.1 206 Partial Content

| Test ID | Requirement | Priority | Test Case | Expected Result |
|---------|-------------|----------|-----------|-----------------|
| 7233-4.1-001 | Content-Range header | MUST | `Content-Range: bytes 0-499/1000` | Parse range info |
| 7233-4.1-002 | Single part response | MUST | 206 with Content-Range | Parse partial content |
| 7233-4.1-003 | Multipart response | SHOULD | 206 with multipart/byteranges | Parse multiple parts |
| 7233-4.1-004 | Complete length unknown | SHOULD | `Content-Range: bytes 0-499/*` | Parse with unknown total |

---

## 📋 RFC 7540 - HTTP/2

### §3.5 HTTP/2 Connection Preface

| Test ID | Requirement | Priority | Test Case | Expected Result |
|---------|-------------|----------|-----------|-----------------|
| 7540-3.5-001 | Client preface | MUST | `PRI * HTTP/2.0\r\n\r\nSM\r\n\r\n` | Accept preface |
| 7540-3.5-002 | Invalid preface | MUST | `INVALID PREFACE` | PROTOCOL_ERROR |
| 7540-3.5-003 | SETTINGS frame after preface | MUST | Preface + SETTINGS frame | Accept connection |
| 7540-3.5-004 | Missing SETTINGS | MUST | Preface without SETTINGS | Error or timeout |

### §4.1 Frame Format

| Test ID | Requirement | Priority | Test Case | Expected Result |
|---------|-------------|----------|-----------|-----------------|
| 7540-4.1-001 | Valid frame header | MUST | 9-byte header with valid fields | Parse success |
| 7540-4.1-002 | Frame length 24-bit | MUST | Length field uses 3 octets | Parse correctly |
| 7540-4.1-003 | Frame type | MUST | Type field 0x00-0x09 | Recognize frame type |
| 7540-4.1-004 | Unknown frame type | MUST | Type field 0x0A+ | Ignore frame |
| 7540-4.1-005 | Stream ID 31-bit | MUST | Stream ID with R-bit = 0 | Parse stream ID |
| 7540-4.1-006 | R-bit must be zero | MUST | R-bit = 1 | PROTOCOL_ERROR |
| 7540-4.1-007 | Frame size limit | MUST | Frame > SETTINGS_MAX_FRAME_SIZE | FRAME_SIZE_ERROR |

### §5.1 Stream States

| Test ID | Requirement | Priority | Test Case | Expected Result |
|---------|-------------|----------|-----------|-----------------|
| 7540-5.1-001 | Idle to open | MUST | Send HEADERS on idle stream | Stream opens |
| 7540-5.1-002 | Open to half-closed (local) | MUST | Send END_STREAM flag | Half-closed local |
| 7540-5.1-003 | Open to half-closed (remote) | MUST | Receive END_STREAM flag | Half-closed remote |
| 7540-5.1-004 | Half-closed to closed | MUST | Both ends send END_STREAM | Stream closed |
| 7540-5.1-005 | Idle to reserved | MUST | Receive PUSH_PROMISE | Stream reserved |
| 7540-5.1-006 | Invalid state transition | MUST | Send DATA on closed stream | STREAM_CLOSED error |
| 7540-5.1-007 | Stream ID reuse | MUST | Reuse closed stream ID | PROTOCOL_ERROR |
| 7540-5.1-008 | Even stream ID from client | MUST | Client sends even ID | PROTOCOL_ERROR |

### §5.2 Flow Control

| Test ID | Requirement | Priority | Test Case | Expected Result |
|---------|-------------|----------|-----------|-----------------|
| 7540-5.2-001 | Initial window size | MUST | Default 65535 bytes | Accept value |
| 7540-5.2-002 | WINDOW_UPDATE | MUST | Send WINDOW_UPDATE frame | Update window |
| 7540-5.2-003 | Flow control violation | MUST | Send more than window allows | FLOW_CONTROL_ERROR |
| 7540-5.2-004 | Window overflow | MUST | WINDOW_UPDATE causes overflow | FLOW_CONTROL_ERROR |
| 7540-5.2-005 | Zero window size | MUST | Window exhausted to 0 | Block sending |
| 7540-5.2-006 | Connection window | MUST | Connection-level flow control | Enforce limit |
| 7540-5.2-007 | Stream window | MUST | Per-stream flow control | Enforce limit |
| 7540-5.2-008 | Negative window increment | MUST | WINDOW_UPDATE with increment=0 | PROTOCOL_ERROR |

### §6.1 DATA Frame

| Test ID | Requirement | Priority | Test Case | Expected Result |
|---------|-------------|----------|-----------|-----------------|
| 7540-6.1-001 | Valid DATA frame | MUST | DATA frame on open stream | Parse data |
| 7540-6.1-002 | END_STREAM flag | MUST | DATA frame with END_STREAM=1 | Close stream |
| 7540-6.1-003 | PADDED flag | SHOULD | DATA frame with padding | Strip padding |
| 7540-6.1-004 | DATA on stream 0 | MUST | DATA frame on stream 0 | PROTOCOL_ERROR |
| 7540-6.1-005 | DATA on closed stream | MUST | DATA frame on closed stream | STREAM_CLOSED error |
| 7540-6.1-006 | Empty DATA frame | MUST | DATA frame with length=0 | Accept (may close stream) |

### §6.2 HEADERS Frame

| Test ID | Requirement | Priority | Test Case | Expected Result |
|---------|-------------|----------|-----------|-----------------|
| 7540-6.2-001 | Valid HEADERS frame | MUST | HEADERS frame with header block | Parse headers |
| 7540-6.2-002 | END_STREAM flag | MUST | HEADERS with END_STREAM=1 | Close stream |
| 7540-6.2-003 | END_HEADERS flag | MUST | HEADERS with END_HEADERS=1 | Complete headers |
| 7540-6.2-004 | PADDED flag | SHOULD | HEADERS with padding | Strip padding |
| 7540-6.2-005 | PRIORITY flag | SHOULD | HEADERS with priority info | Parse priority |
| 7540-6.2-006 | CONTINUATION required | MUST | HEADERS without END_HEADERS | Expect CONTINUATION |
| 7540-6.2-007 | HEADERS on stream 0 | MUST | HEADERS on stream 0 | PROTOCOL_ERROR |

### §6.9 CONTINUATION Frame

| Test ID | Requirement | Priority | Test Case | Expected Result |
|---------|-------------|----------|-----------|-----------------|
| 7540-6.9-001 | After HEADERS | MUST | CONTINUATION after HEADERS | Append header block |
| 7540-6.9-002 | END_HEADERS flag | MUST | CONTINUATION with END_HEADERS=1 | Complete headers |
| 7540-6.9-003 | Multiple CONTINUATION | MUST | Multiple CONTINUATION frames | Append all blocks |
| 7540-6.9-004 | Wrong stream ID | MUST | CONTINUATION on different stream | PROTOCOL_ERROR |
| 7540-6.9-005 | Interleaved frames | MUST | Other frame between HEADERS and CONTINUATION | PROTOCOL_ERROR |
| 7540-6.9-006 | CONTINUATION on stream 0 | MUST | CONTINUATION on stream 0 | PROTOCOL_ERROR |

---

## 📋 RFC 7541 - HPACK

### §2.3 Dynamic Table

| Test ID | Requirement | Priority | Test Case | Expected Result |
|---------|-------------|----------|-----------|-----------------|
| 7541-2.3-001 | Table insertion | MUST | Add entry to dynamic table | Entry at index 62 |
| 7541-2.3-002 | Table eviction | MUST | Table full, insert new entry | Evict oldest entry |
| 7541-2.3-003 | Table size update | MUST | SETTINGS_HEADER_TABLE_SIZE | Resize table |
| 7541-2.3-004 | Table size 0 | MUST | Table size set to 0 | Clear all entries |
| 7541-2.3-005 | Table size too large | MUST | Size > maximum | COMPRESSION_ERROR |

### §5.1 Integer Representation

| Test ID | Requirement | Priority | Test Case | Expected Result |
|---------|-------------|----------|-----------|-----------------|
| 7541-5.1-001 | Small integer | MUST | Value < prefix | Encode in prefix bits |
| 7541-5.1-002 | Large integer | MUST | Value ≥ prefix | Multi-byte encoding |
| 7541-5.1-003 | Maximum integer | MUST | 2^31 - 1 | Parse correctly |
| 7541-5.1-004 | Integer overflow | MUST | Value > 2^31 - 1 | Error |

### §5.2 String Representation

| Test ID | Requirement | Priority | Test Case | Expected Result |
|---------|-------------|----------|-----------|-----------------|
| 7541-5.2-001 | Plain string | MUST | H=0, raw string | Parse string |
| 7541-5.2-002 | Huffman string | MUST | H=1, Huffman encoded | Decode string |
| 7541-5.2-003 | Empty string | MUST | Length=0 | Empty string |
| 7541-5.2-004 | Large string | MUST | String > 8KB | Parse correctly |
| 7541-5.2-005 | Invalid Huffman | MUST | Malformed Huffman data | COMPRESSION_ERROR |

### §6.1 Indexed Header Field

| Test ID | Requirement | Priority | Test Case | Expected Result |
|---------|-------------|----------|-----------|-----------------|
| 7541-6.1-001 | Static table index | MUST | Index 1-61 | Retrieve from static table |
| 7541-6.1-002 | Dynamic table index | MUST | Index 62+ | Retrieve from dynamic table |
| 7541-6.1-003 | Index out of range | MUST | Invalid index | COMPRESSION_ERROR |

### §6.2 Literal Header Field

| Test ID | Requirement | Priority | Test Case | Expected Result |
|---------|-------------|----------|-----------|-----------------|
| 7541-6.2-001 | With incremental indexing | MUST | Pattern 01 | Add to dynamic table |
| 7541-6.2-002 | Without indexing | MUST | Pattern 0000 | Don't add to table |
| 7541-6.2-003 | Never indexed | MUST | Pattern 0001 | Don't add, don't index |
| 7541-6.2-004 | Indexed name | MUST | Name as index, value literal | Parse correctly |
| 7541-6.2-005 | Literal name | MUST | Both name and value literal | Parse correctly |

### Appendix C Examples

| Test ID | Requirement | Priority | Test Case | Expected Result |
|---------|-------------|----------|-----------|-----------------|
| 7541-C.2-001 | Request without Huffman | MUST | Decode example C.2.1-C.2.4 | Match expected output |
| 7541-C.3-001 | Request with Huffman | MUST | Decode example C.3.1-C.3.3 | Match expected output |
| 7541-C.4-001 | Response without Huffman | MUST | Decode example C.4.1-C.4.3 | Match expected output |
| 7541-C.5-001 | Response with Huffman | MUST | Decode example C.5.1-C.5.3 | Match expected output |
| 7541-C.6-001 | Response with Huffman (cont) | MUST | Decode example C.6.1-C.6.3 | Match expected output |

---

## 📊 Test Execution Matrix

### Priority Levels:
- **P0 (Critical):** Must pass before any release
- **P1 (High):** Should pass for production release
- **P2 (Medium):** Nice to have, can be addressed later
- **P3 (Low):** Optional features, future enhancement

### Test Execution Plan:

| Phase | RFC Section | Test Count | Priority | Estimated Time |
|-------|-------------|------------|----------|----------------|
| 1 | RFC 7230 §3.1 (Request-Line) | 8 | P0 | 0.5 day |
| 1 | RFC 7230 §3.2 (Headers) | 8 | P0 | 0.5 day |
| 1 | RFC 7230 §3.3 (Message Body) | 7 | P0 | 1 day |
| 1 | RFC 7230 §4.1 (Chunked) | 8 | P0 | 1 day |
| 1 | RFC 7231 §6.1 (Status Codes) | 8 | P0 | 0.5 day |
| 2 | RFC 7233 (Range Requests) | 10 | P1 | 1 day |
| 2 | RFC 7232 (Conditional) | 12 | P1 | 1 day |
| 3 | RFC 7540 §4.1 (Frame Format) | 7 | P0 | 1 day |
| 3 | RFC 7540 §5.1 (Stream States) | 8 | P0 | 1 day |
| 3 | RFC 7540 §5.2 (Flow Control) | 8 | P0 | 1 day |
| 3 | RFC 7540 §6.x (Frame Types) | 30+ | P0 | 2 days |
| 3 | RFC 7541 (HPACK) | 20+ | P0 | 2 days |
| 4 | RFC 7540 §8.2 (Server Push) | 5 | P2 | 0.5 day |
| **TOTAL** | | **~150+** | | **~13 days** |

---

## 🎯 Coverage Goals

### RFC Compliance:
- **MUST requirements:** 100% coverage
- **SHOULD requirements:** ≥ 90% coverage
- **MAY requirements:** ≥ 50% coverage

### Code Coverage:
- **Line coverage:** ≥ 90%
- **Branch coverage:** ≥ 85%
- **Mutation coverage:** ≥ 75% (optional, for critical paths)

---

**This test matrix should be continuously updated as implementation progresses.**
