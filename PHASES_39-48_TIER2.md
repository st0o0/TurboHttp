# TIER 2: PERFORMANCE — Phases 39-48
## Detailed Implementation Guide

---

## Phase 39-40: Http11Decoder — Streaming Chunk Parser

**Location**: `src/TurboHttp/Protocol/Http11Decoder.cs`
**RFC**: RFC 9112 §6.3
**Effort**: 2 weeks total

### Phase 39: API Design

Add public interface:
```csharp
public class Http11Decoder : IDisposable
{
    /// <summary>
    /// Decodes HTTP/1.1 chunked responses as an async enumerable of chunks.
    /// Memory-efficient: does not buffer entire response in memory.
    /// </summary>
    public async IAsyncEnumerable<ReadOnlyMemory<byte>> DecodeChunkedAsync(
        IAsyncEnumerable<ReadOnlyMemory<byte>> networkStream,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // Placeholder for Phase 40
        throw new NotImplementedException();
    }
}
```

20 contract tests documenting behavior:
- Yields chunks in order
- Handles trailers after final chunk
- Memory usage constant (not O(n))
- Cancellation support
- Error on invalid chunks

### Phase 40: Implementation

```csharp
public async IAsyncEnumerable<ReadOnlyMemory<byte>> DecodeChunkedAsync(
    IAsyncEnumerable<ReadOnlyMemory<byte>> networkStream,
    [EnumeratorCancellation] CancellationToken cancellationToken = default)
{
    ClearBody();
    var pos = 0;
    var buffer = new byte[65536];
    var bufferLength = 0;
    var working = ReadOnlyMemory<byte>.Empty;

    await foreach (var chunk in networkStream.WithCancellation(cancellationToken))
    {
        // Combine with previous remainder
        if (bufferLength > 0)
        {
            Array.Copy(buffer, 0, buffer, chunk.Length, bufferLength);
            chunk.Span.CopyTo(buffer);
            bufferLength += chunk.Length;
            working = buffer.AsMemory(0, bufferLength);
        }
        else
        {
            working = chunk;
        }

        pos = 0;

        while (pos < working.Length)
        {
            var lineEnd = FindCrlf(working.Span, pos);
            if (lineEnd < 0)
                break;

            var sizeLine = working.Slice(pos, lineEnd - pos);
            var semiIdx = sizeLine.Span.IndexOf((byte)';');
            var sizeSpan = semiIdx >= 0 ? sizeLine[..semiIdx] : sizeLine;

            if (!TryParseHex(sizeSpan.Span, out var chunkSize))
            {
                throw new HttpDecoderException(HttpDecodeError.InvalidChunkSize);
            }

            pos = lineEnd + 2;

            if (chunkSize == 0)
            {
                // Final chunk
                var remaining = working[pos..];
                var trailerEnd = FindCrlfCrlf(remaining.Span);

                if (trailerEnd >= 0)
                {
                    // Trailers present (but we ignore for now)
                    yield break;
                }

                throw new HttpDecoderException(HttpDecodeError.InvalidChunkFormat);
            }

            // Need chunk data + CRLF
            if (pos + chunkSize + 2 > working.Length)
            {
                // Not enough data yet
                bufferLength = working.Length - pos;
                Array.Copy(working.Span[pos..].ToArray(), buffer, bufferLength);
                break;
            }

            // Yield chunk data
            yield return working.Slice(pos, chunkSize);

            pos += chunkSize + 2;  // Skip chunk data and CRLF
        }

        // Keep unprocessed data for next iteration
        if (pos < working.Length)
        {
            bufferLength = working.Length - pos;
            Array.Copy(working.Span[pos..].ToArray(), buffer, bufferLength);
        }
        else
        {
            bufferLength = 0;
        }
    }
}
```

### Tests: 30+ (Phase 40)
- 1KB chunks
- 100KB chunks
- 1GB+ stream (memory constant)
- Trailers
- Error conditions
- Cancellation token respected

---

