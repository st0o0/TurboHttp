using System;
using System.Collections.Generic;
using Akka.Actor;
using Akka.Streams;
using Akka.Streams.Stage;

namespace TurboHttp.IO.Stages;

/// <summary>
/// Central connection pool stage that orchestrates multiple <see cref="ConnectionStage"/> instances
/// per host. Routes <see cref="RoutedTransportItem"/> to per-host connection pools and emits
/// <see cref="RoutedDataItem"/> responses.
/// </summary>
public sealed class ConnectionPoolStage : GraphStage<FlowShape<RoutedTransportItem, RoutedDataItem>>
{
    private readonly Func<ConnectionStage> _connectionStageFactory;
    private readonly PoolConfig _config;

    private readonly Inlet<RoutedTransportItem> _inlet = new("pool.in");
    private readonly Outlet<RoutedDataItem> _outlet = new("pool.out");

    public override FlowShape<RoutedTransportItem, RoutedDataItem> Shape { get; }

    public ConnectionPoolStage(Func<ConnectionStage> connectionStageFactory, PoolConfig config)
    {
        _connectionStageFactory = connectionStageFactory ?? throw new ArgumentNullException(nameof(connectionStageFactory));
        _config = config ?? throw new ArgumentNullException(nameof(config));
        Shape = new FlowShape<RoutedTransportItem, RoutedDataItem>(_inlet, _outlet);
    }

    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        => new Logic(this);

    private sealed class Logic : GraphStageLogic
    {
        private readonly ConnectionPoolStage _stage;
        private IActorRef _self = ActorRefs.Nobody;
        private bool _stopping;

        /// <summary>
        /// Tracks which pool keys have been registered via a ConnectItem.
        /// Maps pool key → the TcpOptions from the ConnectItem.
        /// </summary>
        private readonly Dictionary<string, HostRegistration> _registeredHosts = new();

        /// <summary>
        /// Pending responses waiting to be pushed downstream.
        /// </summary>
        private readonly Queue<RoutedDataItem> _pendingResponses = new();

        public Logic(ConnectionPoolStage stage) : base(stage.Shape)
        {
            _stage = stage;

            SetHandler(stage._inlet, onPush: HandlePush, onUpstreamFinish: () =>
            {
                _stopping = true;
                if (_pendingResponses.Count == 0)
                {
                    CompleteStage();
                }
            });

            SetHandler(stage._outlet, onPull: () =>
            {
                if (_pendingResponses.TryDequeue(out var item))
                {
                    Push(_stage._outlet, item);
                }
                else if (_stopping)
                {
                    CompleteStage();
                }
            }, onDownstreamFinish: _ =>
            {
                _stopping = true;
                CompleteStage();
            });
        }

        public override void PreStart()
        {
            _self = GetStageActor(OnMessage).Ref;
            Pull(_stage._inlet);
        }

        private void HandlePush()
        {
            var elem = Grab(_stage._inlet);

            switch (elem.Item)
            {
                case ConnectItem connect:
                    RegisterHost(elem.PoolKey, connect);
                    Pull(_stage._inlet);
                    break;

                case DataItem data:
                    if (!_registeredHosts.ContainsKey(elem.PoolKey))
                    {
                        FailStage(new InvalidOperationException(
                            $"Received DataItem for unknown pool key '{elem.PoolKey}'. " +
                            $"A ConnectItem must be sent before any DataItem for a given pool key."));
                        return;
                    }

                    // TASK-001: Foundation only — data routing will be implemented in TASK-002.
                    // For now, just pull next to avoid stalling.
                    Pull(_stage._inlet);
                    break;

                default:
                    FailStage(new InvalidOperationException(
                        $"Unexpected transport item type: {elem.Item?.GetType().Name ?? "null"}"));
                    break;
            }
        }

        private void RegisterHost(string poolKey, ConnectItem connect)
        {
            if (!_registeredHosts.ContainsKey(poolKey))
            {
                _registeredHosts[poolKey] = new HostRegistration(connect.Options);
            }
        }

        private void OnMessage((IActorRef sender, object msg) args)
        {
            // TASK-001: Foundation only — actor message handling will be needed
            // in later tasks for sub-graph communication.
        }

        public override void PostStop()
        {
            _stopping = true;
            _registeredHosts.Clear();
        }
    }

    /// <summary>
    /// Tracks a registered host (ConnectItem received) and its TCP options.
    /// Will be extended in later tasks with connection slots, activity tracking, etc.
    /// </summary>
    private sealed class HostRegistration
    {
        public TcpOptions Options { get; }

        public HostRegistration(TcpOptions options)
        {
            Options = options;
        }
    }
}
