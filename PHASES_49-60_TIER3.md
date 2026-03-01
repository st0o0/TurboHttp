# TIER 3: FEATURES — Phases 49-60
## Detailed Implementation Guide

---

## Phase 49-50: Content-Encoding Decompression

**Location**: `src/TurboHttp/Features/DecompressionHandler.cs` (NEW)
**Effort**: 2 weeks
**Formats**: gzip, deflate, br, identity

### Phase 49: API Design

```csharp
public class DecompressionHandler : DelegatingHandler
{
    private readonly HashSet<string> _supportedEncodings = new(StringComparer.OrdinalIgnoreCase)
    {
        "gzip", "deflate", "br", "identity"
    };

    public DecompressionHandler(HttpMessageHandler? innerHandler = null)
        : base(innerHandler ?? new HttpClientHandler()) { }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var response = await base.SendAsync(request, cancellationToken);

        if (!response.Content.Headers.ContentEncoding.Any())
            return response;

        var encoding = response.Content.Headers.ContentEncoding.FirstOrDefault()?.ToLowerInvariant();

        if (!_supportedEncodings.Contains(encoding ?? string.Empty))
            return response;

        // Decompress
        var compressedData = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        var decompressedData = encoding switch
        {
            "gzip" => DecompressGzip(compressedData),
            "deflate" => DecompressDeflate(compressedData),
            "br" => DecompressBrotli(compressedData),
            "identity" => compressedData,
            _ => compressedData
        };

        // Replace content
        response.Content = new ByteArrayContent(decompressedData);

        // Copy headers
        foreach (var header in response.Content.Headers)
        {
            response.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        // Remove Content-Encoding header
        response.Content.Headers.ContentEncoding.Clear();

        // Update Content-Length
        response.Content.Headers.ContentLength = decompressedData.Length;

        return response;
    }

    private static byte[] DecompressGzip(byte[] compressedData)
    {
        using var compressedStream = new MemoryStream(compressedData);
        using var gzipStream = new GZipStream(compressedStream, CompressionMode.Decompress);
        using var decompressedStream = new MemoryStream();

        gzipStream.CopyTo(decompressedStream);
        return decompressedStream.ToArray();
    }

    private static byte[] DecompressDeflate(byte[] compressedData)
    {
        using var compressedStream = new MemoryStream(compressedData);
        using var deflateStream = new DeflateStream(compressedStream, CompressionMode.Decompress);
        using var decompressedStream = new MemoryStream();

        deflateStream.CopyTo(decompressedStream);
        return decompressedStream.ToArray();
    }

    private static byte[] DecompressBrotli(byte[] compressedData)
    {
        // Requires System.Net.Http.Brotli or Brotli nuget
        // For now: not implemented (optional)
        throw new NotSupportedException("Brotli decompression requires additional package");
    }
}
```

### Phase 50: Integration

Register in HttpClientBuilder:
```csharp
public static IHttpClientBuilder AddDecompression(this IHttpClientBuilder builder)
{
    return builder.ConfigureHttpClient(client =>
    {
        // Ensure decompression handler is in pipeline
    }).ConfigureHttpMessageHandlerBuilder(handlerBuilder =>
    {
        handlerBuilder.PrimaryHandler = new DecompressionHandler(handlerBuilder.PrimaryHandler);
    });
}
```

### Tests: 30+
- gzip decompression
- deflate decompression
- No compression (identity)
- Multiple encodings
- Invalid compressed data
- Large responses (100MB+)

---

## Phase 51-52: Redirect Following

**Location**: `src/TurboHttp/Features/RedirectHandler.cs` (NEW)
**Effort**: 2 weeks
**Status Codes**: 301, 302, 307, 308

### Phase 51: API Design

