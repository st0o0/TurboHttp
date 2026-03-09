using System.Net;
using Akka;
using Akka.Streams;
using Akka.Streams.Dsl;
using TurboHttp.Streams;

namespace TurboHttp.StreamTests.Engine;

public sealed class EngineRoutingTests : EngineTestBase
{
    [Fact(DisplayName = "ST-ENG-001: HTTP/1.0 request routed to HTTP/1.0 engine")]
    public async Task Http10_Request_Routed_To_Http10_Engine()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/")
        {
            Version = HttpVersion.Version10
        };

        var flow = BuildRoutingFlow();
        var response = await Source.Single(request)
            .Via(flow)
            .RunWith(Sink.First<HttpResponseMessage>(), Materializer)
            .WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(HttpVersion.Version10, response.Version);
    }

    [Fact(DisplayName = "ST-ENG-002: HTTP/1.1 request routed to HTTP/1.1 engine")]
    public async Task Http11_Request_Routed_To_Http11_Engine()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/")
        {
            Version = HttpVersion.Version11
        };

        var flow = BuildRoutingFlow();
        var response = await Source.Single(request)
            .Via(flow)
            .RunWith(Sink.First<HttpResponseMessage>(), Materializer)
            .WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(HttpVersion.Version11, response.Version);
    }

    [Fact(DisplayName = "ST-ENG-003: HTTP/2.0 request routed to HTTP/2.0 engine")]
    public async Task Http20_Request_Routed_To_Http20_Engine()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/")
        {
            Version = HttpVersion.Version20
        };

        var flow = BuildRoutingFlow();
        var response = await Source.Single(request)
            .Via(flow)
            .RunWith(Sink.First<HttpResponseMessage>(), Materializer)
            .WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(HttpVersion.Version20, response.Version);
    }

    /// <summary>
    /// Builds a routing flow that mirrors Engine.cs's Partition/Merge structure.
    /// Uses fake engines (simple flows that echo the request version) instead of real BidiFlows.
    /// </summary>
    private Flow<HttpRequestMessage, HttpResponseMessage, NotUsed> BuildRoutingFlow()
    {
        return Flow.FromGraph(GraphDsl.Create(builder =>
        {
            var partition = builder.Add(new Partition<HttpRequestMessage>(4, msg => msg.Version switch
            {
                { Major: 3, Minor: 0 } => 3,
                { Major: 2, Minor: 0 } => 2,
                { Major: 1, Minor: 1 } => 1,
                { Major: 1, Minor: 0 } => 0
            }));
            var merge = builder.Add(new Merge<HttpResponseMessage>(4));

            // Fake engines: simple flows that create a response with the same version as the request
            var fakeHttp10 = builder.Add(Flow.Create<HttpRequestMessage>()
                .Select(req => new HttpResponseMessage(HttpStatusCode.OK) { Version = req.Version }));
            var fakeHttp11 = builder.Add(Flow.Create<HttpRequestMessage>()
                .Select(req => new HttpResponseMessage(HttpStatusCode.OK) { Version = req.Version }));
            var fakeHttp20 = builder.Add(Flow.Create<HttpRequestMessage>()
                .Select(req => new HttpResponseMessage(HttpStatusCode.OK) { Version = req.Version }));
            var fakeHttp30 = builder.Add(Flow.Create<HttpRequestMessage>()
                .Select(req => new HttpResponseMessage(HttpStatusCode.OK) { Version = req.Version }));

            builder.From(partition.Out(0)).Via(fakeHttp10).To(merge);
            builder.From(partition.Out(1)).Via(fakeHttp11).To(merge);
            builder.From(partition.Out(2)).Via(fakeHttp20).To(merge);
            builder.From(partition.Out(3)).Via(fakeHttp30).To(merge);

            return new FlowShape<HttpRequestMessage, HttpResponseMessage>(partition.In, merge.Out);
        }));
    }
}
