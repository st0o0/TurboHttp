using System;
using System.Buffers;
using System.Net.Http;
using Akka;
using Akka.Streams;
using Akka.Streams.Dsl;
using Akka.Streams.Stage;
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
            var prependPreface = b.Add(new PrependPrefaceStage());
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

    // ── RFC 7540 §3.5 — prepend connection preface to the first outbound bytes ──

    private sealed class PrependPrefaceStage : GraphStage<FlowShape<(IMemoryOwner<byte>, int), (IMemoryOwner<byte>, int)>>
    {
        private readonly Inlet<(IMemoryOwner<byte>, int)> _inlet = new("preface.in");
        private readonly Outlet<(IMemoryOwner<byte>, int)> _outlet = new("preface.out");

        public override FlowShape<(IMemoryOwner<byte>, int), (IMemoryOwner<byte>, int)> Shape
            => new(_inlet, _outlet);

        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
            => new Logic(this);

        private sealed class Logic : GraphStageLogic
        {
            private bool _prefaceSent;

            public Logic(PrependPrefaceStage stage) : base(stage.Shape)
            {
                SetHandler(stage._outlet, onPull: () =>
                {
                    if (!_prefaceSent)
                    {
                        _prefaceSent = true;
                        var preface = Http2Encoder.BuildConnectionPreface();
                        var owner = MemoryPool<byte>.Shared.Rent(preface.Length);
                        ((ReadOnlySpan<byte>)preface).CopyTo(owner.Memory.Span);
                        Push(stage._outlet, (owner, preface.Length));
                    }
                    else
                    {
                        Pull(stage._inlet);
                    }
                });

                SetHandler(stage._inlet,
                    onPush: () => Push(stage._outlet, Grab(stage._inlet)),
                    onUpstreamFinish: CompleteStage,
                    onUpstreamFailure: FailStage);
            }
        }
    }
}