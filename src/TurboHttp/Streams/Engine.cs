using System;
using System.Buffers;
using System.Net;
using System.Net.Http;
using System.Runtime.CompilerServices;
using Akka;
using Akka.Actor;
using Akka.Streams;
using Akka.Streams.Dsl;
using TurboHttp.Client;
using TurboHttp.Streams.Stages;

namespace TurboHttp.Streams;

public class Engine
{
    public Flow<HttpRequestMessage, HttpResponseMessage, NotUsed> CreateFlow(IActorRef clientManager)
    {
        return Flow.FromGraph(GraphDsl.Create(builder =>
        {
            var enricher = builder.Add(new RequestEnricherStage(() => null));

            var partition = builder.Add(Router());
            var hub = builder.Add(new Merge<HttpResponseMessage>(4));

            var http10 = builder.Add(BuildProtocolFlow<Http10Engine>(4, clientManager));
            var http11 = builder.Add(BuildProtocolFlow<Http11Engine>(4, clientManager));
            var http20 = builder.Add(BuildProtocolFlow<Http20Engine>(1, clientManager));
            var http30 = builder.Add(BuildProtocolFlow<Http30Engine>(1, clientManager));

            builder.From(enricher.Outlet).To(partition);
            builder.From(partition.Out(0)).Via(http10).To(hub);
            builder.From(partition.Out(1)).Via(http11).To(hub);
            builder.From(partition.Out(2)).Via(http20).To(hub);
            builder.From(partition.Out(3)).Via(http30).To(hub);

            return new FlowShape<HttpRequestMessage, HttpResponseMessage>(enricher.Inlet, hub.Out);
        }));
    }

    internal Flow<HttpRequestMessage, HttpResponseMessage, NotUsed> CreateFlow(
        Func<Flow<ITransportItem, (IMemoryOwner<byte>, int), NotUsed>> http10Factory,
        Func<Flow<ITransportItem, (IMemoryOwner<byte>, int), NotUsed>> http11Factory,
        Func<Flow<ITransportItem, (IMemoryOwner<byte>, int), NotUsed>> http20Factory,
        Func<Flow<ITransportItem, (IMemoryOwner<byte>, int), NotUsed>> http30Factory)
    {
        return Flow.FromGraph(GraphDsl.Create(builder =>
        {
            // For testing, provide a minimal options object instead of null
            var holder = new HttpRequestMessage();
            var defaultOptions = new TurboRequestOptions(
                BaseAddress: null,
                DefaultRequestHeaders: holder.Headers,
                DefaultRequestVersion: HttpVersion.Version11,
                DefaultVersionPolicy: HttpVersionPolicy.RequestVersionOrHigher,
                Timeout: TimeSpan.FromSeconds(30),
                MaxResponseContentBufferSize: 1024 * 1024);

            var enricher = builder.Add(new RequestEnricherStage(() => defaultOptions));

            // Custom 3-port partition for testing (HTTP/3.0 not yet implemented)
            var partition = builder.Add(new Partition<HttpRequestMessage>(3, msg
                => msg.Version switch
                {
                    { Major: 2, Minor: 0 } => 2,
                    { Major: 1, Minor: 1 } => 1,
                    { Major: 1, Minor: 0 } => 0,
                    _ => throw new SwitchExpressionException(msg.Version)
                }));

            var hub = builder.Add(new Merge<HttpResponseMessage>(3));

            var http10 = builder.Add(BuildProtocolFlow<Http10Engine>(1, ActorRefs.Nobody, http10Factory));
            var http11 = builder.Add(BuildProtocolFlow<Http11Engine>(1, ActorRefs.Nobody, http11Factory));
            var http20 = builder.Add(BuildProtocolFlow<Http20Engine>(1, ActorRefs.Nobody, http20Factory));

            builder.From(enricher.Outlet).To(partition);
            builder.From(partition.Out(0)).Via(http10).To(hub);
            builder.From(partition.Out(1)).Via(http11).To(hub);
            builder.From(partition.Out(2)).Via(http20).To(hub);

            return new FlowShape<HttpRequestMessage, HttpResponseMessage>(enricher.Inlet, hub.Out);
        }));
    }

    private static IGraph<FlowShape<HttpRequestMessage, HttpResponseMessage>, NotUsed> BuildProtocolFlow<TEngine>(
        int connectionCount,
        IActorRef clientManager,
        Func<Flow<ITransportItem, (IMemoryOwner<byte>, int), NotUsed>>? transportFactory = null)
        where TEngine : IHttpProtocolEngine, new()
    {
        return GraphDsl.Create(builder =>
        {
            var balance = builder.Add(new Balance<HttpRequestMessage>(connectionCount));
            var merge = builder.Add(new Merge<HttpResponseMessage>(connectionCount));

            for (var i = 0; i < connectionCount; i++)
            {
                var tcp = transportFactory?.Invoke() ?? Flow.FromGraph(new ConnectionStage(clientManager));
                var conn = builder.Add(new TEngine().CreateFlow().Join(tcp));
                builder.From(balance.Out(i)).Via(conn).To(merge.In(i));
            }

            return new FlowShape<HttpRequestMessage, HttpResponseMessage>(balance.In, merge.Out);
        });
    }

    private static Partition<HttpRequestMessage> Router()
    {
        return new Partition<HttpRequestMessage>(4, msg
            => msg.Version switch
            {
                { Major: 3, Minor: 0 } => 3,
                { Major: 2, Minor: 0 } => 2,
                { Major: 1, Minor: 1 } => 1,
                { Major: 1, Minor: 0 } => 0
            });
    }
}