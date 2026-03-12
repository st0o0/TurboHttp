// using System;
// using System.Buffers;
// using System.Collections.Concurrent;
// using Akka;
// using Akka.Actor;
// using Akka.Streams;
// using Akka.Streams.Dsl;
// using Akka.Streams.Implementation;
// using Akka.Util.Internal;
//
// namespace TurboHttp.IO.Stages;
//
// public record RoutedTransportItem(string PoolKey, ITransportItem Item);
//
// public record RoutedDataItem(string PoolKey, IMemoryOwner<byte> Memory, int Length);
//
// public record PoolConfig(
//     int MaxConnectionsPerHost = 10,
//     TimeSpan IdleTimeout = default,
//     TimeSpan ConnectionTimeout = default)
// {
//     public TimeSpan IdleTimeout { get; init; } = IdleTimeout == TimeSpan.Zero ? TimeSpan.FromMinutes(5) : IdleTimeout;
//
//     public TimeSpan ConnectionTimeout { get; init; } =
//         ConnectionTimeout == TimeSpan.Zero ? TimeSpan.FromSeconds(30) : ConnectionTimeout;
// }
//
// public static class ConnectionPool
// {
//     public static Flow<RoutedTransportItem, RoutedDataItem, NotUsed> Create(
//         IActorRef clientManager,
//         int maxConcurrentConnections = 256,
//         PoolConfig? config = null)
//     {
//         config ??= new PoolConfig();
//
//         return Flow.FromGraph(GraphDsl.Create(builder =>
//         {
//             // 1. GroupBy Stage
//             var groupBy = builder.Add(Flow.Create<RoutedTransportItem>()
//                 .GroupBy(
//                     maxSubstreams: maxConcurrentConnections,
//                     groupingFunc: item => item.PoolKey,
//                     allowClosedSubstreamRecreation: true
//                 ));
//
//             // 2. Per-Pool Processing Flow
//             var perPoolFlow = builder.Add(CreatePerPoolConnectionFlow(clientManager));
//         
//             // 3. Merge Stage
//             var merge = builder.Add(Merge.Create(maxConcurrentConnections, true));
//
//             // 4. Verkettung
//             builder.From(groupBy.Outlet)
//                 .ToFanOut(perPoolFlow.In, 0)  // An alle Substreams
//                 .ToFanIn(merge.In, 0);        // Zurück zum Hauptflow
//
//             // 5. Shape definieren
//             return new FlowShape<RoutedTransportItem, RoutedDataItem>(
//                 groupBy.Inlet,    // Input
//                 merge.Outlet      // Output
//             );
//         }));
//
//     }
//
//     private static SubFlowImpl<RoutedTransportItem, RoutedDataItem, NotUsed, NotUsed>
//         ViaPerPool(this SubFlow<RoutedTransportItem, NotUsed, Sink<RoutedTransportItem, NotUsed>> subFlow,
//             IActorRef clientManager)
//     {
//         return new SubFlowImpl<RoutedTransportItem, RoutedDataItem, NotUsed, NotUsed>(
//             Flow.FromGraph(GraphDsl.Create(builder =>
//             {
//                 var poolKeyRef = new PoolKeyReference();
//
//                 var connectionStage = builder.Add(new ConnectionStage(clientManager));
//
//                 var unwrap = builder.Add(Flow.Create<RoutedTransportItem>()
//                     .Select(routed =>
//                     {
//                         if (poolKeyRef.PoolKey == null)
//                         {
//                             poolKeyRef.PoolKey = routed.PoolKey;
//                         }
//
//                         return routed.Item;
//                     }));
//
//                 var wrap = builder.Add(Flow.Create<(IMemoryOwner<byte>, int)>()
//                     .Select(tuple => new RoutedDataItem(
//                         poolKeyRef.PoolKey ?? "unknown",
//                         tuple.Item1,
//                         tuple.Item2
//                     )));
//
//                 builder.From(unwrap.Outlet)
//                     .Via(connectionStage)
//                     .To(wrap.Inlet);
//
//                 return new FlowShape<RoutedTransportItem, RoutedDataItem>(
//                     unwrap.Inlet,
//                     wrap.Outlet
//                 );
//             })),
//             // MergeBack Function (aus dem originalen SubFlow)
//             new MergeBackAdapter(subFlow),
//             // Finish Function
//             sink => subFlow.To(sink)
//         );
//     }
//
//     public static Flow<RoutedTransportItem, RoutedDataItem, NotUsed> CreateMultiplex(
//         IActorRef clientManager,
//         int maxConcurrentConnections = 256,
//         int maxRequestsPerConnection = 100,
//         PoolConfig? config = null)
//     {
//         config ??= new PoolConfig();
//
//         return Flow.Create<RoutedTransportItem>()
//             .GroupBy(maxConcurrentConnections, item => item.PoolKey, allowClosedSubstreamRecreation: true)
//             .ViaPerPoolMultiplex(clientManager, maxRequestsPerConnection)
//             .MergeSubstreams();
//     }
//
//     private static SubFlowImpl<RoutedTransportItem, RoutedDataItem, NotUsed, NotUsed>
//         ViaPerPoolMultiplex(this SubFlow<RoutedTransportItem, RoutedDataItem, NotUsed, NotUsed> subFlow,
//             IActorRef clientManager,
//             int maxConcurrentRequests)
//     {
//         var innerFlow = new SubFlowImpl<RoutedTransportItem, RoutedDataItem, NotUsed, NotUsed>(
//             Flow.FromGraph(GraphDsl.Create(builder =>
//             {
//                 var poolKeyRef = new PoolKeyReference();
//
//                 var connectionStage = builder.Add(new ConnectionStage(clientManager));
//
//                 var unwrap = builder.Add(Flow.Create<RoutedTransportItem>()
//                     .Select(routed =>
//                     {
//                         poolKeyRef.PoolKey ??= routed.PoolKey;
//                         return routed.Item;
//                     }));
//
//                 var wrap = builder.Add(Flow.Create<(IMemoryOwner<byte>, int)>()
//                     .Select(tuple => new RoutedDataItem(
//                         poolKeyRef.PoolKey ?? "unknown",
//                         tuple.Item1,
//                         tuple.Item2
//                     )));
//
//                 builder.From(unwrap.Outlet)
//                     .Via(connectionStage)
//                     .To(wrap.Inlet);
//
//                 return new FlowShape<RoutedTransportItem, RoutedDataItem>(unwrap.Inlet, wrap.Outlet);
//             })),
//             new MergeBackAdapter(subFlow),
//             sink => subFlow.To(sink)
//         );
//
//         return (SubFlowImpl<RoutedTransportItem, RoutedDataItem, NotUsed, NotUsed>)innerFlow
//             .SelectAsync(maxConcurrentRequests, System.Threading.Tasks.Task.FromResult);
//     }
//
//     public static Flow<RoutedTransportItem, RoutedDataItem, NotUsed> CreateManaged(
//         IActorRef clientManager,
//         int maxConcurrentConnections = 256,
//         PoolConfig? config = null)
//     {
//         config ??= new PoolConfig();
//         var manager = new PoolConnectionManager(config);
//
//         return Flow.Create<RoutedTransportItem, RoutedDataItem>()
//             .GroupBy(maxConcurrentConnections, item => item.PoolKey, allowClosedSubstreamRecreation: true)
//             .ViaPerPoolManaged(clientManager, manager)
//             .MergeSubstreams();
//     }
//
//     private static SubFlowImpl<RoutedTransportItem, RoutedDataItem, NotUsed, NotUsed>
//         ViaPerPoolManaged(this SubFlow<RoutedTransportItem, NotUsed, Sink<RoutedDataItem, NotUsed>> subFlow,
//             IActorRef clientManager,
//             PoolConnectionManager manager)
//     {
//         var poolKeyRef = new PoolKeyReference();
//         var connectionId = Guid.NewGuid();
//         return new SubFlowImpl<RoutedTransportItem, RoutedDataItem, NotUsed, NotUsed>(
//             Flow.FromGraph(GraphDsl.Create(builder =>
//             {
//                 var connectionStage = builder.Add(new ConnectionStage(clientManager));
//
//                 var unwrap = builder.Add(Flow.Create<RoutedTransportItem>()
//                     .Select(routed =>
//                     {
//                         if (poolKeyRef.PoolKey == null)
//                         {
//                             poolKeyRef.PoolKey = routed.PoolKey;
//                             manager.RegisterConnection(poolKeyRef.PoolKey, connectionId);
//                         }
//
//                         manager.UpdateActivity(poolKeyRef.PoolKey!, connectionId);
//                         return routed.Item;
//                     }));
//
//                 var wrap = builder.Add(Flow.Create<(IMemoryOwner<byte>, int)>()
//                     .Select(tuple =>
//                     {
//                         if (poolKeyRef.PoolKey != null)
//                         {
//                             manager.UpdateActivity(poolKeyRef.PoolKey, connectionId);
//                         }
//
//                         return new RoutedDataItem(poolKeyRef.PoolKey ?? "unknown", tuple.Item1, tuple.Item2);
//                     }));
//
//                 var cleanup = builder.Add(Flow.Create<RoutedDataItem>()
//                     .WatchTermination((mat, done) =>
//                     {
//                         done.ContinueWith(_ =>
//                         {
//                             if (poolKeyRef.PoolKey != null)
//                             {
//                                 manager.UnregisterConnection(poolKeyRef.PoolKey, connectionId);
//                             }
//                         });
//                         return mat;
//                     }));
//
//                 builder.From(unwrap.Outlet)
//                     .Via(connectionStage)
//                     .Via(wrap)
//                     .To(cleanup.Inlet);
//
//                 return new FlowShape<RoutedTransportItem, RoutedDataItem>(unwrap.Inlet, cleanup.Outlet);
//             })),
//             new MergeBackAdapter(subFlow),
//             sink => subFlow.To(sink)
//         );
//     }
//
//     private sealed class PoolKeyReference
//     {
//         public string? PoolKey { get; set; }
//     }
//
//     // ✅ Adapter für IMergeBack Interface
//     private sealed class MergeBackAdapter : IMergeBack<RoutedTransportItem, NotUsed>
//     {
//         private readonly SubFlow<RoutedTransportItem, RoutedDataItem, NotUsed, NotUsed> _subFlow;
//
//         public MergeBackAdapter(SubFlow<RoutedTransportItem, RoutedDataItem, NotUsed, NotUsed> subFlow)
//         {
//             _subFlow = subFlow;
//         }
//
//         public IFlow<TOut, NotUsed> Apply<TOut>(Flow<RoutedTransportItem, TOut, NotUsed> flow, int breadth)
//         {
//             // Delegiert an die originale SubFlow Merge-Logik
//             return _subFlow.MergeSubstreamsWithParallelism(breadth).AsInstanceOf<IFlow<TOut, NotUsed>>();
//         }
//     }
// }
//
// public sealed class PoolConnectionManager
// {
//     private readonly PoolConfig _config;
//     private readonly ConcurrentDictionary<string, PoolState> _pools = new();
//
//     public PoolConnectionManager(PoolConfig config)
//     {
//         _config = config;
//     }
//
//     public void RegisterConnection(string poolKey, Guid connectionId)
//     {
//         var pool = _pools.GetOrAdd(poolKey, key => new PoolState { PoolKey = key });
//         pool.AddConnection(connectionId);
//     }
//
//     public void UnregisterConnection(string poolKey, Guid connectionId)
//     {
//         if (!_pools.TryGetValue(poolKey, out var pool)) return;
//         pool.RemoveConnection(connectionId);
//         if (pool.ConnectionCount == 0)
//         {
//             _pools.TryRemove(poolKey, out _);
//         }
//     }
//
//     public void UpdateActivity(string poolKey, Guid connectionId)
//     {
//         if (_pools.TryGetValue(poolKey, out var pool))
//         {
//             pool.UpdateActivity(connectionId);
//         }
//     }
//
//     public int GetActiveConnectionCount(string poolKey)
//     {
//         return _pools.TryGetValue(poolKey, out var pool) ? pool.ConnectionCount : 0;
//     }
//
//     public int GetTotalPools() => _pools.Count;
//
//     private sealed class PoolState
//     {
//         public string PoolKey { get; init; } = "";
//         private readonly ConcurrentDictionary<Guid, ConnectionState> _connections = new();
//
//         public int ConnectionCount => _connections.Count;
//
//         public void AddConnection(Guid id)
//         {
//             _connections.TryAdd(id, new ConnectionState
//             {
//                 Id = id,
//                 Created = DateTime.UtcNow,
//                 LastActivity = DateTime.UtcNow
//             });
//         }
//
//         public void RemoveConnection(Guid id)
//         {
//             _connections.TryRemove(id, out _);
//         }
//
//         public void UpdateActivity(Guid id)
//         {
//             if (_connections.TryGetValue(id, out var conn))
//             {
//                 conn.LastActivity = DateTime.UtcNow;
//             }
//         }
//     }
//
//     private sealed class ConnectionState
//     {
//         public Guid Id { get; init; }
//         public DateTime Created { get; init; }
//         public DateTime LastActivity { get; set; }
//     }
// }