## Phase 41: All Decoders — SIMD CRLF Detection

**Location**: `src/TurboHttp/Protocol/Http11Decoder.cs` (and others)
**Target**: >20% faster
**Effort**: 1 week

### Current Implementation (Line 746)
```csharp
private static int FindCrlfCrlf(ReadOnlySpan<byte> span)
{
    for (var i = 0; i <= span.Length - 4; i++)
    {
        if (span[i] == '\r' && span[i + 1] == '\n' &&
            span[i + 2] == '\r' && span[i + 3] == '\n')
        {
            return i;
        }
    }
    return -1;
}
```

### SIMD Version
```csharp
private static int FindCrlfCrlf(ReadOnlySpan<byte> span)
{
    if (Vector.IsHardwareAccelerated && span.Length >= Vector<byte>.Count)
    {
        var crlfcrlfPattern = new Vector<byte>(new byte[]
        {
            (byte)'\r', (byte)'\n', (byte)'\r', (byte)'\n',
            0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0
        });

        for (int i = 0; i <= span.Length - 4; i += Vector<byte>.Count)
        {
            if (i + Vector<byte>.Count <= span.Length)
            {
                var v = new Vector<byte>(span.Slice(i));
                // SIMD comparison (implementation depends on platform)
                // For now: use scalar fallback
            }
        }
    }

    // Fallback to scalar (always correct)
    return FindCrlfCrlfScalar(span);
}

private static int FindCrlfCrlfScalar(ReadOnlySpan<byte> span)
{
    for (var i = 0; i <= span.Length - 4; i++)
    {
        if (span[i] == '\r' && span[i + 1] == '\n' &&
            span[i + 2] == '\r' && span[i + 3] == '\n')
        {
            return i;
        }
    }
    return -1;
}
```

### Benchmarks
- Baseline: `FindCrlfCrlf()` on 10KB, 100KB, 1MB payloads
- SIMD: Measure improvement
- Target: >20% faster
- Validate: Same results (correctness)

---

## Phase 42: Profiling & Benchmark Baseline

**Location**: `docs/PERFORMANCE.md` (NEW)
**Effort**: 1 week

### Tasks
1. Profile each encoder/decoder:
   - CPU usage (hotspots via BenchmarkDotNet)
   - Memory allocation (GC allocations)
   - Latency distribution (P50, P95, P99, P999)

2. Benchmark command:
```bash
dotnet run --configuration Release --project src/TurboHttp.Benchmarks \
  -- --filter "*Encoder*|*Decoder*" --exportjson baseline.json
```

3. Document baseline in `docs/PERFORMANCE.md`:
```markdown
# Performance Baseline (Phase 42)

## Http10Encoder
- Throughput: X requests/sec
- Memory: Y MB/sec allocation
- P99 latency: Z µs

## Http10Decoder
- Throughput: X requests/sec
- Memory: Y MB/sec allocation
- P99 latency: Z µs

[... rest of components ...]
```

### Validation
- Baseline established
- Reproducible on CI
- Future phases compare against this

---

## Phase 43-44: Http2Encoder — Large Header Streaming

**Location**: `src/TurboHttp/Protocol/HpackEncoder.cs`
**RFC**: RFC 7541
**Effort**: 2 weeks

### Phase 43: API Design

Design streaming HPACK interface:
```csharp
public class HpackEncoder(bool useHuffman = true)
{
    /// <summary>
    /// Encodes headers with streaming support for large header sets.
    /// Avoids buffering entire header block in memory.
    /// </summary>
    public async IAsyncEnumerable<ReadOnlyMemory<byte>> EncodeAsync(
        List<(string, string)> headers,
        HashSet<int>? sensitiveIndices = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // Placeholder
        throw new NotImplementedException();
    }
}
```

### Phase 44: Implementation

