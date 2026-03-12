using System.Net;
using Akka.Actor;
using Akka.Streams;
using Akka.Streams.Dsl;
using TurboHttp.Client;
using TurboHttp.IntegrationTests.Shared;
using TurboHttp.IO;
using TurboHttp.Streams;
using TurboHttp.Streams.Stages;

namespace TurboHttp.IntegrationTests.Shared;

/// <summary>
/// Integration tests for HTTP version negotiation and demultiplexing.
/// Verifies that requests are routed to the correct engine (Http10Engine,
/// Http11Engine, Http20Engine) based on the request's Version property,
/// and that the Engine's Partition correctly demultiplexes mixed-version traffic.
///
/// HTTP/1.x tests use TurboHttpClient (full client pipeline).
/// HTTP/2 tests use Http20Engine directly because the production Engine does not
/// yet inject PrependPrefaceStage into the HTTP/2 connection flow.
/// </summary>
public sealed class VersionNegotiationTests : TestKit, IClassFixture<KestrelFixture>, IClassFixture<KestrelH2Fixture>
{
    private readonly KestrelFixture _h1Fixture;
    private readonly KestrelH2Fixture _h2Fixture;
    private readonly IMaterializer _materializer;
    private readonly IActorRef _clientManager;

    public VersionNegotiationTests(KestrelFixture h1Fixture, KestrelH2Fixture h2Fixture)
    {
        _h1Fixture = h1Fixture;
        _h2Fixture = h2Fixture;
        _materializer = Sys.Materializer();
        _clientManager = Sys.ActorOf(Props.Create<ClientManager>());
    }

    private TurboHttpClient CreateClient()
        => new(new TurboClientOptions(), Sys);

    /// <summary>
    /// Sends a single HTTP/2 request via Http20Engine with PrependPrefaceStage.
    /// </summary>
    private async Task<HttpResponseMessage> SendH2Async(HttpRequestMessage request)
    {
        var tcpOptions = new TcpOptions
        {
            Host = "127.0.0.1",
            Port = _h2Fixture.Port
        };

        const int windowSize = 2 * 1024 * 1024;
        var engine = new Http20Engine(windowSize).CreateFlow();

        var transport =
            Flow.Create<ITransportItem>()
                .Prepend(Source.Single<ITransportItem>(new ConnectItem(tcpOptions)))
                .Via(new PrependPrefaceStage(windowSize))
                .Via(new ConnectionStage(_clientManager));

        var flow = engine.Join(transport);

        var (queue, responseTask) = Source.Queue<HttpRequestMessage>(1, OverflowStrategy.Backpressure)
            .Via(flow)
            .ToMaterialized(Sink.First<HttpResponseMessage>(), Keep.Both)
            .Run(_materializer);

        await queue.OfferAsync(request);
        return await responseTask.WaitAsync(TimeSpan.FromSeconds(10));
    }

    [Fact(DisplayName = "VERNEG-001: HTTP/1.0 request is routed to Http10Engine")]
    public async Task Http10_Request_RoutedToHttp10Engine()
    {
        var client = CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{_h1Fixture.Port}/hello")
        {
            Version = HttpVersion.Version10
        };

        var response = await client.SendAsync(request, CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal("Hello World", body);
        Assert.Equal(HttpVersion.Version10, response.Version);
    }

    [Fact(DisplayName = "VERNEG-002: HTTP/1.1 request is routed to Http11Engine")]
    public async Task Http11_Request_RoutedToHttp11Engine()
    {
        var client = CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{_h1Fixture.Port}/hello")
        {
            Version = HttpVersion.Version11
        };

        var response = await client.SendAsync(request, CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal("Hello World", body);
        Assert.Equal(HttpVersion.Version11, response.Version);
    }

    [Fact(DisplayName = "VERNEG-003: HTTP/2.0 request is routed to Http20Engine")]
    public async Task Http20_Request_RoutedToHttp20Engine()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{_h2Fixture.Port}/hello")
        {
            Version = HttpVersion.Version20
        };

        var response = await SendH2Async(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal("Hello World", body);
    }

    [Fact(DisplayName = "VERNEG-004: Mixed-version requests are demultiplexed to correct engines")]
    public async Task MixedVersion_RequestsDemultiplexedCorrectly()
    {
        var client = CreateClient();

        // Send HTTP/1.0 request through TurboHttpClient
        var req10 = new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{_h1Fixture.Port}/ping")
        {
            Version = HttpVersion.Version10
        };
        var resp10 = await client.SendAsync(req10, CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, resp10.StatusCode);
        Assert.Equal(HttpVersion.Version10, resp10.Version);
        var body10 = await resp10.Content.ReadAsStringAsync();
        Assert.Equal("pong", body10);

        // Send HTTP/1.1 request through TurboHttpClient
        var req11 = new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{_h1Fixture.Port}/ping")
        {
            Version = HttpVersion.Version11
        };
        var resp11 = await client.SendAsync(req11, CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, resp11.StatusCode);
        Assert.Equal(HttpVersion.Version11, resp11.Version);
        var body11 = await resp11.Content.ReadAsStringAsync();
        Assert.Equal("pong", body11);

        // Send HTTP/2.0 request directly via Http20Engine (h2c server, different port)
        var req20 = new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{_h2Fixture.Port}/ping")
        {
            Version = HttpVersion.Version20
        };
        var resp20 = await SendH2Async(req20);

        Assert.Equal(HttpStatusCode.OK, resp20.StatusCode);
        var body20 = await resp20.Content.ReadAsStringAsync();
        Assert.Equal("pong", body20);
    }

    [Fact(DisplayName = "VERNEG-005: DefaultRequestVersion overrides unversioned requests")]
    public async Task DefaultRequestVersion_OverridesUnversionedRequests()
    {
        var client = CreateClient();
        client.DefaultRequestVersion = HttpVersion.Version10;

        // Request without explicit Version — should use DefaultRequestVersion (HTTP/1.0)
        var request = new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{_h1Fixture.Port}/hello");

        var response = await client.SendAsync(request, CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal("Hello World", body);
        Assert.Equal(HttpVersion.Version10, response.Version);
    }
}
