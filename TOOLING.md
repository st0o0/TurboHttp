# TOOLING.md — TurboHttp Build, Test & Debug Guide

> **For Claude Code:** Quick reference for all build, test, and development commands.
> All paths are relative to the repo root `D:/GIT/Akka.Streams.Http/`.

---

## Build

```bash
# Restore packages
dotnet restore ./src/TurboHttp.sln

# Debug build
dotnet build ./src/TurboHttp.sln

# Release build (use for benchmarks)
dotnet build --configuration Release ./src/TurboHttp.sln
```

**Target framework:** `net10.0`
**Implicit usings:** disabled in `TurboHttp`; enabled in `TurboHttp.Tests`
**Nullable:** enabled (`#nullable enable`)

---

## Running Tests

### All Tests

```bash
dotnet test ./src/TurboHttp.sln
```

### Single Project

```bash
dotnet test ./src/TurboHttp.Tests/TurboHttp.Tests.csproj
```

### Filter by Class

```bash
dotnet test ./src/TurboHttp.Tests/TurboHttp.Tests.csproj \
  --filter "ClassName=TurboHttp.Tests.HpackTests"
```

### Filter by Method (partial name match)

```bash
dotnet test ./src/TurboHttp.Tests/TurboHttp.Tests.csproj \
  --filter "FullyQualifiedName~HpackTests.Encode_Decode_RoundTrip"
```

### Filter by RFC Test ID (DisplayName contains ID)

```bash
# Run all tests related to RFC 1945 encoder
dotnet test ./src/TurboHttp.Tests/TurboHttp.Tests.csproj \
  --filter "Name~1945-enc"

# Run a specific RFC test
dotnet test ./src/TurboHttp.Tests/TurboHttp.Tests.csproj \
  --filter "Name~1945-enc-001"
```

### Verbose Output (see test names)

```bash
dotnet test ./src/TurboHttp.Tests/TurboHttp.Tests.csproj \
  --logger "console;verbosity=normal"
```

### With Code Coverage

```bash
dotnet test ./src/TurboHttp.sln \
  /p:CollectCoverage=true \
  /p:CoverletOutputFormat=opencover \
  /p:CoverletOutput=./coverage/
```

---

## Test File Map

| Test File | Covers | Key RFC IDs |
|-----------|--------|-------------|
| `Http10EncoderTests.cs` | Request serialization | 1945-enc-* |
| `Http10DecoderTests.cs` | Response parsing | 1945-dec-* |
| `Http10RoundTripTests.cs` | Encode → decode | 1945-* |
| `Http11EncoderTests.cs` | Request serialization | 7230-enc-*, 9112-enc-* |
| `Http11DecoderTests.cs` | Response parsing | 7230-dec-*, 9112-dec-* |
| `Http2EncoderTests.cs` | Frame generation | 7540-enc-* |
| `Http2DecoderTests.cs` | Frame parsing | 7540-dec-* |
| `Http2FrameTests.cs` | Frame serialization | 7540-4.1-* |
| `HpackTests.cs` | Header compression | 7541-* |
| `HuffmanTests.cs` | Huffman codec | 7541-5.2-* |

---

## Writing New Tests

### Test Class Template

```csharp
#nullable enable

namespace TurboHttp.Tests;

public sealed class Http10EncoderTests
{
    // ── helper ────────────────────────────────────────────────────────────

    private static string Encode(HttpRequestMessage request)
    {
        var buffer = new byte[4096];
        var span = buffer.AsSpan();
        var written = Http10Encoder.Encode(request, ref span);
        return Encoding.ASCII.GetString(buffer, 0, written);
    }

    // ── RFC 1945 encoder tests ────────────────────────────────────────────

    [Fact]
    [DisplayName("1945-enc-001: Request-line must use HTTP/1.0")]
    public void Should_EmitHttp10RequestLine_When_EncodingGetRequest()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/path");
        var encoded = Encode(request);
        Assert.StartsWith("GET /path HTTP/1.0\r\n", encoded);
    }
}
```

### Naming Convention

```
[DisplayName("RFC-ID: Human readable description")]
Method name: Should_ExpectedBehavior_When_Condition
```

### Test Data Pattern (round-trip)

```csharp
[Fact]
[DisplayName("1945-rt-001: Round-trip GET preserves all fields")]
public void Should_PreserveRequestFields_When_RoundTripping()
{
    var original = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");

    // Encode
    var buffer = new byte[4096];
    var span = buffer.AsSpan();
    var written = Http10Encoder.Encode(original, ref span);

    // Decode
    var decoder = new Http10Decoder();
    var bytes = new ReadOnlyMemory<byte>(buffer, 0, written);
    var result = decoder.TryDecodeEof(bytes, out var response);

    Assert.True(result.IsSuccess);
    // ... assertions
}
```