```csharp
public class RedirectHandler : DelegatingHandler
{
    public int MaxRedirects { get; set; } = 10;
    public bool AllowUnsafeRedirects { get; set; } = false;

    public RedirectHandler(HttpMessageHandler? innerHandler = null)
        : base(innerHandler ?? new HttpClientHandler()) { }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        int redirectCount = 0;
        HttpResponseMessage response = null!;

        while (redirectCount < MaxRedirects)
        {
            response = await base.SendAsync(request, cancellationToken);

            if (!IsRedirectStatus(response.StatusCode))
                return response;

            var location = response.Headers.Location;
            if (location == null)
                throw new HttpRequestException("Redirect response missing Location header");

            redirectCount++;

            // Handle method changes based on status code
            var newMethod = GetRedirectMethod((int)response.StatusCode, request.Method);
            var newRequest = new HttpRequestMessage(newMethod, location);

            // Copy headers (except Host)
            foreach (var header in request.Headers)
            {
                if (!header.Key.Equals("Host", StringComparison.OrdinalIgnoreCase))
                {
                    newRequest.Headers.TryAddWithoutValidation(header.Key, header.Value);
                }
            }

            request = newRequest;
            response.Dispose();
        }

        throw new HttpRequestException($"Too many redirects (max {MaxRedirects})");
    }

    private static bool IsRedirectStatus(HttpStatusCode code)
    {
        return code is HttpStatusCode.MovedPermanently or       // 301
                      HttpStatusCode.Found or                  // 302
                      HttpStatusCode.TemporaryRedirect or       // 307
                      (HttpStatusCode)308;                      // 308
    }

    private static HttpMethod GetRedirectMethod(int statusCode, HttpMethod originalMethod)
    {
        return statusCode switch
        {
            301 or 302 => HttpMethod.Get,  // Change to GET unless original was HEAD
            307 or 308 => originalMethod,  // Preserve method
            _ => originalMethod
        };
    }
}
```

### Phase 52: Integration & Tests

Tests: 25+
- 301 redirect (method changes to GET)
- 302 redirect (method changes to GET)
- 307 redirect (method preserved)
- 308 redirect (method preserved)
- Redirect chain (301 → 302 → 200)
- Loop detection (too many redirects)
- Missing Location header

---

## Phase 53-54: Cookie Management

**Location**: `src/TurboHttp/Features/CookieJarHandler.cs` (NEW)
**Effort**: 2 weeks
**RFC**: RFC 6265

### Phase 53: Cookie Storage

```csharp
public class CookieJar
{
    private readonly Dictionary<string, Dictionary<string, HttpCookie>> _cookies = new();
    private readonly object _lock = new();

    public void Add(Uri uri, string setCookieHeader)
    {
        lock (_lock)
        {
            var cookie = ParseSetCookie(setCookieHeader);
            var key = GetKey(uri);

            if (!_cookies.TryGetValue(key, out var hostCookies))
            {
                hostCookies = new Dictionary<string, HttpCookie>();
                _cookies[key] = hostCookies;
            }

            hostCookies[cookie.Name] = cookie;
        }
    }

    public string? GetCookieHeader(Uri uri)
    {
        lock (_lock)
        {
            var key = GetKey(uri);
            if (!_cookies.TryGetValue(key, out var hostCookies))
                return null;

            // Filter by Path, Domain, Secure
            var applicableCookies = hostCookies.Values
                .Where(c => c.IsApplicable(uri))
                .Where(c => !c.IsExpired())
                .OrderByDescending(c => c.Path.Length);  // Most specific first

            if (!applicableCookies.Any())
                return null;

            return string.Join("; ", applicableCookies.Select(c => $"{c.Name}={c.Value}"));
        }
    }

    private HttpCookie ParseSetCookie(string header)
    {
        // RFC 6265 Set-Cookie parsing
        var parts = header.Split(';');
        var nvp = parts[0].Split('=', 2);

        var cookie = new HttpCookie
        {
            Name = nvp[0].Trim(),
            Value = nvp.Length > 1 ? nvp[1].Trim() : string.Empty
        };

        for (int i = 1; i < parts.Length; i++)
        {
            var attr = parts[i].Trim().Split('=', 2);
            var attrName = attr[0].ToLowerInvariant();

            switch (attrName)
            {
                case "domain":
                    cookie.Domain = attr.Length > 1 ? attr[1].Trim() : string.Empty;
                    break;
                case "path":
                    cookie.Path = attr.Length > 1 ? attr[1].Trim() : "/";
                    break;
                case "expires":
                    if (attr.Length > 1 && DateTime.TryParse(attr[1], out var expiry))
                        cookie.Expires = expiry;
                    break;
                case "secure":
                    cookie.Secure = true;
                    break;
                case "httponly":
                    cookie.HttpOnly = true;
                    break;
            }
        }

        return cookie;
    }

    private string GetKey(Uri uri) => $"{uri.Host}:{uri.Port}";
}

public class HttpCookie
{
    public string Name { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string Domain { get; set; } = string.Empty;
    public string Path { get; set; } = "/";
    public DateTime? Expires { get; set; }
    public bool Secure { get; set; }
    public bool HttpOnly { get; set; }

    public bool IsExpired() => Expires.HasValue && Expires.Value < DateTime.UtcNow;

    public bool IsApplicable(Uri uri)
    {
        // RFC 6265 §5.4: Domain Matching
        if (!uri.Host.EndsWith(Domain, StringComparison.OrdinalIgnoreCase))
            return false;

        // Path matching
        if (!uri.AbsolutePath.StartsWith(Path, StringComparison.OrdinalIgnoreCase))
            return false;

        // Secure flag
        if (Secure && uri.Scheme != "https")
            return false;

        return true;
    }
}
```

