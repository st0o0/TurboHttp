using System.Buffers;
using System.Net.Http;
using Akka;
using Akka.Streams;
using Akka.Streams.Dsl;
using TurboHttp.Protocol;

namespace TurboHttp.Streams;

public class Http20Engine : IHttpProtocolEngine
{
    public BidiFlow<HttpRequestMessage, (IMemoryOwner<byte>, int), (IMemoryOwner<byte>, int), HttpResponseMessage,
        NotUsed> CreateFlow()
    {
        var requestEncoder = new Http2RequestEncoder();

        return BidiFlow.FromGraph(GraphDsl.Create(b =>
        {
            var requestToFrame = b.Add(new Stages.Request2Http2FrameStage(requestEncoder));
            var frameEncoder = b.Add(new Stages.Http2FrameEncoderStage());
            var prependPreface = b.Add(new Stages.PrependPrefaceStage());
            var frameDecoder = b.Add(new Stages.Http2FrameDecoderStage());
            var streamDecoder = b.Add(new Stages.Http2StreamStage());
            var connection = b.Add(new Stages.Http2ConnectionStage());

            b.From(requestToFrame.Outlet).To(connection.Inlet2);
            b.From(connection.Outlet2).To(frameEncoder.Inlet);
            b.From(frameEncoder.Outlet).To(prependPreface.Inlet);
            b.From(frameDecoder.Outlet).To(connection.Inlet1);
            b.From(connection.Outlet1).To(streamDecoder.Inlet);

            return new BidiShape<
                HttpRequestMessage,
                (IMemoryOwner<byte> buffer, int readableBytes),
                (IMemoryOwner<byte> buffer, int readableBytes),
                HttpResponseMessage>(
                requestToFrame.Inlet,
                prependPreface.Outlet,
                frameDecoder.Inlet,
                streamDecoder.Outlet);
        }));
    }
}