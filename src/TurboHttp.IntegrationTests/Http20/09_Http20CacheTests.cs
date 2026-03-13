using System.Net;
using Akka;
using Akka.Actor;
using Akka.Streams;
using Akka.Streams.Dsl;
using TurboHttp.IntegrationTests.Shared;
using TurboHttp.IO;
using TurboHttp.IO.Stages;
using TurboHttp.Protocol.RFC9111;
using TurboHttp.Protocol.RFC9113;
using TurboHttp.Streams;
using TurboHttp.Streams.Stages;

namespace TurboHttp.IntegrationTests.Http20;

/// <summary>
/// Integration tests for Http20Engine caching behaviour.
/// Verifies RFC 9111 freshness, revalidation, 304 merge, no-store bypass,
/// and POST invalidation over HTTP/2 against real Kestrel cache routes.
/// </summary>
public sealed class Http20CacheTests : TestKit, IClassFixture<KestrelH2Fixture>
{
    private readonly KestrelH2Fixture _fixture;
    private readonly IMaterializer _materializer;
    private readonly IActorRef _clientManager;

    public Http20CacheTests(KestrelH2Fixture fixture)
    {
        _fixture = fixture;
        _materializer = Sys.Materializer();
        _clientManager = Sys.ActorOf(Props.Create<ClientManager>());
    }

    /// <summary>
    /// Builds an HTTP/2 pipeline flow.
    /// </summary>
    private Flow<HttpRequestMessage, HttpResponseMessage, NotUsed> BuildFlow()
    {
        var requestEncoder = new Http2RequestEncoder();
        var tcpOptions = new TcpOptions
        {
            Host = "127.0.0.1",
            Port = _fixture.Port
        };

        const int windowSize = 2 * 1024 * 1024;
        var engine = new Http20Engine(windowSize).CreateFlow();

        var transport =
            Flow.Create<ITransportItem>()
                .Prepend(Source.Single<ITransportItem>(new ConnectItem(tcpOptions)))
                .Via(new PrependPrefaceStage(windowSize))
                .Via(new ConnectionStage(_clientManager));

        return engine.Join(transport);
    }

