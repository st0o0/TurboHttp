# Quick Reference Card & Code Templates

## 🎯 Zero-Allocation Patterns

### Pattern 1: Parsing with Span<byte>
```csharp
public bool TryParse(ReadOnlySpan<byte> input, out Result result)
{
    result = default;
    
    // Find delimiter
    var delimiterIndex = input.IndexOf((byte)' ');
    if (delimiterIndex == -1)
        return false;
    
    // Extract without allocation
    var firstPart = input[..delimiterIndex];
    var remaining = input[(delimiterIndex + 1)..];
    
    // Convert to string only when necessary
    result = new Result 
    { 
        FirstPart = Encoding.ASCII.GetString(firstPart)
    };
    
    return true;
}
```

### Pattern 2: Writing to Span<byte>
```csharp
public int WriteTo(Span<byte> destination)
{
    var bytesWritten = 0;
    
    // Write header (9 bytes)
    destination[0] = (byte)(length >> 16);
    destination[1] = (byte)(length >> 8);
    destination[2] = (byte)length;
    destination[3] = (byte)type;
    // ... rest of header
    
    bytesWritten += 9;
    
    // Write payload
    payload.CopyTo(destination[bytesWritten..]);
    bytesWritten += payload.Length;
    
    return bytesWritten;
}
```

### Pattern 3: Using ArrayPool
```csharp
public void ProcessLargeData(ReadOnlySpan<byte> input)
{
    var buffer = ArrayPool<byte>.Shared.Rent(input.Length);
    try
    {
        // Use buffer
        input.CopyTo(buffer);
        // Process...
    }
    finally
    {
        ArrayPool<byte>.Shared.Return(buffer);
    }
}
```

### Pattern 4: Stackalloc for Small Buffers
```csharp
public void ProcessSmallData(int size)
{
    // Only for small, known sizes (< 1KB)
    Span<byte> buffer = stackalloc byte[256];
    
    // Use buffer without allocation
    WriteFrameHeader(buffer);
}
```

---

## 📋 Test Templates

### Template 1: Simple Parsing Test
```csharp
[Theory]
[InlineData("valid input", true, "expected result")]
[InlineData("invalid input", false, null)]
[InlineData("", false, null)]
public void Parse_VariousInputs_ReturnsExpected(
    string input,
    bool shouldSucceed,
    string expectedResult)
{
    // Arrange
    var parser = new Parser();
    var bytes = Encoding.ASCII.GetBytes(input);
    
    // Act
    var success = parser.TryParse(bytes, out var result);
    
    // Assert
    Assert.Equal(shouldSucceed, success);
    if (shouldSucceed)
    {
        Assert.Equal(expectedResult, result);
    }
}
```

### Template 2: RFC Conformance Test
```csharp
public static TheoryData<string, string, bool> RFC7230_Section3_1_1_TestCases()
{
    return new TheoryData<string, string, bool>
    {
        // Test ID, Input, Should Pass
        { "7230-3.1.1-001", "GET / HTTP/1.1\r\n", true },
        { "7230-3.1.1-002", "get / HTTP/1.1\r\n", false },
        { "7230-3.1.1-003", "GET/ HTTP/1.1\r\n", false },
        // Add all RFC test cases
    };
}

[Theory]
[MemberData(nameof(RFC7230_Section3_1_1_TestCases))]
public void RFC7230_Section3_1_1_Conformance(
    string testId,
    string input,
    bool shouldPass)
{
    // Arrange
    var parser = new Http11Parser();
    var bytes = Encoding.ASCII.GetBytes(input);
    
    // Act
    var result = parser.TryParseRequestLine(bytes, out var requestLine);
    
    // Assert
    Assert.Equal(shouldPass, result);
}
```

### Template 3: Round-Trip Test
```csharp
[Fact]
public void EncodeAndDecode_Request_PreservesData()
{
    // Arrange
    var originalRequest = new HttpRequest
    {
        Method = "POST",
        Path = "/api/users",
        Version = "HTTP/1.1",
        Headers = new[] 
        { 
            ("Host", "example.com"),
            ("Content-Type", "application/json")
        },
        Body = Encoding.UTF8.GetBytes("{\"name\":\"test\"}")
    };
    
    var encoder = new Http11Encoder();
    var decoder = new Http11Decoder();
    
    // Act - Encode
    var buffer = new byte[4096];
    var bytesWritten = encoder.Encode(originalRequest, buffer);
    
    // Act - Decode
    var decoded = decoder.TryDecode(buffer.AsSpan(0, bytesWritten), out var decodedRequest);
    
    // Assert
    Assert.True(decoded);
    Assert.Equal(originalRequest.Method, decodedRequest.Method);
    Assert.Equal(originalRequest.Path, decodedRequest.Path);
    Assert.Equal(originalRequest.Version, decodedRequest.Version);
    Assert.Equal(originalRequest.Body, decodedRequest.Body);
}
```

