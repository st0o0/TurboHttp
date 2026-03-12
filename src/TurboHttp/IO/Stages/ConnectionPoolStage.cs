using System;
using System.Buffers;
using System.Collections.Generic;
using System.Threading.Tasks;
using Akka;
using Akka.Actor;
using Akka.Streams;
using Akka.Streams.Dsl;
using Akka.Streams.Stage;

namespace TurboHttp.IO.Stages;

/// <summary>
/// Central connection pool stage that orchestrates multiple <see cref="ConnectionStage"/> instances
/// per host. Routes <see cref="RoutedTransportItem"/> to per-host connection pools and emits
/// <see cref="RoutedDataItem"/> responses.
/// </summary>
public sealed class ConnectionPoolStage : GraphStage<FlowShape<RoutedTransportItem, RoutedDataItem>>
{
    private readonly Func<IGraph<FlowShape<ITransportItem, (IMemoryOwner<byte>, int)>, NotUsed>> _connectionFlowFactory;
    private readonly PoolConfig _config;

    private readonly Inlet<RoutedTransportItem> _inlet = new("pool.in");
    private readonly Outlet<RoutedDataItem> _outlet = new("pool.out");

    public override FlowShape<RoutedTransportItem, RoutedDataItem> Shape { get; }

    public ConnectionPoolStage(
        Func<IGraph<FlowShape<ITransportItem, (IMemoryOwner<byte>, int)>, NotUsed>> connectionFlowFactory,
        PoolConfig config)
    {
        _connectionFlowFactory = connectionFlowFactory ?? throw new ArgumentNullException(nameof(connectionFlowFactory));
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
        private int _inFlightCount;

        /// <summary>
        /// Tracks per-host connection pools. Maps pool key → HostPool.
        /// </summary>
        private readonly Dictionary<string, HostPool> _hostPools = new();

        /// <summary>
        /// Pending responses waiting to be pushed downstream.
        /// </summary>
        private readonly Queue<RoutedDataItem> _pendingResponses = new();

        private Action<(string poolKey, IMemoryOwner<byte> memory, int length)>? _onResponseCallback;

        public Logic(ConnectionPoolStage stage) : base(stage.Shape)
        {
            _stage = stage;

            SetHandler(stage._inlet, onPush: HandlePush, onUpstreamFinish: () =>
            {
                _stopping = true;
                TryComplete();
            });

            SetHandler(stage._outlet, onPull: () =>
            {
                if (_pendingResponses.TryDequeue(out var item))
                {
                    Push(_stage._outlet, item);
                    TryComplete();
                }
                else if (_stopping && _inFlightCount == 0)
                {
                    CompleteStage();
                }
                else if (!HasBeenPulled(_stage._inlet) && !_stopping)
                {
                    Pull(_stage._inlet);
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
            _onResponseCallback = GetAsyncCallback<(string, IMemoryOwner<byte>, int)>(OnSubGraphResponse);
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
                    if (!_hostPools.ContainsKey(elem.PoolKey))
                    {
                        FailStage(new InvalidOperationException(
                            $"Received DataItem for unknown pool key '{elem.PoolKey}'. " +
                            $"A ConnectItem must be sent before any DataItem for a given pool key."));
                        return;
                    }

                    RouteDataItem(elem.PoolKey, data);
                    if (!_stopping)
                    {
                        Pull(_stage._inlet);
                    }
                    break;

                default:
                    FailStage(new InvalidOperationException(
                        $"Unexpected transport item type: {elem.Item?.GetType().Name ?? "null"}"));
                    break;
            }
        }

        private void RegisterHost(string poolKey, ConnectItem connect)
        {
            if (!_hostPools.ContainsKey(poolKey))
            {
                _hostPools[poolKey] = new HostPool(connect.Options);
            }
        }

        private void RouteDataItem(string poolKey, DataItem data)
        {
            var pool = _hostPools[poolKey];
            var slot = FindOrCreateSlot(poolKey, pool);

            if (slot is not null)
            {
                SendToSlot(poolKey, slot, data);
            }
            else
            {
                // All connections busy and at max — queue internally (backpressure)
                pool.PendingDataItems.Enqueue(data);
            }
        }

        private void SendToSlot(string poolKey, ConnectionSlot slot, DataItem data)
        {
            _inFlightCount++;
            slot.PendingRequestCount++;
            slot.Idle = false;
            slot.Queue.OfferAsync(data);
        }

        /// <summary>
        /// Finds an idle connection, or creates a new one if under the limit.
        /// Returns null when all connections are busy and the max has been reached.
        /// </summary>
        private ConnectionSlot? FindOrCreateSlot(string poolKey, HostPool pool)
        {
            // 1. Prefer an idle (non-busy) active connection
            foreach (var conn in pool.Connections)
            {
                if (conn.Active && conn.Idle)
                {
                    return conn;
                }
            }

            // 2. If under the limit, materialise a new connection
            if (pool.Connections.Count < _stage._config.MaxConnectionsPerHost)
            {
                return MaterialiseNewSlot(poolKey, pool);
            }

            // 3. All connections busy and at max — return null to signal queuing
            return null;
        }

        private ConnectionSlot MaterialiseNewSlot(string poolKey, HostPool pool)
        {
            var connectionFlow = _stage._connectionFlowFactory();
            var capturedPoolKey = poolKey;
            var capturedCallback = _onResponseCallback!;

            var (queue, completion) = Source.Queue<ITransportItem>(16, OverflowStrategy.Backpressure)
                .Via(connectionFlow)
                .ToMaterialized(
                    Sink.ForEach<(IMemoryOwner<byte>, int)>(chunk =>
                    {
                        capturedCallback((capturedPoolKey, chunk.Item1, chunk.Item2));
                    }),
                    Keep.Both)
                .Run(Materializer);

            var slot = new ConnectionSlot(queue, completion);
            pool.Connections.Add(slot);
            pool.ConnectionCounter++;

            // Send ConnectItem to initialise the connection in the sub-graph
            slot.Queue.OfferAsync(new ConnectItem(pool.Options));

            return slot;
        }

        private void OnSubGraphResponse((string poolKey, IMemoryOwner<byte> memory, int length) response)
        {
            if (_stopping && _pendingResponses.Count == 0 && _inFlightCount <= 1)
            {
                // Stage is shutting down — still deliver this last response
            }

            _inFlightCount--;

            // Update slot status and drain per-host queue
            if (_hostPools.TryGetValue(response.poolKey, out var pool))
            {
                foreach (var slot in pool.Connections)
                {
                    if (slot.PendingRequestCount > 0)
                    {
                        slot.PendingRequestCount--;
                        if (slot.PendingRequestCount == 0)
                        {
                            slot.Idle = true;
                        }
                        break;
                    }
                }

                // Drain queued items: dispatch to any now-idle slot
                DrainPendingQueue(response.poolKey, pool);
            }

            var item = new RoutedDataItem(response.poolKey, response.memory, response.length);

            if (IsAvailable(_stage._outlet))
            {
                Push(_stage._outlet, item);
                TryComplete();
            }
            else
            {
                _pendingResponses.Enqueue(item);
            }
        }

        /// <summary>
        /// Dispatches queued DataItems to idle connections for the given host.
        /// </summary>
        private void DrainPendingQueue(string poolKey, HostPool pool)
        {
            while (pool.PendingDataItems.Count > 0)
            {
                var slot = FindOrCreateSlot(poolKey, pool);
                if (slot is null)
                {
                    break; // Still all busy
                }

                var queued = pool.PendingDataItems.Dequeue();
                SendToSlot(poolKey, slot, queued);
            }
        }

        private void TryComplete()
        {
            if (_stopping && _inFlightCount == 0 && _pendingResponses.Count == 0)
            {
                CompleteStage();
            }
        }

        private void OnMessage((IActorRef sender, object msg) args)
        {
            // Future use for actor-based sub-graph communication
        }

        public override void PostStop()
        {
            _stopping = true;

            foreach (var pool in _hostPools.Values)
            {
                foreach (var slot in pool.Connections)
                {
                    slot.Queue.Complete();
                    slot.Active = false;
                }
            }

            _hostPools.Clear();
        }
    }

    /// <summary>
    /// Per-host connection pool. Tracks TCP options, active connections, and connection count.
    /// </summary>
    private sealed class HostPool
    {
        public TcpOptions Options { get; }
        public List<ConnectionSlot> Connections { get; } = new();
        public int ConnectionCounter { get; set; }

        /// <summary>
        /// DataItems waiting for a free connection slot. Drained when a response arrives
        /// and a slot becomes idle, or when a new slot is materialised.
        /// </summary>
        public Queue<DataItem> PendingDataItems { get; } = new();

        public HostPool(TcpOptions options)
        {
            Options = options;
        }
    }

    /// <summary>
    /// A single materialised connection within a host pool.
    /// Wraps the Source.Queue handle and tracks connection status.
    /// </summary>
    private sealed class ConnectionSlot
    {
        public ISourceQueueWithComplete<ITransportItem> Queue { get; }
        public Task Completion { get; }
        public bool Active { get; set; } = true;
        public bool Idle { get; set; } = true;
        public int PendingRequestCount { get; set; }

        public ConnectionSlot(ISourceQueueWithComplete<ITransportItem> queue, Task completion)
        {
            Queue = queue;
            Completion = completion;
        }
    }
}