### Phase 54: Handler Integration

```csharp
public class CookieJarHandler : DelegatingHandler
{
    private readonly CookieJar _jar = new();

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        // Add cookies to request
        var cookieHeader = _jar.GetCookieHeader(request.RequestUri!);
        if (!string.IsNullOrEmpty(cookieHeader))
        {
            request.Headers.Add("Cookie", cookieHeader);
        }

        var response = await base.SendAsync(request, cancellationToken);

        // Store Set-Cookie headers
        if (response.Headers.TryGetValues("Set-Cookie", out var setCookies))
        {
            foreach (var setCookie in setCookies)
            {
                _jar.Add(request.RequestUri!, setCookie);
            }
        }

        return response;
    }
}
```

### Tests: 25+
- Parse Set-Cookie
- Domain matching
- Path matching
- Expiration
- Secure flag
- Cookie jar persistence

---

## Phase 55-56: Connection Pooling

**Location**: `src/TurboHttp/Features/ConnectionPoolHandler.cs` (NEW)
**Effort**: 2 weeks

### Phase 55: Pool Implementation

```csharp
public class ConnectionPool
{
    private readonly Dictionary<string, Queue<(DateTime AddedTime, Stream Connection)>> _pools = new();
    private readonly int _maxPerHost = 10;
    private readonly TimeSpan _timeout = TimeSpan.FromSeconds(30);
    private readonly object _lock = new();

    public async Task<Stream> AcquireAsync(Uri uri)
    {
        var key = GetKey(uri);

        lock (_lock)
        {
            if (_pools.TryGetValue(key, out var queue))
            {
                while (queue.Count > 0)
                {
                    var (added, conn) = queue.Dequeue();

                    // Check if connection is stale
                    if (DateTime.UtcNow - added > _timeout)
                    {
                        conn?.Dispose();
                        continue;
                    }

                    return conn;
                }
            }
        }

        // Create new connection
        return await CreateConnectionAsync(uri);
    }

    public void Release(Uri uri, Stream connection)
    {
        var key = GetKey(uri);

        lock (_lock)
        {
            if (!_pools.TryGetValue(key, out var queue))
            {
                queue = new Queue<(DateTime, Stream)>();
                _pools[key] = queue;
            }

            if (queue.Count < _maxPerHost)
            {
                queue.Enqueue((DateTime.UtcNow, connection));
            }
            else
            {
                connection?.Dispose();
            }
        }
    }

    private async Task<Stream> CreateConnectionAsync(Uri uri)
    {
        var tcpClient = new TcpClient();
        await tcpClient.ConnectAsync(uri.Host, uri.Port);
        return tcpClient.GetStream();
    }

    private string GetKey(Uri uri) => $"{uri.Host}:{uri.Port}";
}
```

