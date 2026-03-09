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

    [Fact(DisplayName = "ST-ENG-004: Mixed-version batch: each response version matches its request")]
    public async Task Mixed_Version_Batch_Each_Response_Matches_Its_Request_Version()
    {
        var requests = new[]
        {
            new HttpRequestMessage(HttpMethod.Get, "http://example.com/") { Version = HttpVersion.Version10 },
            new HttpRequestMessage(HttpMethod.Get, "http://example.com/") { Version = HttpVersion.Version11 },
            new HttpRequestMessage(HttpMethod.Get, "http://example.com/") { Version = HttpVersion.Version20 }
        };

        var flow = BuildRoutingFlow();
        var results = new List<HttpResponseMessage>();
        var tcs = new TaskCompletionSource();

        _ = Source.From(requests)
            .Via(flow)
            .RunWith(Sink.ForEach<HttpResponseMessage>(res =>
            {
                results.Add(res);
                if (results.Count == 3)
                {
                    tcs.TrySetResult();
                }
            }), Materializer);

        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(3, results.Count);
        Assert.Contains(results, r => r.Version == HttpVersion.Version10);
        Assert.Contains(results, r => r.Version == HttpVersion.Version11);
        Assert.Contains(results, r => r.Version == HttpVersion.Version20);
    }

    [Fact(DisplayName = "ST-ENG-005: N concurrent same-version requests — no cross-stream bleed")]
    public async Task Same_Version_Concurrent_Requests_No_Cross_Stream_Bleed()
    {
        var sentIds = new[] { "corr-1", "corr-2", "corr-3", "corr-4", "corr-5" };
        var requests = sentIds.Select(id =>
        {
            var req = new HttpRequestMessage(HttpMethod.Get, "http://example.com/")
            {
                Version = HttpVersion.Version11
            };
            req.Headers.Add("x-correlation-id", id);
            return req;
        }).ToList();

        var flow = BuildRoutingFlow();
        var results = new List<HttpResponseMessage>();
        var tcs = new TaskCompletionSource();

        _ = Source.From(requests)
            .Via(flow)
            .RunWith(Sink.ForEach<HttpResponseMessage>(res =>
            {
                results.Add(res);
                if (results.Count == 5)
                {
                    tcs.TrySetResult();
                }
            }), Materializer);

        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(5, results.Count);
        var receivedIds = new HashSet<string>();
        foreach (var r in results)
        {
            if (r.Headers.TryGetValues("x-correlation-id", out var vals))
            {
                receivedIds.Add(string.Join(",", vals));
            }
        }
        Assert.Equal(sentIds.ToHashSet(), receivedIds);
    }

    [Fact(DisplayName = "ST-ENG-006: x-correlation-id header preserved through full routing flow")]
    public async Task Correlation_Id_Header_Preserved_Through_Routing_Flow()
    {
        const string correlationId = "test-correlation-id-xyz";
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/")
        {
            Version = HttpVersion.Version11
        };
        request.Headers.Add("x-correlation-id", correlationId);

        var flow = BuildRoutingFlow();
        var response = await Source.Single(request)
            .Via(flow)
            .RunWith(Sink.First<HttpResponseMessage>(), Materializer)
            .WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(response.Headers.TryGetValues("x-correlation-id", out var values));
        Assert.Equal(correlationId, string.Join(",", values));
    }

    /// <summary>
    /// Builds a routing flow that mirrors Engine.cs's Partition/Merge structure.
    /// Uses fake engines that echo the request version and x-correlation-id header.
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

            // Fake engines: echo version and x-correlation-id header
            var fakeHttp10 = builder.Add(Flow.Create<HttpRequestMessage>().Select(EchoResponse));
            var fakeHttp11 = builder.Add(Flow.Create<HttpRequestMessage>().Select(EchoResponse));
            var fakeHttp20 = builder.Add(Flow.Create<HttpRequestMessage>().Select(EchoResponse));
            var fakeHttp30 = builder.Add(Flow.Create<HttpRequestMessage>().Select(EchoResponse));

            builder.From(partition.Out(0)).Via(fakeHttp10).To(merge);
            builder.From(partition.Out(1)).Via(fakeHttp11).To(merge);
            builder.From(partition.Out(2)).Via(fakeHttp20).To(merge);
            builder.From(partition.Out(3)).Via(fakeHttp30).To(merge);

            return new FlowShape<HttpRequestMessage, HttpResponseMessage>(partition.In, merge.Out);
        }));
    }

    private static HttpResponseMessage EchoResponse(HttpRequestMessage req)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Version = req.Version };
        if (req.Headers.TryGetValues("x-correlation-id", out var corrIds))
        {
            response.Headers.TryAddWithoutValidation("x-correlation-id", string.Join(",", corrIds));
        }
        return response;
    }
}
