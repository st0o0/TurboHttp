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

## Phase 32-33: Http2Decoder — MAX_CONCURRENT_STREAMS

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

### Phase 35: Http10 (50+ tests)
- All methods (GET, POST, PUT, DELETE, HEAD, OPTIONS)
- With/without body
- Various headers
- Fragmented TCP reads
- Large bodies (streaming)

### Phase 36: Http11 (60+ tests)
- Content-Length scenarios
- Chunked transfer encoding
- Pipelined requests
- HEAD requests
- No-body responses (204, 304)
- Trailer headers
- Keep-alive vs. close

### Phase 37: Http2 (50+ tests)
- Connection preface + SETTINGS
- Pseudo-header validation
- Large headers (continuation frames)
- Sensitive headers
- Multiple responses
- Flow control
- HPACK synchronization

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