---

## Project Structure Conventions

### Adding a New Source File

1. Place in `src/TurboHttp/Protocol/` (protocol logic) or `src/TurboHttp/IO/` (I/O layer)
2. Add `#nullable enable` at the top
3. Use `namespace TurboHttp.Protocol;` or `namespace TurboHttp.IO;`
4. Default to `sealed class` or `sealed record`

### Adding a New Test File

1. Place in `src/TurboHttp.Tests/`
2. Mirror the source file name: `Http11Decoder.cs` → `Http11DecoderTests.cs`
3. Use `namespace TurboHttp.Tests;`
4. `ImplicitUsings` is enabled in the test project

---

## Common Development Patterns

### Zero-Allocation Encoder Pattern

```csharp
// CORRECT: write directly to caller-provided span
public static int Encode(HttpRequestMessage request, ref Span<byte> buffer)
{
    var start = buffer;
    // write method, space, path, space, version, CRLF
    WriteAscii(ref buffer, "GET");
    WriteAscii(ref buffer, " ");
    // ...
    return start.Length - buffer.Length;
}

private static void WriteAscii(ref Span<byte> buf, string value)
{
    Encoding.ASCII.GetBytes(value, buf);
    buf = buf[value.Length..];
}
```

### Stateful Decoder Pattern

```csharp
public sealed class Http10Decoder : IDisposable
{
    private ReadOnlyMemory<byte> _remainder;

    public HttpDecodeResult TryDecode(ReadOnlyMemory<byte> data, out HttpResponseMessage? response)
    {
        var working = Combine(_remainder, data);
        // try to parse from `working`
        // on success: _remainder = leftover bytes
        // on incomplete: _remainder = working (keep all)
        // on failure: _remainder = default
    }

    public void Reset() => _remainder = default;
}
```

### ArrayPool Pattern

```csharp
var buffer = ArrayPool<byte>.Shared.Rent(size);
try
{
    // use buffer
}
finally
{
    ArrayPool<byte>.Shared.Return(buffer);
}
```

---

## Debugging Tips

### Inspect Encoded Bytes

```csharp
// Quick way to see what the encoder produces
var buffer = new byte[4096];
var span = buffer.AsSpan();
var n = Http11Encoder.Encode(request, ref span);
var text = Encoding.ASCII.GetString(buffer, 0, n);
Console.WriteLine(text);  // or use debugger watch
```

### Inspect HTTP/2 Frames

```csharp
// Dump frame bytes as hex
var hex = Convert.ToHexString(buffer.AsSpan(0, n));
// e.g.: "00000D0104000000017B3A6D6574686F6403474554"
```

### Common Error Patterns

| Error | Likely Cause |
|-------|-------------|
| `NeedMoreData` | TCP frame incomplete — more data expected |
| `InvalidStatusLine` | Response bytes don't start with `HTTP/` |
| `InvalidChunkedEncoding` | Malformed chunk size line |
| `HpackException` | Dynamic table out of sync — `Reset()` needed |
| `Http2Exception(PROTOCOL_ERROR)` | Wrong stream ID or invalid frame sequence |

---

## Dependencies & Versions

| Package | Version | Where |
|---------|---------|-------|
| .NET SDK | 10.0 | Both projects |
| Akka.Streams | 1.5.60 | TurboHttp (main) |
| Servus.Akka | 0.3.10 | TurboHttp (I/O layer) |
| xUnit | 2.9.3 | TurboHttp.Tests |
| xunit.runner.visualstudio | 3.1.5 | TurboHttp.Tests |
| Microsoft.NET.Test.Sdk | 18.0.1 | TurboHttp.Tests |
| Akka.Streams.TestKit | 1.5.60 | TurboHttp.Tests |
| coverlet.collector | 6.0.4 | TurboHttp.Tests |

---

## RFC Conformance Testing (External)

```bash
# HTTP/2 conformance suite (h2spec) — requires running server
h2spec -h localhost -p 8080

# curl smoke test against running instance
curl -v --http2 http://localhost:8080/

# h2load load test
h2load -n 1000 -c 10 http://localhost:8080/
```

---

## Git Workflow

```bash
# Current branch
git branch --show-current   # poc

# Main branch for PRs
git checkout main

# Stage specific files (never git add -A)
git add src/TurboHttp/Protocol/Http10Encoder.cs
git add src/TurboHttp.Tests/Http10EncoderTests.cs
```

---

## IDE Notes

- **Rider / Visual Studio:** solution file is `src/TurboHttp.sln`
- **VS Code:** open `src/` as workspace root for best IntelliSense
- **Test Explorer:** all tests appear under `TurboHttp.Tests` and `TurboHttp.IntegrationTests`
- **Nullable warnings:** enabled globally — fix all warnings, don't suppress without reason