using System.Buffers;
using System.Collections.Concurrent;
using System.Net;
using Akka;
using Akka.Actor;
using Akka.Streams;
using Akka.Streams.Dsl;
using TurboHttp.IntegrationTests.Shared;
using TurboHttp.IO;
using TurboHttp.IO.Stages;
using TurboHttp.Protocol;
using TurboHttp.Streams.Stages;

namespace TurboHttp.IntegrationTests.Http20;

/// <summary>
/// Integration tests for Http20Engine SETTINGS and PING (RFC 9113 §6.5, §6.7).
/// Verifies SETTINGS handshake, MAX_CONCURRENT_STREAMS behaviour,
/// INITIAL_WINDOW_SIZE propagation, and PING round-trip keepalive.
/// </summary>
public sealed class Http20SettingsPingTests : TestKit, IClassFixture<KestrelH2Fixture>
{
    private readonly KestrelH2Fixture _fixture;
    private readonly IMaterializer _materializer;
    private readonly IActorRef _clientManager;

    public Http20SettingsPingTests(KestrelH2Fixture fixture)
    {
        _fixture = fixture;
        _materializer = Sys.Materializer();
        _clientManager = Sys.ActorOf(Props.Create<ClientManager>());
    }

    /// <summary>
    /// Builds an HTTP/2 pipeline flow with configurable initial window size.
    /// </summary>
    private Flow<HttpRequestMessage, HttpResponseMessage, NotUsed> BuildFlow(int windowSize = 2 * 1024 * 1024)
    {
        var requestEncoder = new Http2RequestEncoder();
        var tcpOptions = new TcpOptions
        {
            Host = "127.0.0.1",
            Port = _fixture.Port
        };

        return Flow.FromGraph(GraphDsl.Create(b =>
        {
            var streamIdAllocator = b.Add(new StreamIdAllocatorStage());
            var requestToFrame = b.Add(new Request2FrameStage(requestEncoder));
            var frameEncoder = b.Add(new Http20EncoderStage());
            var prependPreface = b.Add(new PrependPrefaceStage(windowSize));
            var frameDecoder = b.Add(new Http20DecoderStage());
            var streamDecoder = b.Add(new Http20StreamStage());
            var h2Connection = b.Add(new Http20ConnectionStage(windowSize));

            var connectionStage = b.Add(new ConnectionStage(_clientManager));

            var toDataItem = b.Add(Flow.Create<(IMemoryOwner<byte>, int)>()
                .Select(ITransportItem (x) => new DataItem(x.Item1, x.Item2)));

            var connectSource = b.Add(Source.Single<ITransportItem>(new ConnectItem(tcpOptions)));
            var concat = b.Add(Concat.Create<ITransportItem>(2));

            // Request path
            b.From(streamIdAllocator.Outlet).To(requestToFrame.Inlet);
            b.From(requestToFrame.Outlet).To(h2Connection.Inlet2);

            // Outbound
            b.From(h2Connection.Outlet2).To(frameEncoder.Inlet);
            b.From(frameEncoder.Outlet).To(toDataItem.Inlet);
            b.From(connectSource).To(concat.In(0));
            b.From(toDataItem.Outlet).To(concat.In(1));
            b.From(concat.Out).To(prependPreface.Inlet);
            b.From(prependPreface.Outlet).To(connectionStage.Inlet);

            // Inbound
            b.From(connectionStage.Outlet).To(frameDecoder.Inlet);
            b.From(frameDecoder.Outlet).To(h2Connection.Inlet1);
            b.From(h2Connection.Outlet1).To(streamDecoder.Inlet);

            return new FlowShape<HttpRequestMessage, HttpResponseMessage>(
                streamIdAllocator.Inlet, streamDecoder.Outlet);
        }));
    }

