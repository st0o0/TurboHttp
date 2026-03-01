# TIER 1: RFC COMPLIANCE — Phases 28-38
## Detailed Implementation Guide

---

## Phase 28: Http11Decoder — Chunk Extension Parsing

**Location**: `src/TurboHttp/Protocol/Http11Decoder.cs`
**RFC**: RFC 9112 §6.3
**Tests**: 35+

### Concrete Implementation

Add helper method:
```csharp
private static bool TryParseChunkExtensions(ReadOnlySpan<byte> extBytes)
{
    // RFC 9112 §6.3: chunk-ext = *( BWS ";" BWS chunk-ext-name [ BWS "=" BWS chunk-ext-val ] )
    if (extBytes.IsEmpty) return true;

    var pos = 0;
    while (pos < extBytes.Length)
    {
        while (pos < extBytes.Length && (extBytes[pos] == ' ' || extBytes[pos] == '\t'))
            pos++;

        var nameStart = pos;
        while (pos < extBytes.Length && IsTokenChar(extBytes[pos]) && extBytes[pos] != ';')
            pos++;

        if (pos == nameStart) return false;

        while (pos < extBytes.Length && (extBytes[pos] == ' ' || extBytes[pos] == '\t'))
            pos++;

        if (pos < extBytes.Length && extBytes[pos] == '=')
        {
            pos++;
            while (pos < extBytes.Length && (extBytes[pos] == ' ' || extBytes[pos] == '\t'))
                pos++;

            if (pos < extBytes.Length && extBytes[pos] == '"')
            {
                pos++;
                while (pos < extBytes.Length && extBytes[pos] != '"')
                {
                    if (extBytes[pos] == '\\') pos += 2;
                    else pos++;
                }
                if (pos >= extBytes.Length) return false;
                pos++;
            }
            else
            {
                var valStart = pos;
                while (pos < extBytes.Length && IsTokenChar(extBytes[pos]) && extBytes[pos] != ';')
                    pos++;
                if (pos == valStart) return false;
            }
        }

        while (pos < extBytes.Length && (extBytes[pos] == ' ' || extBytes[pos] == '\t'))
            pos++;

        if (pos < extBytes.Length && extBytes[pos] == ';')
            pos++;
        else if (pos < extBytes.Length)
            return false;
    }
    return true;
}

private static bool IsTokenChar(byte b)
{
    return b switch
    {
        (byte)'!' or (byte)'#' or (byte)'$' or (byte)'%' or (byte)'&' or (byte)'\''
        or (byte)'*' or (byte)'+' or (byte)'-' or (byte)'.' or (byte)'^' or (byte)'_'
        or (byte)'`' or (byte)'|' or (byte)'~' => true,
        _ => (b >= (byte)'0' && b <= (byte)'9') ||
             (b >= (byte)'A' && b <= (byte)'Z') ||
             (b >= (byte)'a' && b <= (byte)'z')
    };
}
```

Modify `ParseChunkedBody` (line 641):
```csharp
var semiIdx = sizeLine.IndexOf((byte)';');
var sizeSpan = semiIdx >= 0 ? sizeLine[..semiIdx] : sizeLine;
var extSpan = semiIdx >= 0 ? sizeLine[(semiIdx + 1)..] : ReadOnlySpan<byte>.Empty;

