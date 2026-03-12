using System.Collections.Generic;
using Akka.Actor;
using Akka.Streams;
using Akka.Streams.Stage;
using TurboHttp.IO.Stages;

namespace TurboHttp.IO;

public sealed class ConnectionPoolStageTest : GraphStage<FlowShape<RoutedTransportItem, RoutedDataItem>>
{
    private readonly IActorRef _router;

    private readonly Inlet<RoutedTransportItem> _inlet = new("connectionpool.in");
    private readonly Outlet<RoutedDataItem> _outlet = new("connectionpool.out");

    public override FlowShape<RoutedTransportItem, RoutedDataItem> Shape { get; }

    public ConnectionPoolStageTest(IActorRef router)
    {
        _router = router;
        Shape = new FlowShape<RoutedTransportItem, RoutedDataItem>(_inlet, _outlet);
    }

    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
    {
        return new Logic(this);
    }

    private sealed class Logic : GraphStageLogic
    {
        private readonly ConnectionPoolStageTest _stage;

        private readonly Queue<RoutedDataItem> _responses = new();

        public Logic(ConnectionPoolStageTest stage) : base(stage.Shape)
        {
            _stage = stage;
        }

        public override void PreStart()
        {
            var stageActor = GetStageActor(OnMessage);

            SetHandler(_stage._inlet, onPush: () =>
            {
                var item = Grab(_stage._inlet);

                if (item.Item is DataItem data)
                {
                    _stage._router.Tell(
                        new PoolRouterActor.SendRequest(
                            item.PoolKey,
                            data,
                            stageActor.Ref));
                }

                Pull(_stage._inlet);
            });

            SetHandler(_stage._outlet, onPull: PushIfAvailable);
        }

        private void OnMessage((IActorRef sender, object msg) args)
        {
            if (args.msg is PoolRouterActor.Response resp)
            {
                _responses.Enqueue(
                    new RoutedDataItem(
                        resp.PoolKey,
                        resp.Memory,
                        resp.Length));

                PushIfAvailable();
            }
        }

        private void PushIfAvailable()
        {
            if (_responses.Count > 0 && IsAvailable(_stage._outlet))
            {
                Push(_stage._outlet, _responses.Dequeue());
            }
        }
    }
}