```csharp
public async IAsyncEnumerable<ReadOnlyMemory<byte>> EncodeAsync(
    List<(string, string)> headers,
    HashSet<int>? sensitiveIndices = null,
    [EnumeratorCancellation] CancellationToken cancellationToken = default)
{
    const int chunkSize = 4096;
    var buffer = new List<byte>(chunkSize);

    for (int i = 0; i < headers.Count; i++)
    {
        var (name, value) = headers[i];
        var isSensitive = sensitiveIndices?.Contains(i) ?? false;

        EncodeHeader(buffer, name, value, isSensitive);

        // Yield when buffer reaches chunk size
        if (buffer.Count >= chunkSize)
        {
            yield return buffer.ToArray().AsMemory();
            buffer.Clear();
        }

        if (cancellationToken.IsCancellationRequested)
            yield break;
    }

    // Final chunk
    if (buffer.Count > 0)
    {
        yield return buffer.ToArray().AsMemory();
    }
}
```

### Tests: 35+
- 1KB header set
- 10KB header set
- 100KB header set
- Memory usage constant

---

## Phase 45: Concurrent Decoder Testing

**Location**: `src/TurboHttp.Tests/Http11DecoderConcurrentTests.cs` (NEW)
**Effort**: 1 week

### Tasks
1. Create concurrent test harness:
   ```csharp
   [Fact]
   public void ConcurrentDecode_100Requests_AllCorrect()
   {
       var decoder = new Http11Decoder();  // Shared instance?
       var tasks = new Task[100];

       for (int i = 0; i < 100; i++)
       {
           tasks[i] = Task.Run(() =>
           {
               var buffer = BuildValidHttpResponse();
               var result = decoder.TryDecode(buffer.AsMemory(), out var responses);
               Assert.True(result);
           });
       }

       Task.WaitAll(tasks);
   }
   ```

2. Stress test (1000+ concurrent operations)
3. Check for:
   - Race conditions
   - Deadlocks
   - Memory safety
   - Correct results

### Tests: 40+

---

## Phase 46: Memory Profiling & Optimization

**Location**: All decoders
**Effort**: 1 week

### Tasks
1. Profile allocations:
   ```bash
   dotnet test src/TurboHttp.Tests/TurboHttp.Tests.csproj \
     --filter "*Decoder*" --logger "console;verbosity=detailed"
   ```

2. Identify hot paths (high allocation):
   - String allocations (use spans instead)
   - List allocations (ArrayPool)
   - Byte[] allocations (reuse)

3. Optimize:
   - Replace `Encoding.ASCII.GetString()` with span-based
   - Use ArrayPool for temporary buffers
   - Reduce LINQ allocations

4. Measure GC impact:
   ```bash
   dotnet run --project src/TurboHttp.Benchmarks \
     -- --filter "*Decoder*" --memoryDiagnoser
   ```

---

## Phase 47: P99 Latency Optimization

**Location**: Hot decoder/encoder paths
**Effort**: 1 week
**Target**: P99 reduced >10%

### Tasks
1. Measure baseline P99:
   ```bash
   dotnet run --project src/TurboHttp.Benchmarks -- --percentiles 99 --job dry
   ```

2. Profile latency hotspots
3. Optimize:
   - Reduce allocations per request
   - Optimize loops (avoid LINQ)
   - Cache frequently used values
   - Inline small methods

4. Benchmark improvement:
   ```bash
   dotnet run --project src/TurboHttp.Benchmarks -- --percentiles 50,99,999
   ```

---

## Phase 48: Validation Gate ✅

**Objective**: Confirm all Tier 2 optimizations working

### Tasks
- [ ] Benchmark suite vs. Phase 42 baseline
- [ ] Performance improvement measured
- [ ] <5% regression on any path (acceptable)
- [ ] Concurrent tests pass
- [ ] Memory profile improved
- [ ] Commit: "Complete Tier 2: Client-Side Performance (10 Phases)"

### Validation Checklist
- [ ] All benchmarks green
- [ ] Performance improved
- [ ] No regressions
- [ ] Documented improvements
- [ ] Ready for Tier 3

**Definition of Done**: **Tier 2 COMPLETE ✅**