### Template 4: Property-Based Test (FsCheck)
```csharp
[Property]
public Property Parse_ValidMethod_NeverThrows(NonEmptyString method)
{
    // Arrange
    var validMethod = new string(method.Get
        .Where(c => c >= 'A' && c <= 'Z')
        .ToArray());
    
    if (string.IsNullOrEmpty(validMethod))
        return true.ToProperty();
    
    var input = $"{validMethod} / HTTP/1.1\r\n";
    var parser = new Http11Parser();
    var bytes = Encoding.ASCII.GetBytes(input);
    
    // Act & Assert
    return Prop.ForAll(
        () => {
            try 
            {
                parser.TryParseRequestLine(bytes, out _);
                return true;
            }
            catch
            {
                return false;
            }
        });
}
```

### Template 5: Performance Test (BenchmarkDotNet)
```csharp
[MemoryDiagnoser]
public class ParserBenchmarks
{
    private byte[] _simpleRequest;
    private Http11Parser _parser;
    
    [GlobalSetup]
    public void Setup()
    {
        _simpleRequest = Encoding.ASCII.GetBytes(
            "GET / HTTP/1.1\r\nHost: example.com\r\n\r\n");
        _parser = new Http11Parser();
    }
    
    [Benchmark]
    public bool ParseSimpleRequest()
    {
        return _parser.TryParseRequest(_simpleRequest, out _);
    }
    
    [Benchmark]
    public bool ParseSimpleRequest_WithAllocation()
    {
        // Compare with allocation version
        var parser = new Http11Parser();
        return parser.TryParseRequest(_simpleRequest, out _);
    }
}
```

---

## 🔍 Common Parsing Patterns

### Pattern: Find CRLF
```csharp
private static int IndexOfCRLF(ReadOnlySpan<byte> span)
{
    for (int i = 0; i < span.Length - 1; i++)
    {
        if (span[i] == (byte)'\r' && span[i + 1] == (byte)'\n')
            return i;
    }
    return -1;
}

// Or use vectorized search (faster)
private static int IndexOfCRLF_Fast(ReadOnlySpan<byte> span)
{
    var cr = (byte)'\r';
    var index = span.IndexOf(cr);
    
    while (index != -1 && index < span.Length - 1)
    {
        if (span[index + 1] == (byte)'\n')
            return index;
        
        index = span[(index + 1)..].IndexOf(cr);
        if (index != -1)
            index += (span.Length - (span.Length - index - 1));
    }
    
    return -1;
}
```

### Pattern: Parse Header Field
```csharp
private static bool TryParseHeaderField(
    ReadOnlySpan<byte> line,
    out ReadOnlySpan<byte> name,
    out ReadOnlySpan<byte> value)
{
    name = default;
    value = default;
    
    // Find colon
    var colonIndex = line.IndexOf((byte)':');
    if (colonIndex == -1)
        return false;
    
    // Extract name (before colon)
    name = line[..colonIndex];
    
    // Extract value (after colon, trim whitespace)
    var valueSpan = line[(colonIndex + 1)..];
    value = TrimWhitespace(valueSpan);
    
    return true;
}

private static ReadOnlySpan<byte> TrimWhitespace(ReadOnlySpan<byte> span)
{
    // Trim leading
    while (!span.IsEmpty && IsWhitespace(span[0]))
        span = span[1..];
    
    // Trim trailing
    while (!span.IsEmpty && IsWhitespace(span[^1]))
        span = span[..^1];
    
    return span;
}

private static bool IsWhitespace(byte b) => b == ' ' || b == '\t';
```

### Pattern: Parse Integer
```csharp
private static bool TryParseInt32(ReadOnlySpan<byte> span, out int value)
{
    value = 0;
    
    if (span.IsEmpty)
        return false;
    
    foreach (var b in span)
    {
        if (b < '0' || b > '9')
            return false;
        
        // Check overflow
        if (value > (int.MaxValue - (b - '0')) / 10)
            return false;
        
        value = value * 10 + (b - '0');
    }
    
    return true;
}
```

