using System.Buffers;
using System.Net.Http;
using Akka;
using Akka.Streams;
using Akka.Streams.Dsl;
using TurboHttp.IO.Stages;
using TurboHttp.Streams.Stages;

namespace TurboHttp.Streams;

public class Http10Engine : IHttpProtocolEngine
{
    public BidiFlow<HttpRequestMessage, ITransportItem, IDataItem, HttpResponseMessage, NotUsed> CreateFlow()
    {
        return BidiFlow.FromGraph(GraphDsl.Create(b =>
        {
            var encoder = b.Add(new Http10EncoderStage());
            var decoder = b.Add(new Http10DecoderStage());
            var correlation = b.Add(new Http1XCorrelationStage());

            var requestBCast = b.Add(new Broadcast<HttpRequestMessage>(2));

            var flowOut = b.Add(Flow.Create<(IMemoryOwner<byte>, int), ITransportItem>()
                .Select(ITransportItem (x) => new DataItem(x.Item1, x.Item2)));
            var flowIn = b.Add(Flow.Create<IDataItem>().Select(x => (x.Memory, x.Length)));

            b.From(requestBCast).Via(encoder).To(flowOut.Inlet);
            b.From(requestBCast).To(correlation.In0);

            b.From(flowIn.Outlet).Via(decoder).To(correlation.In1);

            return new BidiShape<
                HttpRequestMessage,
                ITransportItem,
                IDataItem,
                HttpResponseMessage>(
                requestBCast.In,
                flowOut.Outlet,
                flowIn.Inlet,
                correlation.Out);
        }));
    }
}