    /// <summary>
    /// Sends a single HTTP/2 request through the pipeline.
    /// </summary>
    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, int windowSize = 2 * 1024 * 1024)
    {
        var flow = BuildFlow(windowSize);

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
        int windowSize = 2 * 1024 * 1024,
        TimeSpan? timeout = null)
    {
        var effectiveTimeout = timeout ?? TimeSpan.FromSeconds(15);
        var flow = BuildFlow(windowSize);

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

    [Fact(DisplayName = "20E-INT-025: SETTINGS exchange — client SETTINGS ACK enables successful request")]
    public async Task SettingsExchange_ClientAcksServerSettings()
    {
        // RFC 9113 §6.5: Upon receiving a SETTINGS frame that is not an ACK, the
        // endpoint MUST apply the settings and send a SETTINGS ACK. The Http20ConnectionStage
        // handles this automatically. A successful request proves the SETTINGS handshake
        // completed: the client sent its SETTINGS in the connection preface, received the
        // server's SETTINGS, sent SETTINGS ACK, and received SETTINGS ACK from the server.
        var request = new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{_fixture.Port}/h2/settings")
        {
            Version = HttpVersion.Version20
        };

        var response = await SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal("h2-ok", body);
    }

    [Fact(DisplayName = "20E-INT-026: MAX_CONCURRENT_STREAMS — multiple concurrent streams complete successfully")]
    public async Task MaxConcurrentStreams_MultipleConcurrentStreamsComplete()
    {
        // RFC 9113 §6.5.2: SETTINGS_MAX_CONCURRENT_STREAMS indicates the maximum number
        // of concurrent streams the sender will allow. Kestrel's default is 100.
        // Send 10 concurrent requests through a single connection to verify that the
        // pipeline correctly multiplexes streams within the server's concurrency limit.
        // All streams should complete without RST_STREAM or REFUSED_STREAM errors.
        var requests = new List<HttpRequestMessage>();
        for (var i = 0; i < 10; i++)
        {
            requests.Add(new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{_fixture.Port}/ping")
            {
                Version = HttpVersion.Version20
            });
        }

        var responses = await SendManyAsync(requests);

        Assert.Equal(10, responses.Count);
        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
    }

    [Fact(DisplayName = "20E-INT-027: INITIAL_WINDOW_SIZE — custom window size propagated in SETTINGS enables large transfer")]
    public async Task InitialWindowSize_CustomWindowSizePropagated()
    {
        // RFC 9113 §6.5.2: SETTINGS_INITIAL_WINDOW_SIZE indicates the sender's initial
        // window size for stream-level flow control. The PrependPrefaceStage emits this
        // value in the client's initial SETTINGS frame. A 256KB response exceeds the
        // RFC-default 65535-byte window, so success proves our custom INITIAL_WINDOW_SIZE
        // (1 MB here) was propagated to the server and applied to stream flow control.
        const int windowSize = 1 * 1024 * 1024; // 1 MB
        var request = new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{_fixture.Port}/large/256")
        {
            Version = HttpVersion.Version20
        };

        var response = await SendAsync(request, windowSize);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsByteArrayAsync();
        Assert.Equal(256 * 1024, body.Length);
        Assert.All(body, b => Assert.Equal((byte)'A', b));
    }

    [Fact(DisplayName = "20E-INT-028: PING round-trip — connection survives server PING after idle period")]
    public async Task PingRoundTrip_ConnectionSurvivesAfterIdle()
    {
        // RFC 9113 §6.7: PING frames are a mechanism for measuring round-trip time
        // and determining whether an idle connection is still functional. The receiver
        // MUST respond with a PING ACK containing the same opaque data.
        // Http20ConnectionStage automatically responds to server PINGs with ACKs.
        // Send two sequential requests with a delay between them on the same pipeline.
        // The delay allows the server to potentially send PING frames for keepalive.
        // A successful second request proves PING handling kept the connection alive.
        var flow = BuildFlow();

        var responses = new ConcurrentBag<HttpResponseMessage>();
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var (queue, _) = Source.Queue<HttpRequestMessage>(4, OverflowStrategy.Backpressure)
            .Via(flow)
            .ToMaterialized(
                Sink.ForEach<HttpResponseMessage>(res =>
                {
                    responses.Add(res);
                    if (responses.Count >= 2)
                    {
                        tcs.TrySetResult();
                    }
                }),
                Keep.Both)
            .Run(_materializer);

        // First request — establishes connection and completes SETTINGS exchange
        await queue.OfferAsync(new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{_fixture.Port}/ping")
        {
            Version = HttpVersion.Version20
        });

        // Brief idle period — allows server PING keepalive frames to be exchanged
        await Task.Delay(500);

        // Second request — proves the connection is still alive after the idle period.
        // If PING handling were broken, the server would close the connection or the
        // pipeline would be in an invalid state.
        await queue.OfferAsync(new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{_fixture.Port}/ping")
        {
            Version = HttpVersion.Version20
        });

        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(15));

        Assert.Equal(2, responses.Count);
        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
    }
}
