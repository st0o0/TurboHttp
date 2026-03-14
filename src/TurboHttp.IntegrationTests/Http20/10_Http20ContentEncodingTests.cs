using System.Net;
using Akka;
using Akka.Actor;
using Akka.Streams;
using Akka.Streams.Dsl;
using TurboHttp.IntegrationTests.Shared;
using TurboHttp.IO;
using TurboHttp.IO.Stages;
using TurboHttp.Protocol.RFC9113;
using TurboHttp.Streams;
using TurboHttp.Streams.Stages;

namespace TurboHttp.IntegrationTests.Http20;

/// <summary>
/// Integration tests for Http20Engine content encoding (decompression).
/// Verifies that the HTTP/2 pipeline transparently decompresses gzip, deflate,
/// and brotli responses (RFC 9110 §8.4) and handles large compressed bodies.
/// </summary>
public sealed class Http20ContentEncodingTests : TestKit, IClassFixture<KestrelH2Fixture>
{
    private readonly KestrelH2Fixture _fixture;
    private readonly IMaterializer _materializer;
    private readonly IActorRef _poolRouter;

    public Http20ContentEncodingTests(KestrelH2Fixture fixture)
    {
        _fixture = fixture;
        _materializer = Sys.Materializer();
        _poolRouter = Sys.ActorOf(Props.Create(() => new PoolRouterActor()));
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
                .Via(new ConnectionStage(_poolRouter));

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
    /// Generates the same deterministic payload the server produces.
    /// </summary>
    private static byte[] GenerateExpectedPayload(int kb)
    {
        var size = kb * 1024;
        var data = new byte[size];
        for (var i = 0; i < size; i++)
        {
            data[i] = (byte)('A' + (i % 26));
        }

        return data;
    }

    private HttpRequestMessage MakeGet(string path) =>
        new(HttpMethod.Get, $"http://127.0.0.1:{_fixture.Port}{path}")
        {
            Version = HttpVersion.Version20
        };

    // ── Tests ────────────────────────────────────────────────────────────────

    [Fact(DisplayName = "20E-INT-051: GET /compress/gzip/1 decompresses gzip response over HTTP/2")]
    public async Task Gzip_Decompressed()
    {
        var response = await SendAsync(MakeGet("/compress/gzip/1"));
        var body = await response.Content.ReadAsByteArrayAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(GenerateExpectedPayload(1), body);
    }

    [Fact(DisplayName = "20E-INT-052: GET /compress/deflate/1 decompresses deflate response over HTTP/2")]
    public async Task Deflate_Decompressed()
    {
        var response = await SendAsync(MakeGet("/compress/deflate/1"));
        var body = await response.Content.ReadAsByteArrayAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(GenerateExpectedPayload(1), body);
    }

    [Fact(DisplayName = "20E-INT-053: GET /compress/br/1 decompresses brotli response over HTTP/2")]
    public async Task Brotli_Decompressed()
    {
        var response = await SendAsync(MakeGet("/compress/br/1"));
        var body = await response.Content.ReadAsByteArrayAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(GenerateExpectedPayload(1), body);
    }

    [Fact(DisplayName = "20E-INT-054: GET /compress/gzip/500 decompresses large 500KB gzip body over HTTP/2")]
    public async Task Large_Gzip_500KB_Decompressed()
    {
        var response = await SendAsync(MakeGet("/compress/gzip/500"));
        var body = await response.Content.ReadAsByteArrayAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(500 * 1024, body.Length);
        Assert.Equal(GenerateExpectedPayload(500), body);
    }
}
