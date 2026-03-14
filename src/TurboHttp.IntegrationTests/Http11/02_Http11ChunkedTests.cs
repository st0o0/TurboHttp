using System.Net;
using System.Text;
using Akka.Actor;
using Akka.Streams;
using Akka.Streams.Dsl;
using TurboHttp.IntegrationTests.Shared;
using TurboHttp.IO;
using TurboHttp.IO.Stages;
using TurboHttp.Streams;

namespace TurboHttp.IntegrationTests.Http11;

/// <summary>
/// Integration tests for Http11Engine chunked transfer encoding (RFC 9112 §7.1).
/// These tests drive the actual Http11Engine Akka.Streams pipeline against a real
/// Kestrel server that returns chunked responses via StartAsync().
/// </summary>
public sealed class Http11ChunkedTests : TestKit, IClassFixture<KestrelFixture>
{
    private readonly KestrelFixture _fixture;
    private readonly IMaterializer _materializer;
    private readonly IActorRef _poolRouter;

    public Http11ChunkedTests(KestrelFixture fixture)
    {
        _fixture = fixture;
        _materializer = Sys.Materializer();
        _poolRouter = Sys.ActorOf(Props.Create(() => new PoolRouterActor()));
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request)
    {
        var engine = new Http11Engine();
        var tcpOptions = new TcpOptions
        {
            Host = "127.0.0.1",
            Port = _fixture.Port
        };

        var transport =
            Flow.Create<ITransportItem>()
                .Prepend(Source.Single<ITransportItem>(
                    new ConnectItem(tcpOptions)))
                .Via(new ConnectionStage(_poolRouter));

        var flow = engine.CreateFlow().Join(transport);

        var (queue, responseTask) = Source.Queue<HttpRequestMessage>(1, OverflowStrategy.Backpressure)
            .Via(flow)
            .ToMaterialized(Sink.First<HttpResponseMessage>(), Keep.Both)
            .Run(_materializer);

        await queue.OfferAsync(request);
        return await responseTask.WaitAsync(TimeSpan.FromSeconds(10));
    }

    [Fact(DisplayName = "11E-CHUNK-001: GET /chunked/1 returns decoded chunked body")]
    public async Task Get_Chunked_ReturnsDecodedBody()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{_fixture.Port}/chunked/1")
        {
            Version = HttpVersion.Version11
        };

        var response = await SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsByteArrayAsync();
        Assert.Equal(1024, body.Length);
        Assert.All(body, b => Assert.Equal((byte)'A', b));
    }

    [Fact(DisplayName = "11E-CHUNK-002: GET /chunked/exact/5/100 reassembles multiple chunks")]
    public async Task Get_ChunkedExact_ReassemblesMultipleChunks()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{_fixture.Port}/chunked/exact/5/100")
        {
            Version = HttpVersion.Version11
        };

        var response = await SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsByteArrayAsync();
        Assert.Equal(500, body.Length);
        Assert.All(body, b => Assert.Equal((byte)'B', b));
    }

    [Fact(DisplayName = "11E-CHUNK-003: POST /echo/chunked echoes request body as chunked response")]
    public async Task Post_EchoChunked_ReturnsEchoedBody()
    {
        var content = new StringContent("chunked-echo-test", Encoding.UTF8, "text/plain");
        var request = new HttpRequestMessage(HttpMethod.Post, $"http://127.0.0.1:{_fixture.Port}/echo/chunked")
        {
            Version = HttpVersion.Version11,
            Content = content
        };

        var response = await SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal("chunked-echo-test", body);
    }

    [Fact(DisplayName = "11E-CHUNK-004: GET /chunked/exact/1/0 handles zero-length chunk body")]
    public async Task Get_ChunkedExact_ZeroLengthChunk()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{_fixture.Port}/chunked/exact/1/0")
        {
            Version = HttpVersion.Version11
        };

        var response = await SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsByteArrayAsync();
        Assert.Empty(body);
    }

    [Fact(DisplayName = "11E-CHUNK-005: GET /chunked/trailer returns body with trailer header")]
    public async Task Get_ChunkedTrailer_ReturnsBodyWithTrailer()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{_fixture.Port}/chunked/trailer")
        {
            Version = HttpVersion.Version11
        };

        var response = await SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal("chunked-with-trailer", body);
    }

    [Fact(DisplayName = "11E-CHUNK-006: GET /chunked/100 returns 100KB chunked body")]
    public async Task Get_Chunked_LargeBody100KB()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{_fixture.Port}/chunked/100")
        {
            Version = HttpVersion.Version11
        };

        var response = await SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsByteArrayAsync();
        Assert.Equal(100 * 1024, body.Length);
        Assert.All(body, b => Assert.Equal((byte)'A', b));
    }

    [Fact(DisplayName = "11E-CHUNK-007: HEAD /chunked/1 returns no body for chunked endpoint")]
    public async Task Head_Chunked_ReturnsNoBody()
    {
        var request = new HttpRequestMessage(HttpMethod.Head, $"http://127.0.0.1:{_fixture.Port}/chunked/1")
        {
            Version = HttpVersion.Version11
        };

        var response = await SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsByteArrayAsync();
        Assert.Empty(body);
    }

    [Fact(DisplayName = "11E-CHUNK-008: GET /chunked/md5 returns chunked body with Content-MD5 header")]
    public async Task Get_ChunkedMd5_ReturnsBodyWithMd5Header()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{_fixture.Port}/chunked/md5")
        {
            Version = HttpVersion.Version11
        };

        var response = await SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal("checksum-body", body);

        Assert.True(response.Content.Headers.TryGetValues("Content-MD5", out var md5Values));
        var expectedMd5 = Convert.ToBase64String(
            System.Security.Cryptography.MD5.HashData("checksum-body"u8.ToArray()));
        Assert.Equal(expectedMd5, md5Values!.First());
    }
}
