using System;
using System.Buffers;
using System.Net;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Event;

namespace Servus.Akka.IO;

public class TcpClientRunner : ReceiveActor
{
    private readonly IStreamProvider _streamProvider;
    private readonly CancellationTokenSource _cts = new();
    private readonly IActorRef _handler;
    private readonly IActorRef _selfClosure;
    private readonly TcpOptions _options;
    private readonly Channel<(IMemoryOwner<byte> buffer, int readableBytes)>? _inboundChannel;
    private readonly Channel<(IMemoryOwner<byte> buffer, int readableBytes)>? _outboundChannel;
    private TcpClientState? _state;
    private bool _poisonPillSent;

    public record ClientConnected(
        EndPoint RemoteEndPoint,
        ChannelReader<(IMemoryOwner<byte> buffer, int readableBytes)> InboundReader,
        ChannelWriter<(IMemoryOwner<byte> buffer, int readableBytes)> OutboundWriter);

    public record ClientDisconnected(EndPoint RemoteEndPoint);

    private record BackgroundTasksCompleted : IDeadLetterSuppression;

    public record DoConnect : IDeadLetterSuppression;

    public record DoClose : IDeadLetterSuppression;

    public TcpClientRunner(IStreamProvider streamProvider, TcpOptions options, IActorRef handler,
        Channel<(IMemoryOwner<byte> buffer, int readableBytes)>? inboundChannel = null,
        Channel<(IMemoryOwner<byte> buffer, int readableBytes)>? outboundChannel = null)
    {
        _streamProvider = streamProvider;
        _handler = handler;
        _options = options;
        _inboundChannel = inboundChannel;
        _outboundChannel = outboundChannel;

        _selfClosure = Context.Self;

        ReceiveAsync<DoConnect>(async msg =>
        {
            if (_state is not null)
            {
                Sender.Tell(new ClientConnected(_streamProvider.RemoteEndPoint!, _state.InboundReader,
                    _state.OutboundWriter));
                return;
            }

            var stream = await _streamProvider
                .ConnectAsync(_options.Host, _options.Port, _cts.Token)
                .ConfigureAwait(false);
            _state = new TcpClientState(_options.MaxFrameSize, stream, _inboundChannel, _outboundChannel);
            _handler.Tell(new ClientConnected(_streamProvider.RemoteEndPoint!, _state.InboundReader,
                _state.OutboundWriter));
        });
        Receive<DoClose>(_ =>
        {
            _cts.Cancel();
            _handler.Tell(new ClientDisconnected(_streamProvider.RemoteEndPoint!));
        });
        Receive<BackgroundTasksCompleted>(_ => SendPoisonPillOnce());
    }

    public void BecomeUnconnected()
    {
        Become(() =>
        {
            ReceiveAsync<DoConnect>(async _ =>
            {
                var stream = await _streamProvider
                    .ConnectAsync(_options.Host, _options.Port, _cts.Token)
                    .ConfigureAwait(false);
                _state = new TcpClientState(_options.MaxFrameSize, stream, _inboundChannel, _outboundChannel);
                _handler.Tell(new ClientConnected(_streamProvider.RemoteEndPoint!, _state.InboundReader,
                    _state.OutboundWriter));
            });
        });
    }

    public void BecomeConnected()
    {
        Become(() =>
        {
            Receive<DoConnect>(_ =>
            {
                if (_state is null)
                {
                    BecomeUnconnected();
                    return;
                }

                Sender.Tell(new ClientConnected(_streamProvider.RemoteEndPoint!, _state.InboundReader,
                    _state.OutboundWriter));
            });
            Receive<DoClose>(_ =>
            {
                _cts.Cancel();
                _handler.Tell(new ClientDisconnected(_streamProvider.RemoteEndPoint!));
            });
            Receive<BackgroundTasksCompleted>(_ => SendPoisonPillOnce());
        });
    }

    protected override void PreStart()
    {
        if (_state is null)
        {
            return;
        }

        var moveStreamToPipeTask = TcpClientByteMover.MoveStreamToPipe(_state, _selfClosure, _cts.Token);
        var movePipeToChannelTask = TcpClientByteMover.MovePipeToChannel(_state, _selfClosure, _cts.Token);
        var moveChannelToStreamTask = TcpClientByteMover.MoveChannelToStream(_state, _selfClosure, _cts.Token);

        Task.WhenAll(moveStreamToPipeTask, movePipeToChannelTask, moveChannelToStreamTask)
            .ContinueWith(_ => { _selfClosure.Tell(new BackgroundTasksCompleted()); },
                TaskContinuationOptions.ExecuteSynchronously);
    }

    protected override void PostStop()
    {
        if (_state is null)
        {
            return;
        }

        _state.InboundWriter.TryComplete();
        _state.OutboundWriter.TryComplete();

        if (!_cts.IsCancellationRequested)
        {
            _cts.Cancel();
        }

        try
        {
            _state.Pipe.Reader.Complete();
            _state.Pipe.Writer.Complete();
            _state.Stream.Close();
            _state.Stream.Dispose();
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            throw;
        }
    }

    private void SendPoisonPillOnce()
    {
        if (!_poisonPillSent)
        {
            _poisonPillSent = true;
            _selfClosure.Tell(PoisonPill.Instance);
        }
    }
}