if (!TryParseChunkExtensions(extSpan))
{
    return (HttpDecodeResult.Fail(HttpDecodeError.InvalidChunkExtension), null, 0, null);
}
```

Add error code to `HttpDecodeError.cs`:
```csharp
InvalidChunkExtension,
```

### Test Requirements
- 35+ tests in `Http11DecoderChunkExtensionTests.cs`
- Valid: no extension, single, multiple, whitespace, quoted values
- Invalid: missing name, unclosed quote
- All existing tests must still pass

### Validation
- `dotnet test --filter "Http11DecoderChunkExtensionTests"`
- Zero regressions

---

## Phase 29-30: Http2Encoder — Pseudo-Header Validation

**Location**: `src/TurboHttp/Protocol/Http2Encoder.cs`
**RFC**: RFC 7540 §8.1.2.1
**Part 1**: API Design (20 contract tests)
**Part 2**: Implementation (25+ integration tests)

### Implementation

```csharp
private static void ValidatePseudoHeaders(List<(string, string)> headers)
{
    var hasMethod = false, hasPath = false, hasScheme = false, hasAuthority = false;
    var lastPseudoIndex = -1;
    var firstRegularIndex = int.MaxValue;

    for (int i = 0; i < headers.Count; i++)
    {
        var (name, value) = headers[i];

        if (name.StartsWith(':'))
        {
            lastPseudoIndex = i;

            switch (name)
            {
                case ":method":
                    if (hasMethod) throw new Http2Exception("Duplicate :method", Http2ErrorCode.ProtocolError);
                    hasMethod = true;
                    break;
                case ":path":
                    if (hasPath) throw new Http2Exception("Duplicate :path", Http2ErrorCode.ProtocolError);
                    hasPath = true;
                    break;
                case ":scheme":
                    if (hasScheme) throw new Http2Exception("Duplicate :scheme", Http2ErrorCode.ProtocolError);
                    hasScheme = true;
                    break;
                case ":authority":
                    if (hasAuthority) throw new Http2Exception("Duplicate :authority", Http2ErrorCode.ProtocolError);
                    hasAuthority = true;
                    break;
                default:
                    throw new Http2Exception($"Unknown pseudo-header: {name}", Http2ErrorCode.ProtocolError);
            }
        }
        else
        {
            firstRegularIndex = Math.Min(firstRegularIndex, i);
        }
    }

    var missing = new List<string>();
    if (!hasMethod) missing.Add(":method");
    if (!hasPath) missing.Add(":path");
    if (!hasScheme) missing.Add(":scheme");
    if (!hasAuthority) missing.Add(":authority");

    if (missing.Count > 0)
    {
        throw new Http2Exception(
            $"RFC 7540 §8.1.2.1: Missing required pseudo-headers: {string.Join(", ", missing)}",
            Http2ErrorCode.ProtocolError);
    }

    if (lastPseudoIndex > firstRegularIndex)
    {
        throw new Http2Exception(
            $"RFC 7540 §8.1.2.1: Pseudo-header at index {lastPseudoIndex} after regular header at {firstRegularIndex}",
            Http2ErrorCode.ProtocolError);
    }

    var expectedOrder = new[] { ":method", ":path", ":scheme", ":authority" };
    var actualPseudoIdx = 0;

    for (int i = 0; i < headers.Count && actualPseudoIdx < 4; i++)
    {
        if (headers[i].Item1.StartsWith(':'))
        {
            if (headers[i].Item1 != expectedOrder[actualPseudoIdx])
            {
                throw new Http2Exception(
                    $"RFC 7540 §8.1.2.1: Pseudo-header {headers[i].Item1} out of order. Expected {expectedOrder[actualPseudoIdx]}",
                    Http2ErrorCode.ProtocolError);
            }
            actualPseudoIdx++;
        }
    }
}
```

Call in `Encode()` after building headers list:
```csharp
ValidatePseudoHeaders(headers);
var headerBlock = _hpack.Encode(headers);
```

---

## Phase 31: Http2Encoder — Sensitive Header Handling ✅

**Location**: `src/TurboHttp/Protocol/Http2Encoder.cs` + `HpackEncoder.cs`
**RFC**: RFC 7541 §7.1.3
**Tests**: 35 (exceeds 30+ requirement) — `Http2EncoderSensitiveHeaderTests.cs`

### Implementation

In Http2Encoder.Encode():
```csharp
var sensitiveIndices = new HashSet<int>();
var headerIndex = 4;  // After pseudo-headers

// ... when adding headers ...
if (lower is "authorization" or "proxy-authorization" or "set-cookie")
{
    sensitiveIndices.Add(headerIndex);
}

headers.Add((lower, value));
headerIndex++;
```

In HpackEncoder:
```csharp
public ReadOnlyMemory<byte> Encode(
    List<(string, string)> headers,
    HashSet<int>? sensitiveIndices = null)
{
    var output = new List<byte>(1024);
    for (int i = 0; i < headers.Count; i++)
    {
        var (name, value) = headers[i];
        var isSensitive = sensitiveIndices?.Contains(i) ?? false;
        EncodeHeader(output, name, value, isSensitive);
    }
    return output.ToArray().AsMemory();
}
```

---

## Phase 32-33: Http2Decoder — MAX_CONCURRENT_STREAMS ✅

**Location**: `src/TurboHttp/Protocol/Http2Decoder.cs`
**RFC**: RFC 7540 §5.1, §6.5.2
**Part 1**: API Design (20 contract tests)
**Part 2**: Implementation (20+ integration tests)

### Implementation

Add fields:
```csharp
private int _maxConcurrentStreams = int.MaxValue;
private int _activeStreamCount = 0;