### Pattern: Parse Chunked Size (Hex)
```csharp
private static bool TryParseChunkSize(
    ReadOnlySpan<byte> span,
    out int size)
{
    size = 0;
    
    if (span.IsEmpty)
        return false;
    
    foreach (var b in span)
    {
        int digit;
        if (b >= '0' && b <= '9')
            digit = b - '0';
        else if (b >= 'A' && b <= 'F')
            digit = b - 'A' + 10;
        else if (b >= 'a' && b <= 'f')
            digit = b - 'a' + 10;
        else
            break; // Stop at non-hex char (could be chunk extension)
        
        // Check overflow
        if (size > (int.MaxValue - digit) / 16)
            return false;
        
        size = size * 16 + digit;
    }
    
    return size >= 0;
}
```

---

## 🎯 HTTP/2 Frame Writing Patterns

### Pattern: Write Frame Header
```csharp
private static void WriteFrameHeader(
    Span<byte> destination,
    int payloadLength,
    FrameType type,
    byte flags,
    int streamId)
{
    // Length (24-bit)
    destination[0] = (byte)(payloadLength >> 16);
    destination[1] = (byte)(payloadLength >> 8);
    destination[2] = (byte)payloadLength;
    
    // Type (8-bit)
    destination[3] = (byte)type;
    
    // Flags (8-bit)
    destination[4] = flags;
    
    // Stream ID (31-bit, R-bit must be 0)
    destination[5] = (byte)((streamId >> 24) & 0x7F);
    destination[6] = (byte)(streamId >> 16);
    destination[7] = (byte)(streamId >> 8);
    destination[8] = (byte)streamId;
}
```

### Pattern: HPACK Integer Encoding
```csharp
private static int EncodeInteger(
    Span<byte> destination,
    int value,
    int prefixBits)
{
    var prefixMax = (1 << prefixBits) - 1;
    
    if (value < prefixMax)
    {
        // Fits in prefix
        destination[0] |= (byte)value;
        return 1;
    }
    
    // Doesn't fit in prefix
    destination[0] |= (byte)prefixMax;
    value -= prefixMax;
    
    var bytesWritten = 1;
    while (value >= 128)
    {
        destination[bytesWritten] = (byte)((value % 128) + 128);
        value /= 128;
        bytesWritten++;
    }
    
    destination[bytesWritten] = (byte)value;
    return bytesWritten + 1;
}
```

---

## 🚨 Common Pitfalls & Solutions

### Pitfall 1: String Allocations in Hot Path
❌ **BAD:**
```csharp
public void ParseHeaders(byte[] data)
{
    var line = Encoding.ASCII.GetString(data); // Allocation!
    var parts = line.Split(':'); // More allocations!
}
```

✅ **GOOD:**
```csharp
public void ParseHeaders(ReadOnlySpan<byte> data)
{
    var colonIndex = data.IndexOf((byte)':');
    var name = data[..colonIndex];
    var value = data[(colonIndex + 1)..];
    // No allocations!
}
```

### Pitfall 2: Forgetting to Return ArrayPool Buffers
❌ **BAD:**
```csharp
public void Process()
{
    var buffer = ArrayPool<byte>.Shared.Rent(1024);
    // ... use buffer ...
    // Forgot to return! Memory leak!
}
```

✅ **GOOD:**
```csharp
public void Process()
{
    var buffer = ArrayPool<byte>.Shared.Rent(1024);
    try
    {
        // ... use buffer ...
    }
    finally
    {
        ArrayPool<byte>.Shared.Return(buffer);
    }
}
```

### Pitfall 3: Not Checking Bounds
❌ **BAD:**
```csharp
public void ParseHeader(ReadOnlySpan<byte> data)
{
    var value = data[10..20]; // Might be out of bounds!
}
```

✅ **GOOD:**
```csharp
public bool TryParseHeader(ReadOnlySpan<byte> data)
{
    if (data.Length < 20)
        return false;
    
    var value = data[10..20]; // Safe!
    return true;
}
```

### Pitfall 4: Modifying ReadOnlySpan
❌ **BAD:**
```csharp
public void Process(ReadOnlySpan<byte> data)
{
    // This won't compile!
    // data[0] = 0;
}
```

✅ **GOOD:**
```csharp
public void Process(ReadOnlySpan<byte> data)
{
    // If you need to modify, copy to writable span
    Span<byte> writable = stackalloc byte[data.Length];
    data.CopyTo(writable);
    writable[0] = 0;
}
```