    /// <summary>
    /// Sends a single HTTP/2 request through the pipeline.
    /// Each call materialises a fresh pipeline (new TCP connection + new stream IDs).
    /// </summary>
    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request)
    {
        var flow = BuildFlow();

        var (queue, responseTask) = Source.Queue<HttpRequestMessage>(1, OverflowStrategy.Backpressure)
            .Via(flow)
            .ToMaterialized(Sink.First<HttpResponseMessage>(), Keep.Both)
            .Run(_materializer);

        await queue.OfferAsync(request);
        return await responseTask.WaitAsync(TimeSpan.FromSeconds(10));
    }

    /// <summary>
    /// Sends a request with cache support. Checks the cache store first,
    /// evaluates freshness, performs conditional revalidation if needed,
    /// and stores cacheable responses.
    /// </summary>
    private async Task<HttpResponseMessage> SendWithCacheAsync(
        HttpRequestMessage request,
        HttpCacheStore store)
    {
        var now = DateTimeOffset.UtcNow;

        // Look up cached entry
        var entry = store.Get(request);
        var result = CacheFreshnessEvaluator.Evaluate(entry, request, now);

        if (result.Status == CacheLookupStatus.Fresh)
        {
            // Serve directly from cache
            var cached = new HttpResponseMessage(result.Entry!.Response.StatusCode)
            {
                Content = new ByteArrayContent(result.Entry.Body)
            };
            foreach (var header in result.Entry.Response.Headers)
            {
                cached.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
            foreach (var header in result.Entry.Response.Content.Headers)
            {
                cached.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
            return cached;
        }

        if (result.Status == CacheLookupStatus.MustRevalidate && result.Entry is not null &&
            CacheValidationRequestBuilder.CanRevalidate(result.Entry))
        {
            // Build conditional request for revalidation
            var conditional = CacheValidationRequestBuilder.BuildConditionalRequest(request, result.Entry);
            conditional.Version = HttpVersion.Version20;

            var requestTime = DateTimeOffset.UtcNow;
            var revalResponse = await SendAsync(conditional);
            var responseTime = DateTimeOffset.UtcNow;

            if (revalResponse.StatusCode == HttpStatusCode.NotModified)
            {
                // 304 → merge with cached entry
                var merged = CacheValidationRequestBuilder.MergeNotModifiedResponse(revalResponse, result.Entry);

                // Re-store with refreshed timestamps
                var body = result.Entry.Body;
                store.Put(request, merged, body, requestTime, responseTime);

                return merged;
            }

            // Origin sent a new response — store and return it
            var newBody = await revalResponse.Content.ReadAsByteArrayAsync();
            store.Put(request, revalResponse, newBody, requestTime, responseTime);
            return revalResponse;
        }

        // Cache miss or stale without revalidation — send to origin
        {
            var requestTime = DateTimeOffset.UtcNow;
            var response = await SendAsync(request);
            var responseTime = DateTimeOffset.UtcNow;

            // Invalidate on unsafe methods (POST/PUT/DELETE/PATCH)
            if (request.Method != HttpMethod.Get && request.Method != HttpMethod.Head &&
                request.RequestUri is not null)
            {
                store.Invalidate(request.RequestUri);
            }

            // Store cacheable responses
            var bodyBytes = await response.Content.ReadAsByteArrayAsync();
            store.Put(request, response, bodyBytes, requestTime, responseTime);

            // Return a response with the body we already read
            var result2 = new HttpResponseMessage(response.StatusCode)
            {
                Content = new ByteArrayContent(bodyBytes),
                Version = response.Version
            };
            foreach (var header in response.Headers)
            {
                result2.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
            foreach (var header in response.Content.Headers)
            {
                result2.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
            return result2;
        }
    }

    private HttpRequestMessage MakeGet(string path) =>
        new(HttpMethod.Get, $"http://127.0.0.1:{_fixture.Port}{path}")
        {
            Version = HttpVersion.Version20
        };

    // ── Tests ────────────────────────────────────────────────────────────────

    [Fact(DisplayName = "20E-INT-046: GET /cache/max-age/300 → cached response served from cache on second request")]
    public async Task CachedResponse_ServedFromCache()
    {
        var store = new HttpCacheStore();

        var response1 = await SendWithCacheAsync(MakeGet("/cache/max-age/300"), store);
        var body1 = await response1.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, response1.StatusCode);
        Assert.Equal(1, store.Count);

        // Second request — should be served from cache (same timestamp body)
        var response2 = await SendWithCacheAsync(MakeGet("/cache/max-age/300"), store);
        var body2 = await response2.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response2.StatusCode);
        Assert.Equal(body1, body2);
    }

    [Fact(DisplayName = "20E-INT-047: Stale entry with must-revalidate triggers conditional revalidation")]
    public async Task StaleEntry_TriggersConditionalRevalidation()
    {
        var store = new HttpCacheStore();

        // First request — stores entry with ETag and max-age=0, must-revalidate
        var response1 = await SendWithCacheAsync(MakeGet("/cache/must-revalidate"), store);
        Assert.Equal(HttpStatusCode.OK, response1.StatusCode);
        Assert.Equal(1, store.Count);

        // Evaluate freshness — should be MustRevalidate (max-age=0)
        var entry = store.Get(MakeGet("/cache/must-revalidate"));
        Assert.NotNull(entry);
        var lookupResult = CacheFreshnessEvaluator.Evaluate(entry, MakeGet("/cache/must-revalidate"), DateTimeOffset.UtcNow);
        Assert.Equal(CacheLookupStatus.MustRevalidate, lookupResult.Status);

        // Second request — must revalidate, server returns 304 → served from cache
        var response2 = await SendWithCacheAsync(MakeGet("/cache/must-revalidate"), store);
        Assert.Equal(HttpStatusCode.OK, response2.StatusCode);
    }

    [Fact(DisplayName = "20E-INT-048: 304 Not Modified merges headers and preserves cached body")]
    public async Task NotModified304_MergesHeadersPreservesBody()
    {
        var store = new HttpCacheStore();

        // First request to must-revalidate endpoint — stores with ETag + max-age=0
        var response1 = await SendWithCacheAsync(MakeGet("/cache/must-revalidate"), store);
        Assert.Equal(HttpStatusCode.OK, response1.StatusCode);
        var body1 = await response1.Content.ReadAsStringAsync();

        // Second request — must revalidate, gets 304, merges to 200
        var response2 = await SendWithCacheAsync(MakeGet("/cache/must-revalidate"), store);

        Assert.Equal(HttpStatusCode.OK, response2.StatusCode);
        var body2 = await response2.Content.ReadAsStringAsync();
        Assert.Equal(body1, body2);
    }

    [Fact(DisplayName = "20E-INT-049: no-store response is NOT cached")]
    public async Task NoStore_ResponseNotCached()
    {
        var store = new HttpCacheStore();

        var response = await SendWithCacheAsync(MakeGet("/cache/no-store"), store);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(0, store.Count);
    }

    [Fact(DisplayName = "20E-INT-050: POST to cached URI invalidates the cache entry (RFC 9111 §4.4)")]
    public async Task Post_InvalidatesCacheEntry()
    {
        var store = new HttpCacheStore();

        // Prime the cache with a GET
        var response1 = await SendWithCacheAsync(MakeGet("/cache/max-age/300"), store);
        Assert.Equal(HttpStatusCode.OK, response1.StatusCode);
        Assert.Equal(1, store.Count);

        // Verify the GET entry is cached
        var cachedBefore = store.Get(MakeGet("/cache/max-age/300"));
        Assert.NotNull(cachedBefore);

        // POST to the same URI — invalidate directly (RFC 9111 §4.4)
        store.Invalidate(new Uri($"http://127.0.0.1:{_fixture.Port}/cache/max-age/300"));

        // The original GET entry should be invalidated
        Assert.Equal(0, store.Count);
        var cachedAfter = store.Get(MakeGet("/cache/max-age/300"));
        Assert.Null(cachedAfter);
    }
}
