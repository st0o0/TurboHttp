using System.Net;
using Akka.Actor;
using Akka.Streams;
using Akka.Streams.Dsl;
using TurboHttp.IntegrationTests.Shared;
using TurboHttp.IO;
using TurboHttp.IO.Stages;
using TurboHttp.Streams;

namespace TurboHttp.IntegrationTests.Http11;

/// <summary>
/// Integration tests for Http11Engine content encoding (decompression).
/// Verifies that the Http11Engine transparently decompresses gzip/deflate/brotli
/// responses (RFC 9110 §8.4), passes through identity encoding, removes the
/// Content-Encoding header after decompression, sends Accept-Encoding, and
/// handles large compressed bodies.
/// </summary>
public sealed class Http11ContentEncodingTests : TestKit, IClassFixture<KestrelFixture>
{
    private readonly KestrelFixture _fixture;
    private readonly IMaterializer _materializer;
    private readonly IActorRef _clientManager;

    public Http11ContentEncodingTests(KestrelFixture fixture)
    {
        _fixture = fixture;
        _materializer = Sys.Materializer();
        _clientManager = Sys.ActorOf(Props.Create<ClientManager>());
    }

    /// <summary>
    /// Sends a single HTTP/1.1 request through the Http11Engine pipeline.
    /// </summary>
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
                .Via(new ConnectionStage(_clientManager));

        var flow = engine.CreateFlow().Join(transport);

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

    [Fact(DisplayName = "11E-CE-001: GET /compress/gzip/1 decompresses gzip response")]
    public async Task Gzip_Decompressed()
    {
        var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"http://127.0.0.1:{_fixture.Port}/compress/gzip/1")
        {
            Version = HttpVersion.Version11
        };

        var response = await SendAsync(request);
        var body = await response.Content.ReadAsByteArrayAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(GenerateExpectedPayload(1), body);
    }

    [Fact(DisplayName = "11E-CE-002: GET /compress/deflate/1 decompresses deflate response")]
    public async Task Deflate_Decompressed()
    {
        var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"http://127.0.0.1:{_fixture.Port}/compress/deflate/1")
        {
            Version = HttpVersion.Version11
        };

        var response = await SendAsync(request);
        var body = await response.Content.ReadAsByteArrayAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(GenerateExpectedPayload(1), body);
    }

    [Fact(DisplayName = "11E-CE-003: GET /compress/br/1 decompresses brotli response")]
    public async Task Brotli_Decompressed()
    {
        var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"http://127.0.0.1:{_fixture.Port}/compress/br/1")
        {
            Version = HttpVersion.Version11
        };

        var response = await SendAsync(request);
        var body = await response.Content.ReadAsByteArrayAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(GenerateExpectedPayload(1), body);
    }

    [Fact(DisplayName = "11E-CE-004: GET /compress/identity/1 passes through uncompressed")]
    public async Task Identity_Passthrough()
    {
        var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"http://127.0.0.1:{_fixture.Port}/compress/identity/1")
        {
            Version = HttpVersion.Version11
        };

        var response = await SendAsync(request);
        var body = await response.Content.ReadAsByteArrayAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(GenerateExpectedPayload(1), body);
        Assert.Empty(response.Content.Headers.ContentEncoding);
    }

    [Fact(DisplayName = "11E-CE-005: Content-Encoding header removed after gzip decompression")]
    public async Task ContentEncoding_Removed_After_Decompression()
    {
        var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"http://127.0.0.1:{_fixture.Port}/compress/gzip/1")
        {
            Version = HttpVersion.Version11
        };

        var response = await SendAsync(request);

        Assert.Empty(response.Content.Headers.ContentEncoding);
        var body = await response.Content.ReadAsByteArrayAsync();
        Assert.Equal(GenerateExpectedPayload(1), body);
    }

    [Fact(DisplayName = "11E-CE-006: Accept-Encoding sent triggers server content negotiation")]
    public async Task AcceptEncoding_Sent_Triggers_Negotiation()
    {
        var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"http://127.0.0.1:{_fixture.Port}/compress/negotiate")
        {
            Version = HttpVersion.Version11
        };
        request.Headers.Add("Accept-Encoding", "gzip");

        var response = await SendAsync(request);
        var body = await response.Content.ReadAsByteArrayAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        // Server responds with gzip when Accept-Encoding: gzip is sent;
        // decoder decompresses transparently
        Assert.Equal(GenerateExpectedPayload(1), body);
    }

    [Fact(DisplayName = "11E-CE-007: GET /compress/gzip/500 decompresses large 500KB gzip body")]
    public async Task Large_Gzip_500KB_Decompressed()
    {
        var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"http://127.0.0.1:{_fixture.Port}/compress/gzip/500")
        {
            Version = HttpVersion.Version11
        };

        var response = await SendAsync(request);
        var body = await response.Content.ReadAsByteArrayAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(500 * 1024, body.Length);
        Assert.Equal(GenerateExpectedPayload(500), body);
    }
}
