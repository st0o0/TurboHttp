using System.Buffers;
using System.Threading.Channels;
using Akka.Streams;
using Akka.Streams.Stage;

namespace TurboHttp.StreamTests;

/// <summary>
/// Fake TCP flow for engine round-trip tests.
/// Captures all outbound bytes from the engine in <see cref="Captured"/>.
/// On each inbound push, calls the response factory and emits response bytes downstream.
/// Use <see cref="Echo"/> for a variant that reflects inbound bytes back unchanged.
/// </summary>
public sealed class FakeTcpFlow : GraphStage<FlowShape<(IMemoryOwner<byte>, int), (IMemoryOwner<byte>, int)>>
{
    private readonly Func<byte[], byte[]>? _responseFactory;

    public Channel<(IMemoryOwner<byte>, int)> Captured { get; } =
        Channel.CreateUnbounded<(IMemoryOwner<byte>, int)>();

    public Inlet<(IMemoryOwner<byte>, int)> In { get; } = new("fake-tcp.in");
    public Outlet<(IMemoryOwner<byte>, int)> Out { get; } = new("fake-tcp.out");

    public override FlowShape<(IMemoryOwner<byte>, int), (IMemoryOwner<byte>, int)> Shape { get; }

    public FakeTcpFlow(Func<byte[]> responseFactory)
    {
        _responseFactory = _ => responseFactory();
        Shape = new FlowShape<(IMemoryOwner<byte>, int), (IMemoryOwner<byte>, int)>(In, Out);
    }

    /// <summary>Creates a FakeTcpFlow that reflects inbound bytes back as-is.</summary>
    public static FakeTcpFlow Echo() => new FakeTcpFlow(echo: true);

    private FakeTcpFlow(bool echo)
    {
        _responseFactory = echo ? inbound => inbound : null;
        Shape = new FlowShape<(IMemoryOwner<byte>, int), (IMemoryOwner<byte>, int)>(In, Out);
    }

    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this);

    private sealed class Logic : GraphStageLogic
    {
        private readonly FakeTcpFlow _stage;
        private readonly Queue<(IMemoryOwner<byte>, int)> _buffer = new();
        private bool _downstreamWaiting;

        public Logic(FakeTcpFlow stage) : base(stage.Shape)
        {
            _stage = stage;

            SetHandler(stage.In,
                onPush: () =>
                {
                    var (owner, length) = Grab(stage.In);

                    var copy = new byte[length];
                    owner.Memory.Span[..length].CopyTo(copy);
                    stage.Captured.Writer.TryWrite((new SimpleMemoryOwner(copy), length));
                    owner.Dispose();

                    var responseBytes = _stage._responseFactory!(copy);
                    IMemoryOwner<byte> responseOwner = new SimpleMemoryOwner(responseBytes);

                    if (_downstreamWaiting)
                    {
                        _downstreamWaiting = false;
                        Push(stage.Out, (responseOwner, responseBytes.Length));
                    }
                    else
                    {
                        _buffer.Enqueue((responseOwner, responseBytes.Length));
                    }

                    Pull(stage.In);
                },
                onUpstreamFinish: CompleteStage,
                onUpstreamFailure: FailStage);

            SetHandler(stage.Out,
                onPull: () =>
                {
                    if (_buffer.TryDequeue(out var chunk))
                    {
                        Push(stage.Out, chunk);
                    }
                    else
                    {
                        _downstreamWaiting = true;
                    }
                },
                onDownstreamFinish: _ => CompleteStage());
        }

        public override void PreStart() => Pull(_stage.In);
    }
}