public int GetActiveStreamCount() => _activeStreamCount;
public int GetMaxConcurrentStreams() => _maxConcurrentStreams;
```

In HandleHeaders():
```csharp
if (!_streams.ContainsKey(streamId) && _activeStreamCount >= _maxConcurrentStreams)
{
    throw new Http2Exception(
        $"Max concurrent streams limit ({_maxConcurrentStreams}) exceeded",
        Http2ErrorCode.RefusedStream);
}
_activeStreamCount++;
```

In HandleData() on stream close:
```csharp
if ((flags & (byte)DataFlags.EndStream) != 0)
{
    // ...
    _activeStreamCount--;
}
```

In ApplySettings():
```csharp
case SettingsParameter.MaxConcurrentStreams:
    _maxConcurrentStreams = (int)value;
    break;
```

---

## Phase 34: Error Codes & Messages

**Location**: `HttpDecodeError.cs` + all decoders

### Task
- Ensure all errors have specific codes (not generic)
- Add context to messages (position, expected vs. actual)
- RFC references in messages
- Clear, actionable error messages

### Tests: 20+

---

## Phase 35-37: Round-Trip Tests

### Phase 35: Http10 (50+ tests) ✅
- [ ] All methods (GET, POST, PUT, DELETE, HEAD, OPTIONS, PATCH)
- [ ] With/without body
- [ ] Various headers (custom, forbidden-stripped, content headers)
- [ ] Fragmented TCP reads (2-frag, single-byte, mid-header, mid-body)
- [ ] Large bodies (64KB), UTF-8 multi-byte bodies
- **Tests**: 50 in `Http10RoundTripTests.cs` (7 original + 43 new RT-10-008..RT-10-050)

### Phase 36: Http11 (60+ tests) ✅
- [ ] Content-Length scenarios (zero, UTF-8, 64KB, pipelined, single-byte, reset)
- [ ] Chunked transfer encoding (1-byte, uppercase hex, 20 tiny, 32KB, with extension, pipelined)
- [ ] Pipelined requests (3x, 5x, mixed status, mixed encodings)
- [ ] HEAD requests (TryDecodeHead — Content-Length ignored, 404, pipelined, HEAD+GET)
- [ ] No-body responses (304 ETag, 204 DELETE, 304→200 pipeline, 102 skipped, 204+Content-Type)
- [ ] Trailer headers (two trailers, numeric, multiple)
- [ ] Keep-alive vs. close (Connection:close, sequential, reset, varying sizes)
- [ ] TCP fragmentation (5 fragmentation scenarios + single-byte delivery)
- **Tests**: 61 in `Http11RoundTripTests.cs` (20 original + 41 new RT-11-021..RT-11-061)

### Phase 37: Http2 (50+ tests)
- [ ] Connection preface + SETTINGS
- [ ] Pseudo-header validation
- [ ] Large headers (continuation frames)
- [ ] Sensitive headers
- [ ] Multiple responses
- [ ] Flow control
- [ ] HPACK synchronization

---

## Phase 38: Validation Gate ✅

**Objective**: Confirm all Tier 1 improvements working together

### Tasks
- [ ] Full test suite: `dotnet test src/TurboHttp.sln`
- [ ] All tests pass (0 failures)
- [ ] Code coverage >95%
- [ ] Benchmarks (dry-run)
- [ ] Performance vs. baseline
- [ ] Regression report
- [ ] Commit: "Complete Tier 1: Client-Side RFC Compliance (16 Phases)"

### Validation Checklist
- [ ] Zero test failures
- [ ] Zero regressions
- [ ] Coverage maintained
- [ ] All phases documented
- [ ] Ready for Tier 2

**Definition of Done**: **Tier 1 COMPLETE ✅**

# TIER 2 – HTTP Core Compliance Task List (Phases 49–60)

---

# Phase 49–50: Content-Encoding Handling

(**RFC 9110 §8.4**)

## 🎯 Objective

Correct processing of `Content-Encoding` according to HTTP Semantics.

## MUST Requirements

- [ ] Send `Accept-Encoding` if the client supports compression
- [ ] Support stacked encodings (decode in reverse order)
- [ ] Properly handle `identity`
- [ ] Unknown encodings:

    * Request → 415 (if sending unsupported encoding)
    * Response → fail or pass through safely
- [ ] Remove `Content-Encoding` after successful decompression
- [ ] Update `Content-Length` after decompression
- [ ] Support streaming decompression (avoid full buffering when possible)

## Edge Cases

- [ ] Multiple encodings (e.g., `gzip, br`)
- [ ] Empty bodies
- [ ] 204 and 304 MUST NOT contain a body
- [ ] HEAD responses MUST NOT include a body
- [ ] Do not confuse `Transfer-Encoding` with `Content-Encoding`

---

# Phase 51–52: Redirect Handling

(**RFC 9110 §15.4**)

## 🎯 Objective

Semantically correct redirect behavior.

## MUST Requirements

- [ ] Support: 301, 302, 303, 307, 308
- [ ] 303 → always switch to GET
- [ ] 307/308 → preserve method and body
- [ ] 301/302 → historical GET rewrite handled intentionally
- [ ] Resolve relative `Location` headers correctly
- [ ] Enforce `MaxRedirects`
- [ ] Detect redirect loops

## Security Requirements

- [ ] Do NOT forward `Authorization` header across origins
- [ ] Optionally block HTTPS → HTTP downgrade
- [ ] Re-evaluate cookies for each new redirect URI

---

# Phase 53–54: Cookie Management

(**RFC 6265**)

## 🎯 Objective

Full RFC 6265 compliance.

## MUST Requirements

- [ ] Implement domain matching per RFC 6265 §5.1.3
- [ ] Distinguish host-only vs domain cookies
- [ ] Implement path matching correctly
- [ ] Correctly interpret `Expires` and `Max-Age`
- [ ] Send `Secure` cookies only over HTTPS
- [ ] Respect `HttpOnly`
- [ ] Correctly process multiple `Set-Cookie` headers

## SHOULD

- [ ] Support `SameSite`
- [ ] Implement public suffix protection

## MUST NOT

- [ ] Use naive `EndsWith()` domain matching
- [ ] Store cookies without domain/path scoping

---

# Phase 55–56: Connection Management

(**RFC 9112 §9 – HTTP/1.1**)

## 🎯 Objective

Correct persistent connection behavior.

## MUST Requirements

- [ ] Persistent connections enabled by default
- [ ] Respect `Connection: close`
- [ ] Correctly interpret `Keep-Alive`
- [ ] Do NOT reuse connection when:

    * Response body not fully consumed
    * Protocol errors occurred
    * Connection explicitly closed
- [ ] Enforce per-host connection limits

## HTTP/2 / HTTP/3 Considerations

- [ ] Support multiplexing behavior
- [ ] Do not apply HTTP/1.1 pooling logic to HTTP/2 streams

---

# Phase 57: Logging (Spec-Neutral but Safe)

## MUST NOT

- [ ] Log sensitive headers (Authorization, Cookie)
- [ ] Log full bodies by default
- [ ] Alter request/response semantics

---

# Phase 58: Timeout & Retry Policies

(**RFC 9110 §9.2 – Idempotency**)

## 🎯 Objective

Semantically safe retries.

## MUST Requirements

-[ ] Automatically retry only idempotent methods:

    * GET
    * HEAD
    * PUT
    * DELETE
    * OPTIONS

- [ ] Do NOT automatically retry POST

- [ ] Retry only on:

    * Network failures
    * 408
    * 503 (+ optionally respect Retry-After)

- [ ] Respect `Retry-After` header

## MUST NOT

- [ ] Retry partial streamed bodies without rewind support
- [ ] Blindly resend non-idempotent requests

---

# Phase 59: Cross-Feature Integrity Validation

Ensure correct interaction between features:

- [ ] Redirect + Cookies → correct domain re-evaluation
- [ ] Redirect + Authorization → strip on cross-origin
- [ ] Decompression + Caching → entity integrity preserved
- [ ] Pooling + Timeout → no leaked connections
- [ ] Retry + Streaming → only retry rewindable bodies
- [ ] HEAD → never expose body even if decompressed

---

# Phase 60: Final HTTPWG Core Validation Gate

## RFC 9110 Validation

- [ ] All methods handled correctly
- [ ] All status codes interpreted correctly
- [ ] Headers treated case-insensitively
- [ ] Multiple header combination rules respected
- [ ] Message body rules fully implemented
- [ ] Proper handling of 1xx responses
- [ ] 204/304 without body
- [ ] HEAD without body

## RFC 9112 (HTTP/1.1)

- [ ] Correct chunked decoding
- [ ] Chunk extensions safely ignored (if unsupported)
- [ ] Trailer fields handled or discarded safely
- [ ] Content-Length conflicts handled securely

## RFC 9111 (if caching implemented)

-[ ] Correct Cache-Control parsing
-[ ] Respect `no-store`
-[ ] Respect `must-revalidate`
-[ ] Implement `Vary` handling

---

# 🚨 Definition of Done – HTTP Core Compliant

Your client is **HTTP Core compliant** when:

-[ ] No behavior contradicts RFC 9110 semantics
-[ ] Redirect handling is secure
-[ ] Cookie matching is RFC-correct
-[ ] No incorrect body handling
-[ ] No keep-alive protocol violations
-[ ] Retry logic respects idempotency rules

---

---

# 🚀 HTTP/2 + HPACK Implementation Phases

## RFC 9113 & RFC 7541 Deep Compliance Roadmap

---

# 🟦 TIER 3 — Connection & Framing Layer

---

## Phase 1–2: Connection Preface & ALPN

### Objectives

* Implement TLS ALPN negotiation (`h2`)
* Validate HTTP/2 client/server preface exactly

### MUST

- [ ] Require TLS 1.2+
- [ ] Reject if ALPN ≠ `h2`
- [ ] Send and verify exact connection preface
- [ ] Fail fast on malformed preface

### Tests

* Invalid preface
* Partial preface
* Wrong ALPN
* Cleartext upgrade (if supported)

---

## Phase 3–4: Frame Parsing Core

### Objectives

Implement strict frame layer parser.

### MUST

- [ ] Parse 9-byte frame header exactly
- [ ] Enforce 24-bit length
- [ ] Validate frame type
- [ ] Validate stream ID rules
- [ ] Reject frames > SETTINGS_MAX_FRAME_SIZE

### MUST NOT

- [ ] Accept unknown flag combinations
- [ ] Accept invalid frame in stream state

### Tests

* Oversized frame
* Invalid type
* Stream ID misuse
* Zero-length violations

---

# 🟦 TIER 2 — Stream State Machine

---

## Phase 5–6: Full Stream Lifecycle

### Implement States

* idle
* open
* half-closed (local/remote)
* closed

### MUST

- [ ] Enforce valid transitions
- [ ] Reject invalid frame per state
- [ ] Auto-close stream on END_STREAM
- [ ] Send RST_STREAM when required

### Tests

* Frame on closed stream
* HEADERS on half-closed
* DATA before HEADERS

---

## Phase 7: GOAWAY & RST_STREAM Handling

### MUST

- [ ] Stop new streams after GOAWAY
- [ ] Process streams ≤ last-stream-id
- [ ] Immediately terminate on connection-level error
- [ ] Clean up stream resources

---

# 🟦 TIER 3 — SETTINGS & Flow Control

---

## Phase 8–9: SETTINGS Synchronization

### MUST

- [ ] Send SETTINGS immediately after preface
- [ ] Apply peer SETTINGS only after receipt
- [ ] Send SETTINGS ACK
- [ ] Validate:

    * MAX_CONCURRENT_STREAMS
    * INITIAL_WINDOW_SIZE
    * MAX_FRAME_SIZE
    * HEADER_TABLE_SIZE

### Tests

* Invalid SETTINGS value
* Missing ACK
* SETTINGS flood

---

## Phase 10–11: Flow Control Engine

### Implement

* Connection window
* Stream window

### MUST

- [ ] Track window sizes accurately
- [ ] Decrease window on DATA sent
- [ ] Send WINDOW_UPDATE when consuming data
- [ ] Reject overflow > 2^31-1

### MUST NOT

- [ ] Send DATA when window exhausted
- [ ] Allow window wraparound

### Tests

* Window exhaustion
* Window overflow
* Missing WINDOW_UPDATE

---

# 🟦 TIER 4 — HEADERS & DATA Semantics

---

## Phase 12–13: HEADERS Validation

### MUST

- [ ] Pseudo-headers first
- [ ] No duplicate pseudo-headers
- [ ] No uppercase header names
- [ ] No connection-specific headers
- [ ] Validate required pseudo-headers:

    * :method
    * :scheme
    * :path
    * :authority

### MUST NOT

- [ ] Allow pseudo-header after normal header
- [ ] Allow invalid ordering

---

## Phase 14: CONTINUATION Frames

### MUST

- [ ] Enforce END_HEADERS
- [ ] Require contiguous CONTINUATION frames
- [ ] Reject interleaved frames

---

# 🟦 TIER 5 — HPACK Core (RFC 7541)

---

## Phase 15–16: Static Table

### MUST

- [ ] Implement full static table
- [ ] Correct index resolution
- [ ] Reject invalid indices

---

## Phase 17–18: Dynamic Table Engine

### MUST

- [ ] FIFO eviction
- [ ] Track table size precisely
- [ ] Enforce HEADER_TABLE_SIZE limit
- [ ] Apply size updates only at allowed position

### MUST NOT

- [ ] Allow table size overflow
- [ ] Desync encoder/decoder

---

## Phase 19–20: Header Block Decoding

### Implement support for:

* Indexed representation
* Literal with incremental indexing
* Literal without indexing
* Never indexed
* Dynamic table size update

### MUST

- [ ] Decode prefix integers correctly
- [ ] Validate length fields
- [ ] Detect malformed encodings

---

# 🟦 TIER 6 — Huffman & Security

---

## Phase 21–22: Huffman Decoder

### MUST

- [ ] Implement canonical Huffman tree
- [ ] Reject:

    * Invalid code
    * EOS misuse
    * Overlong padding
    * Incomplete symbol

### Tests

* Random fuzzed Huffman
* Invalid bitstream
* Truncated symbol

---

## Phase 23: Header List Size Enforcement

### MUST

- [ ] Enforce MAX_HEADER_LIST_SIZE
- [ ] Abort stream if exceeded

---

# 🟦 TIER 7 — Advanced Robustness & Hardening

---

## Phase 24–25: Resource Exhaustion Protection

### MUST DEFEND AGAINST

* SETTINGS flood
* Rapid reset attack
* CONTINUATION flood
* PING flood
* Dynamic table abuse
* Stream ID exhaustion

---

## Phase 26: Error Mapping & Correct Codes

### MUST

- [ ] Distinguish stream vs connection errors
- [ ] Map correctly:

    * PROTOCOL_ERROR
    * FLOW_CONTROL_ERROR
    * FRAME_SIZE_ERROR
    * INTERNAL_ERROR
    * REFUSED_STREAM
    * CANCEL

---

# 🟦 TIER 8 — Integration Validation

---

## Phase 27–28: Cross-Component Validation

### Ensure

- [ ] HPACK failure → connection error
- [ ] Flow control independent from header decoding
- [ ] Stream cleanup on RST
- [ ] GOAWAY stops new stream creation
- [ ] No header injection via compression

---

# 🟦 TIER 9 — Stress & Fuzz Testing

---

## Phase 29: Fuzz Harness

### Include

* Random frame ordering
* Invalid lengths
* Invalid header encodings
* Window overflow attempts
* Table resizing storms

---

## Phase 30: High-Concurrency Validation

- [ ] 10k stream creation attempts
- [ ] Parallel header decoding
- [ ] Flow control saturation
- [ ] Connection teardown under load

---

# 🏁 Final Definition of Done

You are **fully RFC 9113 + RFC 7541 compliant** when:

- [ ] Frame parser rejects all malformed frames
- [ ] Stream state machine strictly enforced
- [ ] Flow control mathematically correct
- [ ] HPACK never desynchronizes
- [ ] Huffman decoder rejects invalid sequences
- [ ] No unbounded memory growth
- [ ] All MUST/MUST NOT satisfied
- [ ] Fuzz tests produce zero crashes
- [ ] No resource exhaustion vectors

---
