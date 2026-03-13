using System;
using System.Buffers;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Event;
using Akka.Streams;
using Akka.Streams.Dsl;
using TurboHttp.IO.Stages;

namespace TurboHttp.IO;

public sealed class ConnectionActor : ReceiveActor
{
    public sealed record GetStreamRefs;

    public sealed record StreamRefsResponse(ISinkRef<IDataItem> Requests, ISourceRef<IDataItem> Responses);

    private readonly TcpOptions _options;
    private readonly IActorRef _clientManager;

    private ChannelWriter<(IMemoryOwner<byte>, int)>? _outbound;
    private ChannelReader<(IMemoryOwner<byte>, int)>? _inbound;

    private readonly CancellationTokenSource _cts = new();

    private readonly ILoggingAdapter _log = Context.GetLogger();

    private IActorRef? _runner;

    private ISourceQueueWithComplete<IDataItem>? _responseQueue;

    public ConnectionActor(TcpOptions options, IActorRef clientManager)
    {
        _options = options;
        _clientManager = clientManager;

        Receive<ClientRunner.ClientConnected>(HandleConnected);
        Receive<ClientRunner.ClientDisconnected>(HandleDisconnected);
        Receive<DataItem>(HandleSend);
        ReceiveAsync<GetStreamRefs>(HandleGetStreamRefs);
        Receive<Terminated>(HandleTerminated);
    }

    protected override void PreStart()
    {
        Connect();
    }

    private void Connect()
    {
        _clientManager.Tell(new ClientManager.CreateTcpRunner(_options, Self));
    }

    private void HandleConnected(ClientRunner.ClientConnected msg)
    {
        _log.Debug("Connected {0}", msg.RemoteEndPoint);

        _inbound = msg.InboundReader;
        _outbound = msg.OutboundWriter;
        _runner = Sender;

        Context.Watch(_runner);

        _ = PumpInbound(_cts.Token);
    }

    private async Task PumpInbound(CancellationToken token)
    {
        try
        {
            await foreach (var item in _inbound!.ReadAllAsync(token))
            {
                if (_responseQueue != null)
                {
                    await _responseQueue.OfferAsync(new DataItem(item.Item1, item.Item2));
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Inbound pump failed");
        }
    }

    private void HandleSend(DataItem data)
    {
        if (_outbound == null)
        {
            data.Memory.Dispose();
            return;
        }

        if (!_outbound.TryWrite((data.Memory, data.Length)))
        {
            data.Memory.Dispose();
        }
    }

    private void HandleDisconnected(ClientRunner.ClientDisconnected msg)
    {
        _log.Warning("Disconnected {0}", msg.RemoteEndPoint);
        Reconnect();
    }

    private void HandleTerminated(Terminated msg)
    {
        if (msg.ActorRef.Equals(_runner))
        {
            _log.Warning("ClientRunner terminated");
            Reconnect();
        }
    }

    private void Reconnect()
    {
        _runner = null;
        _outbound = null;
        _inbound = null;

        Connect();
    }

    private async Task HandleGetStreamRefs(GetStreamRefs _)
    {
        var mat = Context.System.Materializer();

        // ---------- RESPONSE STREAM ----------
        var responseMat =
            Source.Queue<IDataItem>(1024, OverflowStrategy.Backpressure)
                .PreMaterialize(mat);

        _responseQueue = responseMat.Item1;

        var sourceRef = await responseMat.Item2
            .RunWith(StreamRefs.SourceRef<IDataItem>(), mat);


        // ---------- REQUEST STREAM ----------
        var requestSink = Sink.ForEachAsync<IDataItem>(1, async x => await _outbound!.WriteAsync((x.Memory, x.Length)));

        var sinkRef = await requestSink
            .RunWith(StreamRefs.SinkRef<IDataItem>(), mat);


        Sender.Tell(new StreamRefsResponse(sinkRef, sourceRef));
    }

    protected override void PostStop()
    {
        _cts.Cancel();

        try
        {
            _runner?.Tell(new DoClose());
        }
        catch
        {
            // noop
        }

        _responseQueue?.Complete();
    }
}