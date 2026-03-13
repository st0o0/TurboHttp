using System.Collections.Concurrent;
using System.Net;
using System.Text;
using Akka;
using Akka.Actor;
using Akka.Streams;
using Akka.Streams.Dsl;
using TurboHttp.IntegrationTests.Shared;
using TurboHttp.IO;
using TurboHttp.IO.Stages;
using TurboHttp.Streams;
using TurboHttp.Streams.Stages;

namespace TurboHttp.IntegrationTests.Http20;

/// <summary>
/// Integration tests for Http20Engine redirect handling (RFC 9110 §15.4).
/// Verifies redirect status codes, method rewriting semantics, redirect chains,
/// loop detection, and same-connection reuse across redirect requests over HTTP/2.
/// </summary>
public sealed class Http20RedirectTests : TestKit, IClassFixture<KestrelH2Fixture>
{
    private readonly KestrelH2Fixture _fixture;
    private readonly IMaterializer _materializer;
    private readonly IActorRef _clientManager;

    public Http20RedirectTests(KestrelH2Fixture fixture)
    {
        _fixture = fixture;
        _materializer = Sys.Materializer();
        _clientManager = Sys.ActorOf(Props.Create<ClientManager>());
    }

    /// <summary>
    /// Builds an HTTP/2 pipeline flow for redirect testing.
    /// </summary>
    private Flow<HttpRequestMessage, HttpResponseMessage, NotUsed> BuildFlow()
    {
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
    /// Sends multiple HTTP/2 requests through a single multiplexed pipeline.
    /// </summary>
    private async Task<List<HttpResponseMessage>> SendManyAsync(
        IReadOnlyList<HttpRequestMessage> requests,
        TimeSpan? timeout = null)
    {
        var effectiveTimeout = timeout ?? TimeSpan.FromSeconds(15);
        var flow = BuildFlow();

        var responses = new ConcurrentBag<HttpResponseMessage>();
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var (queue, _) = Source.Queue<HttpRequestMessage>(requests.Count + 1, OverflowStrategy.Backpressure)
            .Via(flow)
            .ToMaterialized(
                Sink.ForEach<HttpResponseMessage>(res =>
                {
                    responses.Add(res);
                    if (responses.Count >= requests.Count)
                    {
                        tcs.TrySetResult();
                    }
                }),
                Keep.Both)
            .Run(_materializer);

        foreach (var request in requests)
        {
            await queue.OfferAsync(request);
        }

        await tcs.Task.WaitAsync(effectiveTimeout);
        return responses.ToList();
    }

    [Theory(DisplayName = "20E-INT-029: GET redirect — 301/302/307/308 returns correct status and Location header")]
    [InlineData(301)]
    [InlineData(302)]
    [InlineData(307)]
    [InlineData(308)]
    public async Task GetRedirect_ReturnsCorrectStatusAndLocation(int statusCode)
    {
        // RFC 9110 §15.4: Redirect responses include a Location header indicating
        // the target URI. The Http20Engine must correctly decode the redirect status
        // and Location header from HTTP/2 HEADERS frames.
        var request =
            new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{_fixture.Port}/redirect/{statusCode}/hello")
            {
                Version = HttpVersion.Version20
            };

        var response = await SendAsync(request);

        Assert.Equal((HttpStatusCode)statusCode, response.StatusCode);
        Assert.Equal("/hello", response.Headers.Location?.OriginalString);
    }

    [Fact(DisplayName = "20E-INT-030: POST /redirect/303 — server returns 303 See Other for POST→GET rewrite")]
    public async Task PostRedirect303_Returns303SeeOther()
    {
        // RFC 9110 §15.4.4: 303 See Other indicates the server is redirecting the
        // user agent to a different resource using GET. The original POST method
        // should be changed to GET when following the redirect. This test verifies
        // the 303 response and Location header are correctly decoded over HTTP/2.
        var content = new StringContent("post-body", Encoding.UTF8, "text/plain");
        var request = new HttpRequestMessage(HttpMethod.Post, $"http://127.0.0.1:{_fixture.Port}/redirect/303")
        {
            Version = HttpVersion.Version20,
            Content = content
        };

        var response = await SendAsync(request);

        Assert.Equal(HttpStatusCode.SeeOther, response.StatusCode);
        Assert.Equal("/hello", response.Headers.Location?.OriginalString);
    }

    [Fact(DisplayName = "20E-INT-031: POST /redirect/307 — server returns 307 preserving POST method")]
    public async Task PostRedirect307_Returns307PreservingMethod()
    {
        // RFC 9110 §15.4.8: 307 Temporary Redirect requires the user agent to
        // preserve the request method and body when following the redirect.
        // This test verifies the 307 response and Location header pointing to
        // /echo (a POST endpoint) are correctly decoded over HTTP/2.
        var content = new StringContent("preserved-body", Encoding.UTF8, "text/plain");
        var request = new HttpRequestMessage(HttpMethod.Post, $"http://127.0.0.1:{_fixture.Port}/redirect/307")
        {
            Version = HttpVersion.Version20,
            Content = content
        };

        var response = await SendAsync(request);

        Assert.Equal(HttpStatusCode.TemporaryRedirect, response.StatusCode);
        Assert.Equal("/echo", response.Headers.Location?.OriginalString);
    }

    [Fact(DisplayName = "20E-INT-032: GET /redirect/chain/5 — redirect chain returns 302 with next hop Location")]
    public async Task RedirectChain_Returns302WithNextHop()
    {
        // RFC 9110 §15.4: Redirect chains involve multiple sequential redirects.
        // GET /redirect/chain/5 returns 302 with Location: /redirect/chain/4,
        // which would continue until /redirect/chain/1 → /hello.
        // This test verifies the first hop of a 5-hop chain is correctly decoded.
        var request = new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{_fixture.Port}/redirect/chain/5")
        {
            Version = HttpVersion.Version20
        };

        var response = await SendAsync(request);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/redirect/chain/4", response.Headers.Location?.OriginalString);
    }

    [Fact(DisplayName = "20E-INT-033: GET /redirect/loop — infinite redirect loop returns 302 back to self")]
    public async Task RedirectLoop_Returns302BackToSelf()
    {
        // RFC 9110 §15.4: A client should detect redirect loops to prevent
        // infinite cycling. GET /redirect/loop redirects back to itself.
        // This test verifies the redirect loop response is correctly decoded,
        // providing a baseline for future loop-detection logic in the pipeline.
        var request = new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{_fixture.Port}/redirect/loop")
        {
            Version = HttpVersion.Version20
        };

        var response = await SendAsync(request);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/redirect/loop", response.Headers.Location?.OriginalString);
    }

    [Fact(DisplayName = "20E-INT-034: Multiple redirect requests reuse same HTTP/2 connection")]
    public async Task MultipleRedirects_ReuseSameConnection()
    {
        // RFC 9113 §9.1: A client can send multiple requests on the same HTTP/2
        // connection. This test verifies that multiple redirect-producing requests
        // can be multiplexed on a single connection, proving connection reuse works
        // correctly even when responses are redirects rather than final responses.
        var requests = new List<HttpRequestMessage>
        {
            new(HttpMethod.Get, $"http://127.0.0.1:{_fixture.Port}/redirect/301/hello")
            {
                Version = HttpVersion.Version20
            },
            new(HttpMethod.Get, $"http://127.0.0.1:{_fixture.Port}/redirect/302/hello")
            {
                Version = HttpVersion.Version20
            },
            new(HttpMethod.Get, $"http://127.0.0.1:{_fixture.Port}/redirect/307/hello")
            {
                Version = HttpVersion.Version20
            },
            new(HttpMethod.Get, $"http://127.0.0.1:{_fixture.Port}/redirect/308/hello")
            {
                Version = HttpVersion.Version20
            }
        };

        var responses = await SendManyAsync(requests);

        Assert.Equal(4, responses.Count);
        Assert.All(responses, r =>
        {
            Assert.True(
                r.StatusCode is HttpStatusCode.MovedPermanently
                    or HttpStatusCode.Redirect
                    or HttpStatusCode.TemporaryRedirect
                    or HttpStatusCode.PermanentRedirect);
            Assert.NotNull(r.Headers.Location);
        });
    }
}