using System.Buffers;
using System.Net.Http;
using Akka;
using Akka.Streams;
using Akka.Streams.Dsl;
using TurboHttp.Streams.Stages;

namespace TurboHttp.Streams;

public class Http10Engine : IHttpProtocolEngine
{
    public BidiFlow<HttpRequestMessage, (IMemoryOwner<byte>, int), (IMemoryOwner<byte>, int), HttpResponseMessage,
        NotUsed> CreateFlow()
    {
        return BidiFlow.FromGraph(GraphDsl.Create(b =>
        {
            var requestEncoder = b.Add(new Http10EncoderStage());
            var responseDecoder = b.Add(new Http10DecoderStage());

            return new BidiShape<
                HttpRequestMessage,
                (IMemoryOwner<byte>, int),
                (IMemoryOwner<byte>, int),
                HttpResponseMessage>(
                requestEncoder.Inlet,
                requestEncoder.Outlet,
                responseDecoder.Inlet,
                responseDecoder.Outlet);
        }));
    }
}