---

## 📊 Performance Optimization Checklist

### Before Optimizing:
- [ ] Benchmark first (measure baseline)
- [ ] Identify bottleneck (profiler)
- [ ] Set target metric (e.g., "reduce allocations by 50%")

### Optimization Techniques:
1. **Use Span<T> instead of arrays**
   - Reduces allocations
   - Enables slicing without copy

2. **Use ArrayPool for temporary buffers**
   - Reuses memory
   - Reduces GC pressure

3. **Use stackalloc for small buffers** (< 1KB)
   - Zero allocations
   - Fastest option

4. **Avoid LINQ in hot paths**
   - Use for loops instead
   - Cache enumerators

5. **Cache static data**
   - Static lookup tables
   - Compiled regex
   - Pre-computed values

6. **Use struct instead of class** (when appropriate)
   - Value types avoid heap allocation
   - Be careful with copying

7. **Avoid boxing**
   - Don't cast value types to object
   - Use generic constraints

8. **Use ValueTask for async** (when often completing synchronously)
   - Reduces allocation
   - Faster than Task

### After Optimizing:
- [ ] Benchmark again (verify improvement)
- [ ] Verify correctness (all tests pass)
- [ ] Check memory usage (no leaks)
- [ ] Document optimization rationale

---

## 🔧 Debugging Cheat Sheet

### Print Binary Data:
```csharp
private static string ToHex(ReadOnlySpan<byte> data)
{
    return string.Join(" ", data.ToArray().Select(b => b.ToString("X2")));
}

// Usage:
Console.WriteLine($"Data: {ToHex(data)}");
// Output: Data: 48 54 54 50 2F 31 2E 31 0D 0A
```

### Inspect Frame:
```csharp
private static void DebugFrame(ReadOnlySpan<byte> frame)
{
    if (frame.Length < 9)
    {
        Console.WriteLine("Frame too short!");
        return;
    }
    
    var length = (frame[0] << 16) | (frame[1] << 8) | frame[2];
    var type = (FrameType)frame[3];
    var flags = frame[4];
    var streamId = ((frame[5] & 0x7F) << 24) | (frame[6] << 16) | (frame[7] << 8) | frame[8];
    
    Console.WriteLine($"Frame: Type={type}, Length={length}, Flags=0x{flags:X2}, Stream={streamId}");
}
```

### Measure Allocations:
```csharp
[MemoryDiagnoser]
public class AllocationTest
{
    [Benchmark]
    public void MyMethod()
    {
        // Your code here
    }
}

// Run: dotnet run -c Release
// Check: Allocated column in results
```

---

## 📚 Quick Reference Table

### HTTP/1.1 Status Codes:
| Code | Meaning | Body? |
|------|---------|-------|
| 1xx | Informational | No |
| 200 | OK | Usually |
| 204 | No Content | No |
| 301 | Moved Permanently | Optional |
| 304 | Not Modified | No |
| 400 | Bad Request | Usually |
| 404 | Not Found | Usually |
| 500 | Internal Server Error | Usually |

### HTTP/2 Frame Types:
| Type | Value | Description |
|------|-------|-------------|
| DATA | 0x0 | Carries request/response payload |
| HEADERS | 0x1 | Contains HTTP headers |
| PRIORITY | 0x2 | Specifies stream priority |
| RST_STREAM | 0x3 | Terminates a stream |
| SETTINGS | 0x4 | Connection configuration |
| PUSH_PROMISE | 0x5 | Server push notification |
| PING | 0x6 | Liveness check |
| GOAWAY | 0x7 | Connection termination |
| WINDOW_UPDATE | 0x8 | Flow control |
| CONTINUATION | 0x9 | Continuation of HEADERS |

### HTTP/2 Error Codes:
| Code | Value | Description |
|------|-------|-------------|
| NO_ERROR | 0x0 | Graceful shutdown |
| PROTOCOL_ERROR | 0x1 | Protocol violation |
| INTERNAL_ERROR | 0x2 | Implementation fault |
| FLOW_CONTROL_ERROR | 0x3 | Flow control violation |
| FRAME_SIZE_ERROR | 0x6 | Frame size invalid |
| STREAM_CLOSED | 0x5 | Frame on closed stream |
| COMPRESSION_ERROR | 0x9 | HPACK compression error |

---

**Keep this handy while coding! 📌**