### Phase 56: Handler Integration

```csharp
public class PoolingHandler : DelegatingHandler
{
    private readonly ConnectionPool _pool = new();

    // Reuse kept alive connections across requests
    // Implementation details...
}
```

### Tests: 30+
- Connection reuse
- No connection leaks
- Timeout eviction
- Max per-host limits

---

## Phase 57: Request/Response Logging

**Location**: `src/TurboHttp/Features/LoggingHandler.cs` (NEW)
**Tests**: 20+

```csharp
public class LoggingHandler : DelegatingHandler
{
    private readonly ILogger<LoggingHandler> _logger;

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();

        _logger.LogInformation(
            "HTTP {Method} {Uri}",
            request.Method, request.RequestUri);

        var response = await base.SendAsync(request, cancellationToken);

        sw.Stop();

        _logger.LogInformation(
            "HTTP {Method} {Uri} → {Status} ({ElapsedMs}ms, {ContentLength} bytes)",
            request.Method, request.RequestUri, (int)response.StatusCode,
            sw.ElapsedMilliseconds, response.Content.Headers.ContentLength ?? 0);

        return response;
    }
}
```

---

## Phase 58: Timeout & Retry Policies

**Location**: `src/TurboHttp/Features/TimeoutAndRetryHandler.cs` (NEW)
**Tests**: 25+

```csharp
public class TimeoutAndRetryHandler : DelegatingHandler
{
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(30);
    public int MaxRetries { get; set; } = 3;
    public TimeSpan InitialBackoff { get; set; } = TimeSpan.FromMilliseconds(100);

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var backoff = InitialBackoff;

        for (int attempt = 0; attempt < MaxRetries; attempt++)
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(RequestTimeout);

            try
            {
                return await base.SendAsync(request, cts.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // Timeout occurred, retry
                if (attempt < MaxRetries - 1)
                {
                    await Task.Delay(backoff, cancellationToken);
                    backoff = TimeSpan.FromMilliseconds(backoff.TotalMilliseconds * 2);
                }
                else
                {
                    throw;
                }
            }
        }

        throw new InvalidOperationException("Should not reach here");
    }
}
```

---

## Phase 59: Integration Test Suite

**Location**: `src/TurboHttp.Tests/FeatureIntegrationTests.cs` (NEW)
**Tests**: 100+

Test combinations:
- Decompression + redirects
- Redirects + cookies
- Cookies + pooling
- Decompression + cookies
- Concurrent pooling
- Timeout + retry
- Logging + all others

---

## Phase 60: Validation Gate ✅

**Objective**: Final validation before v1.0 release

### Tasks
- [ ] Full regression test suite: `dotnet test src/TurboHttp.sln`
- [ ] Benchmark suite vs. baseline
- [ ] Memory profile (no leaks)
- [ ] Concurrent stress test (1000+ requests)
- [ ] Thread-safety verified
- [ ] Performance acceptable
- [ ] All features documented
- [ ] Commit: "Complete All Tiers: Client-Side Production Ready (38 Phases)"

### Validation Checklist
- [ ] 0 test failures
- [ ] <5% performance regression
- [ ] Memory stable under load
- [ ] Thread-safe
- [ ] All 60 phases complete

**Definition of Done**: **v1.0 PRODUCTION READY ✅**

---

# Summary

**Total: 60 Atomic Phases**
- **Tier 1**: Phases 23-38 (RFC Compliance, 4 weeks)
- **Tier 2**: Phases 39-48 (Performance, 3 weeks)
- **Tier 3**: Phases 49-60 (Features, 3 weeks)
- **Total Effort**: 10 weeks
- **Result**: Production-ready v1.0

Each phase:
- ✅ Independent (can merge separately)
- ✅ Tested (20-60 tests each)
- ✅ Documented (RFC, tasks, validation)
- ✅ Validated (checklist before merge)
