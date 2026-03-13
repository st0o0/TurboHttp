# TurboHttp Performance Analysis for High-Throughput HTTP Requests

This document provides a comprehensive analysis of architectural, conceptual, and implementation issues that may limit throughput when sending high volumes of HTTP requests to diverse API endpoints.

> **Note**: This analysis is based on code review. Some issues may already be addressed in later commits.

---

## Executive Summary

TurboHttp implements correct HTTP protocol handling with RFC compliance across HTTP/1.0, HTTP/1.1, HTTP/2, and HTTP/3. However, for high-throughput scenarios with diverse endpoints, several bottlenecks exist that will limit scalability.

**Critical findings:**
- Connection pool selection uses O(n) linear search
- HPACK static table lookups are O(n) per header
- Actor-based architecture introduces message-passing overhead
- Pipeline stages are not wired for production use
- Correlation stages have inefficient iteration patterns

---

## 1. Conceptual & Architectural Issues

### 1.1 Pipeline Not Fully Wired

**Location**: `Engine.cs` (lines 31-73), `TurboClientStreamManager.cs`

The protocol handler stages (RedirectHandler, CookieJar, RetryEvaluator, CacheFreshnessEvaluator, HttpCacheStore, DecompressionStage) exist as standalone classes with unit tests but are **NOT integrated** into the Akka.Streams pipeline.

```csharp
// Engine.cs:73 - Extended pipeline exists but is conditionally unused
return BuildExtendedPipeline(clientManager, options, requestOptionsFactory);
```

The `TurboClientStreamManager` has graph construction **commented out**, meaning `TurboHttpClient.SendAsync` does not work end-to-end.

**Impact**: Without the full pipeline, features like retry, redirect handling, cookie management, and caching are unavailable in production.

### 1.2 Connection Pool Architecture Mismatch for Diverse Endpoints

**Location**: `HostPoolActor.cs` (lines 93-130)

The connection pool is keyed by `host:port`. For a client hitting many different API endpoints on the same host (e.g., `api.example.com/users`, `api.example.com/orders`, `api.example.com/products`), HTTP/1.x connections can only pipeline requests sequentially on a single connection.

**Issue**: The pool assumes uniform traffic distribution but provides no endpoint-aware routing or request prioritization. Under high load to diverse endpoints:
- HTTP/1.1: Single connection per host queues requests (head-of-line blocking)
- HTTP/2: Multiplexing helps, but stream capacity limits apply

**Mitigation needed**: Consider endpoint-aware connection pooling or allow explicit connection affinity per endpoint.

### 1.3 No Connection Warmup

**Location**: `ConnectionStage.cs` (lines 125-128)

Connections are created on-demand via `TryConnect()`:

```csharp
private void TryConnect()
{
    if (_stopping || _options == null) return;
    _stage.ClientManager.Tell(new ClientManager.CreateTcpRunner(_options, _self));
}
```

For bursty high-throughput scenarios, this adds full TCP + TLS handshake latency to the first requests. A connection warmup mechanism would improve p99 latency.

### 1.4 Actor Model Trade-offs

**Location**: `HostPoolActor.cs`, `PoolRouterActor.cs`, `ClientManager.cs`

The Actor model provides isolation andbackpressure but introduces:
- **Message passing overhead**: Every request involves at least 2-3 actor messages
- **Serialization cost**: Messages must be serialized/deserialized between actors
- **Scheduling latency**: Actors are scheduled on a thread pool, introducing non-deterministic delays

For pure throughput (latency-bound scenarios), a direct channel-based approach without actors may outperform.

---

## 2. Performance-Critical Implementation Issues

### 2.1 O(n) Connection Selection in HostPoolActor

**Location**: `HostPoolActor.cs` (lines 105-121)

```csharp
private ConnectionState? SelectHttp1Connection(Version version)
{
    // HTTP/1.1: prefer idle, active, reusable connections
    return _connections.FirstOrDefault(x =>
        x is { Active: true, Idle: true, Reusable: true } &&
        x.PendingRequests < _config.MaxRequestsPerConnection);
}
```

**Issue**: Uses `List<ConnectionState>` with `FirstOrDefault()` - linear O(n) scan. With 100+ connections per host, this becomes a hotspot.

**Recommendation**: Use a `Dictionary<ConnectionState, ...>` with indexed lookups, or maintain separate "idle" and "active" lists.

### 2.2 O(n) HPACK Static Table Lookups

**Location**: `HpackEncoder.cs` (lines 378-408)

```csharp
private static int FindStaticFullMatch(string name, string value)
{
    for (var i = 1; i <= HpackStaticTable.StaticCount; i++)
    {
        var entry = HpackStaticTable.Entries[i];
        // ... linear search
    }
    return 0;
}
```

**Issue**: Every header encoding performs O(n) lookup in the static table (61 entries). For high-throughput scenarios with many unique headers, this adds up.

**Recommendation**: Create a `Dictionary<(string name, string value), int>` lookup for O(1) access. At minimum, index by header name first.

### 2.3 Http20CorrelationStage Inefficient Iteration

**Location**: `Http20CorrelationStage.cs` (lines 91-102)

