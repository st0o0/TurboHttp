using System.Buffers;
using System.Drawing;
using System.Net.Http;
using System.Security.Cryptography;
using Akka;
using Akka.Streams;
using Akka.Streams.Dsl;
using Akka.Streams.Stage;
using TurboHttp.IO;
using TurboHttp.Streams.Stages;

namespace TurboHttp.Streams;

public class Http11Engine : IHttpProtocolEngine
{
    public BidiFlow<HttpRequestMessage, (IMemoryOwner<byte>, int), (IMemoryOwner<byte>, int), HttpResponseMessage,
        NotUsed> CreateFlow()
    {
        return BidiFlow.FromGraph(GraphDsl.Create(b =>
        {
            var requestEncoder = b.Add(new Http11EncoderStage());
            var responseDecoder = b.Add(new Http11DecoderStage());

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

public record HttpRequestOptions;

public record HttpRequest(HttpRequestOptions Options, HttpRequestMessage RequestMessage);

public class Http11EngineTest
{
    public BidiFlow<HttpRequest, IConnectionItem, (IMemoryOwner<byte>, int), HttpResponseMessage,
        NotUsed> CreateFlow()
    {
        return BidiFlow.FromGraph(GraphDsl.Create(b =>
        {
            var extractOptions = b.Add(new ExtractOptionsStage());
            var requestEncoder = b.Add(new Http11EncoderStage());
            var merge = b.Add(new Merge<IConnectionItem>(2));
            var responseDecoder = b.Add(new Http11DecoderStage());
            var wrapDataInput = b.Add(Flow.Create<(IMemoryOwner<byte>, int)>()
                .Select(IConnectionItem (chunk) => new DataInput(chunk.Item1, chunk.Item2)));

            // InitialInput (Out0) → merge
            b.From(extractOptions.Out0).To(merge);

            // HttpRequest (Out1) → Encoder → merge
            b.From(extractOptions.Out1).Via(requestEncoder).Via(wrapDataInput).To(merge);

            return new BidiShape<
                HttpRequest,
                IConnectionItem,
                (IMemoryOwner<byte>, int),
                HttpResponseMessage>(
                extractOptions.In,
                merge.Out,
                responseDecoder.Inlet,
                responseDecoder.Outlet);
        }));
    }
}

internal sealed class ExtractOptionsStage : GraphStage<FanOutShape<HttpRequest, IConnectionItem, HttpRequestMessage>>
{
    private readonly Outlet<IConnectionItem> _outletOptions = new("");
    private readonly Outlet<HttpRequestMessage> _outletRequest = new("");
    private readonly Inlet<HttpRequest> _inletRequest = new("");
    public override FanOutShape<HttpRequest, IConnectionItem, HttpRequestMessage> Shape { get; }

    public ExtractOptionsStage()
    {
        Shape = new FanOutShape<HttpRequest, IConnectionItem, HttpRequestMessage>(_inletRequest, _outletOptions,
            _outletRequest);
    }

    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        => new Logic(this);

    private sealed class Logic : GraphStageLogic
    {
        private bool _initialSent;
        private HttpRequest? _pending;

        public Logic(ExtractOptionsStage stage) : base(stage.Shape)
        {
            SetHandler(stage.Shape.In,
                onPush: () =>
                {
                    var request = Grab(stage.Shape.In);

                    if (!_initialSent)
                    {
                        var options = new TcpOptions { Host = string.Empty, Port = 0 };
                        _pending = request;
                        _initialSent = true;
                        Push(stage.Shape.Out0, new InitialInput(options));
                    }
                    else
                    {
                        Push(stage.Shape.Out1, request.RequestMessage);
                    }
                },
                onUpstreamFinish: CompleteStage,
                onUpstreamFailure: FailStage);

            SetHandler(stage.Shape.Out0,
                onPull: () =>
                {
                    if (!_initialSent)
                    {
                        Pull(stage.Shape.In);
                    }
                }, onDownstreamFinish: _ => { });

            SetHandler(stage.Shape.Out1,
                onPull: () =>
                {
                    if (_pending is not null)
                    {
                        Push(stage.Shape.Out1, _pending.RequestMessage);
                        _pending = null;
                    }
                    else
                    {
                        Pull(stage.Shape.In);
                    }
                }, onDownstreamFinish: _ => CompleteStage());
        }
    }
}