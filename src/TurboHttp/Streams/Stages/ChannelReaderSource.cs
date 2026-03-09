using System;
using System.Threading.Channels;
using System.Threading.Tasks;
using Akka.Streams;
using Akka.Streams.Stage;

namespace TurboHttp.Streams.Stages;

/// <summary>
/// An Akka.Streams Source that emits elements from a <see cref="ChannelReader{T}"/>.
/// Follows demand-driven back-pressure: elements are only read when downstream requests them.
/// The source completes when the channel is completed (writer calls Complete()).
/// </summary>
internal sealed class ChannelReaderSource<T> : GraphStage<SourceShape<T>>
{
    private readonly ChannelReader<T> _reader;
    private readonly Outlet<T> _outlet;

    public override SourceShape<T> Shape { get; }

    public ChannelReaderSource(ChannelReader<T> reader)
    {
        _reader = reader;
        _outlet = new Outlet<T>("channel-reader.out");
        Shape = new SourceShape<T>(_outlet);
    }

    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        => new Logic(this);

    private sealed class Logic : GraphStageLogic
    {
        private readonly ChannelReaderSource<T> _stage;
        private Action? _onItemAvailable;
        private Action? _onChannelComplete;

        public Logic(ChannelReaderSource<T> stage) : base(stage.Shape)
        {
            _stage = stage;

            SetHandler(stage._outlet,
                onPull: TryPush,
                onDownstreamFinish: _ => CompleteStage());
        }

        public override void PreStart()
        {
            _onItemAvailable = GetAsyncCallback(TryPush);
            _onChannelComplete = GetAsyncCallback(CompleteStage);
        }

        private void TryPush()
        {
            if (_stage._reader.TryRead(out var item))
            {
                Push(_stage._outlet, item);
                return;
            }

            _stage._reader
                .WaitToReadAsync()
                .AsTask()
                .ContinueWith(t =>
                {
                    if (t is { IsCompletedSuccessfully: true, Result: true })
                    {
                        _onItemAvailable!();
                    }
                    else
                    {
                        _onChannelComplete!();
                    }
                }, TaskContinuationOptions.ExecuteSynchronously);
        }
    }
}