```csharp
private void TryCorrelateAndEmit(Http20CorrelationStage stage)
{
    if (!IsAvailable(stage._out))
    {
        return;
    }

    foreach (var (streamId, response) in _waiting)  // Iterates ALL waiting
    {
        if (_pending.Remove(streamId, out var request))
        {
            _waiting.Remove(streamId);
            // ...
            return;  // Only processes ONE, then returns!
        }
    }
}
```

**Issue**: 
1. Iterates all waiting responses on every call (could be hundreds)
2. Only processes one match per invocation, then returns
3. Uses `foreach` which allocates an enumerator

For high concurrency with many concurrent streams, this creates CPU overhead.

### 2.4 Http1XCorrelationStage Queue Contention

**Location**: `Http1XCorrelationStage.cs` (lines 28-30)

```csharp
private readonly Queue<HttpRequestMessage> _pending = new();
private readonly Queue<HttpResponseMessage> _waiting = new();
```

Uses `Queue<T>` which, while not thread-bound like `ConcurrentQueue`, still has some overhead. More critically, the correlation logic:

```csharp
if (_pending.Count == 0)  // Check before enqueue
{
    _pending.Enqueue(Grab(stage._requestIn));
    TryCorrelateAndEmit(stage);
}
```

Only enqueues if the queue was empty, which could cause early backpressure. Under high load, this pattern may limit throughput.

### 2.5 PerHostConnectionLimiter Lock Contention

**Location**: `PerHostConnectionLimiter.cs` (lines 66-83)

```csharp
public bool TryAcquire(string host)
{
    var current = _active.GetValueOrDefault(host, 0);
    if (current >= _maxConnectionsPerHost)
    {
        return false;
    }

    _active[host] = current + 1;  // Non-atomic read-modify-write
    return true;
}
```

**Issue**: The read-modify-write pattern is **not atomic**. Under concurrent access, multiple threads could pass the check and over-count.

**Fix needed**: Use `ConcurrentDictionary.AddOrUpdate` or `Interlocked` operations for atomic increment.

### 2.6 Guid Allocation Overhead

**Location**: `TurboHttpClient.cs` (lines 76-78)

```csharp
var requestId = Guid.NewGuid();
request.Options.Set(_key, requestId);
_pending.TryAdd(requestId, tcs);
```

`Guid.NewGuid()` allocates a new GUID for every request. While convenient, this adds:
- ~16 bytes per request (GC pressure)
- RNG overhead on some platforms
- Dictionary lookup overhead (128-bit vs 64-bit keys)

For high-throughput scenarios, consider using `long` or `ulong` with an atomic counter.

### 2.7 Multiple Buffering Layers

**Location**: Various stages

Data flows through multiple internal buffers:
1. `Http1XCorrelationStage` - internal `Queue`
2. `Http20CorrelationStage` - internal `Dictionary`
3. `ConnectionStage` - internal `Queue` for pending reads/writes
4. Channel-based communication between client and actors

Each layer adds memory copies and potential backpressure points.

### 2.8 Engine Instance Creation Per Connection

**Location**: `Engine.cs` (lines 391-407)

```csharp
for (var i = 0; i < connectionCount; i++)
{
    // ...
    var conn = builder.Add(new TEngine().CreateFlow().Join(tcp));
}
```

A new engine instance (`new TEngine()`) is created for each connection. While Akka Streams may optimize this, pooling engines would reduce allocations.

---

## 3. Missing Optimizations for High Throughput

### 3.1 No Request Coalescing

When sending many small requests to the same host, batching requests into fewer HTTP/2 frames or HTTP/1.1 pipelined sequences could reduce round-trips. Currently, each request is processed individually.

### 3.2 No Priority Queue

All requests have equal priority. For diverse API endpoints, some may be latency-sensitive (e.g., health checks) while others are bulk (e.g., analytics). A priority mechanism is missing.

### 3.3 No Header Compression Warmup

HPACK dynamic table starts empty for each connection. With many short-lived connections, the compression ratio suffers. Consider sending static headers proactively.

### 3.4 No Zero-Copy Optimization for Response Body

**Location**: `Http11Decoder.cs`, `Http20DecoderStage.cs`

Response bodies are copied through multiple buffers. For large responses, zero-copy transfer (using `PinnedMemory` or `Span<byte>`) would reduce allocation overhead.

---

## 4. Summary of Recommendations

| Priority | Issue | Fix Complexity |
|----------|-------|-----------------|
| Critical | Pipeline not wired | Medium |
| High | O(n) connection selection | Low |
| High | O(n) HPACK static table | Low |
| High | Non-atomic connection limiter | Low |
| Medium | Http20CorrelationStage iteration | Medium |
| Medium | Guid overhead | Low |
| Low | Actor overhead | High |
| Low | No connection warmup | Medium |

---

## 5. Conclusion

TurboHttp has a solid foundation with correct RFC implementation. The current codebase is suitable for moderate throughput scenarios. However, for **high-throughput workloads with diverse API endpoints**, addressing the O(n) lookups and ensuring the full pipeline is wired will be essential for scalability.

The Actor-based architecture provides robustness but may not be optimal for the lowest-latency use cases. Consider offering a "low-latency" mode with direct channel-based communication for performance-critical paths.
