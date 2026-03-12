using System.Buffers;
using System.Collections.Concurrent;
using System.Threading;
using Akka;
using Akka.Streams;
using Akka.Streams.Dsl;
using TurboHttp.IO;
using TurboHttp.IO.Stages;

namespace TurboHttp.StreamTests.Stages;

/// <summary>
/// Tests for <see cref="ConnectionPoolStage"/>.
/// TASK-001: Foundation — ConnectItem registration, unknown PoolKey rejection, empty pass-through.
/// TASK-002: Per-host connection lifecycle — sub-graph materialisation, single-host roundtrip.
/// TASK-003: Multi-connection per host — dynamic scaling, backpressure queuing.
/// </summary>
public sealed class ConnectionPoolStageTests : StreamTestBase
{
    private static readonly new PoolConfig DefaultConfig = new();

    /// <summary>
    /// Creates an echo flow that filters out ConnectItems and echoes DataItems back.
    /// Used as a fake ConnectionStage for unit tests.
    /// </summary>
    private static IGraph<FlowShape<ITransportItem, (IMemoryOwner<byte>, int)>, NotUsed> CreateEchoFlow()
    {
        return Flow.Create<ITransportItem>()
            .Where(x => x is DataItem)
            .Select(x =>
            {
                var data = (DataItem)x;
                return (data.Memory, data.Length);
            });
    }

    /// <summary>
    /// Creates an echo flow factory that counts how many times it has been invoked.
    /// Each invocation represents a new ConnectionStage materialisation.
    /// </summary>
    private static (Func<IGraph<FlowShape<ITransportItem, (IMemoryOwner<byte>, int)>, NotUsed>> factory, Func<int> getCount)
        CreateCountingEchoFlowFactory()
    {
        var count = 0;
        IGraph<FlowShape<ITransportItem, (IMemoryOwner<byte>, int)>, NotUsed> Factory()
        {
            Interlocked.Increment(ref count);
            return CreateEchoFlow();
        }
        return (Factory, () => Volatile.Read(ref count));
    }

    /// <summary>
    /// Creates a gated flow factory. Each materialised flow holds DataItems until the
    /// corresponding gate is released. This simulates busy connections for testing backpressure.
    /// </summary>
    private static (
        Func<IGraph<FlowShape<ITransportItem, (IMemoryOwner<byte>, int)>, NotUsed>> factory,
        ConcurrentQueue<TaskCompletionSource<bool>> gates,
        Func<int> getCount)
        CreateGatedFlowFactory()
    {
        var gates = new ConcurrentQueue<TaskCompletionSource<bool>>();
        var count = 0;

        IGraph<FlowShape<ITransportItem, (IMemoryOwner<byte>, int)>, NotUsed> Factory()
        {
            Interlocked.Increment(ref count);
            return Flow.Create<ITransportItem>()
                .Where(x => x is DataItem)
                .SelectAsync(1, async x =>
                {
                    var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                    gates.Enqueue(gate);
                    await gate.Task;
                    var data = (DataItem)x;
                    return (data.Memory, data.Length);
                });
        }
        return (Factory, gates, () => Volatile.Read(ref count));
    }

    /// <summary>
    /// Creates a ConnectionPoolStage with an echo flow factory.
    /// The echo flow passes DataItems through and filters ConnectItems.
    /// </summary>
    private static ConnectionPoolStage CreateStage()
    {
        return new ConnectionPoolStage(CreateEchoFlow, DefaultConfig);
    }

    private static ConnectionPoolStage CreateStage(
        Func<IGraph<FlowShape<ITransportItem, (IMemoryOwner<byte>, int)>, NotUsed>> factory,
        PoolConfig? config = null)
    {
        return new ConnectionPoolStage(factory, config ?? DefaultConfig);
    }

    private static TcpOptions TestOptions(string host = "example.com", int port = 443)
        => new() { Host = host, Port = port };

    // ── POOL-001: ConnectItem registers host ──────────────────────────────────

    [Fact(Timeout = 10_000,
        DisplayName = "POOL-001: ConnectItem on inlet registers host without error")]
    public async Task POOL_001_ConnectItem_RegistersHost_CompletesCleanly()
    {
        // Sending a ConnectItem followed by stream completion should not fail.
        // The ConnectItem registers the pool key in internal state.
        var items = new List<RoutedTransportItem>
        {
            new("https:example.com:443:1.1", new ConnectItem(TestOptions()))
        };

        var results = await Source.From(items)
            .Via(Flow.FromGraph(CreateStage()))
            .RunWith(Sink.Seq<RoutedDataItem>(), Materializer);

        // No DataItems sent → no responses expected
        Assert.Empty(results);
    }

    // ── POOL-002: Multiple ConnectItems for different hosts ───────────────────

    [Fact(Timeout = 10_000,
        DisplayName = "POOL-002: Multiple ConnectItems for different hosts register without error")]
    public async Task POOL_002_MultipleConnectItems_DifferentHosts_CompletesCleanly()
    {
        var items = new List<RoutedTransportItem>
        {
            new("https:example.com:443:1.1", new ConnectItem(TestOptions("example.com", 443))),
            new("https:other.com:443:1.1", new ConnectItem(TestOptions("other.com", 443))),
            new("http:third.com:80:1.1", new ConnectItem(TestOptions("third.com", 80)))
        };

        var results = await Source.From(items)
            .Via(Flow.FromGraph(CreateStage()))
            .RunWith(Sink.Seq<RoutedDataItem>(), Materializer);

        Assert.Empty(results);
    }

    // ── POOL-003: Duplicate ConnectItem for same host is idempotent ───────────

    [Fact(Timeout = 10_000,
        DisplayName = "POOL-003: Duplicate ConnectItem for same pool key is idempotent")]
    public async Task POOL_003_DuplicateConnectItem_SameKey_Idempotent()
    {
        const string poolKey = "https:example.com:443:1.1";
        var items = new List<RoutedTransportItem>
        {
            new(poolKey, new ConnectItem(TestOptions())),
            new(poolKey, new ConnectItem(TestOptions()))
        };

        var results = await Source.From(items)
            .Via(Flow.FromGraph(CreateStage()))
            .RunWith(Sink.Seq<RoutedDataItem>(), Materializer);

        Assert.Empty(results);
    }

    // ── POOL-004: DataItem without prior ConnectItem → stage failure ──────────

    [Fact(Timeout = 10_000,
        DisplayName = "POOL-004: DataItem without prior ConnectItem fails stage with descriptive error")]
    public async Task POOL_004_DataItem_WithoutConnectItem_FailsStage()
    {
        var data = new SimpleMemoryOwner(new byte[] { 0x01, 0x02 });
        var items = new List<RoutedTransportItem>
        {
            new("https:unknown.com:443:1.1", new DataItem(data, 2))
        };

        var ex = await Assert.ThrowsAnyAsync<Exception>(async () =>
            await Source.From(items)
                .Via(Flow.FromGraph(CreateStage()))
                .RunWith(Sink.Seq<RoutedDataItem>(), Materializer));

        var inner = ex is AggregateException agg ? agg.InnerException! : ex;
        Assert.IsType<InvalidOperationException>(inner);
        Assert.Contains("unknown pool key", inner.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("unknown.com", inner.Message);
    }

    // ── POOL-005: DataItem after ConnectItem for different key → stage failure ─

    [Fact(Timeout = 10_000,
        DisplayName = "POOL-005: DataItem for unregistered key (other key registered) fails stage")]
    public async Task POOL_005_DataItem_ForWrongKey_FailsStage()
    {
        var data = new SimpleMemoryOwner(new byte[] { 0x01 });
        var items = new List<RoutedTransportItem>
        {
            // Register host-a
            new("https:host-a.com:443:1.1", new ConnectItem(TestOptions("host-a.com", 443))),
            // Send data for host-b (not registered)
            new("https:host-b.com:443:1.1", new DataItem(data, 1))
        };

        var ex = await Assert.ThrowsAnyAsync<Exception>(async () =>
            await Source.From(items)
                .Via(Flow.FromGraph(CreateStage()))
                .RunWith(Sink.Seq<RoutedDataItem>(), Materializer));

        var inner = ex is AggregateException agg ? agg.InnerException! : ex;
        Assert.IsType<InvalidOperationException>(inner);
        Assert.Contains("host-b.com", inner.Message);
    }

    // ── POOL-006: Empty source completes without error ────────────────────────

    [Fact(Timeout = 10_000,
        DisplayName = "POOL-006: Empty source (no items) completes without error")]
    public async Task POOL_006_EmptySource_CompletesCleanly()
    {
        var results = await Source.Empty<RoutedTransportItem>()
            .Via(Flow.FromGraph(CreateStage()))
            .RunWith(Sink.Seq<RoutedDataItem>(), Materializer);

        Assert.Empty(results);
    }

    // ── POOL-007: ConnectItem then DataItem for registered key produces response ──

    [Fact(Timeout = 10_000,
        DisplayName = "POOL-007: DataItem after ConnectItem for same key produces response")]
    public async Task POOL_007_DataItem_AfterConnectItem_SameKey_ProducesResponse()
    {
        const string poolKey = "https:example.com:443:1.1";
        var payload = new byte[] { 0x48, 0x49 };
        var data = new SimpleMemoryOwner(payload);
        var items = new List<RoutedTransportItem>
        {
            new(poolKey, new ConnectItem(TestOptions())),
            new(poolKey, new DataItem(data, 2))
        };

        var results = await Source.From(items)
            .Via(Flow.FromGraph(CreateStage()))
            .RunWith(Sink.Seq<RoutedDataItem>(), Materializer);

        // Sub-graph echo flow returns the DataItem as a RoutedDataItem
        Assert.Single(results);
        Assert.Equal(poolKey, results[0].PoolKey);
        Assert.Equal(2, results[0].Length);
    }

    // ── POOL-008: Stage shape has correct inlet/outlet names ──────────────────

    [Fact(DisplayName = "POOL-008: Stage shape exposes named inlet and outlet")]
    public void POOL_008_StageShape_HasCorrectInletOutlet()
    {
        var stage = CreateStage();

        Assert.NotNull(stage.Shape.Inlet);
        Assert.NotNull(stage.Shape.Outlet);
        Assert.Equal("pool.in", stage.Shape.Inlet.Name);
        Assert.Equal("pool.out", stage.Shape.Outlet.Name);
    }

    // ── POOL-009: Single-host single-connection roundtrip ─────────────────────

    [Fact(Timeout = 10_000,
        DisplayName = "POOL-009: Single-host single-connection roundtrip with echo flow")]
    public async Task POOL_009_SingleHost_SingleConnection_Roundtrip()
    {
        const string poolKey = "https:example.com:443:1.1";
        var payload = new byte[] { 0x48, 0x45, 0x4C, 0x4C, 0x4F }; // HELLO
        var data = new SimpleMemoryOwner(payload);
        var items = new List<RoutedTransportItem>
        {
            new(poolKey, new ConnectItem(TestOptions())),
            new(poolKey, new DataItem(data, payload.Length))
        };

        var results = await Source.From(items)
            .Via(Flow.FromGraph(CreateStage()))
            .RunWith(Sink.Seq<RoutedDataItem>(), Materializer);

        Assert.Single(results);
        Assert.Equal(poolKey, results[0].PoolKey);
        Assert.Equal(payload.Length, results[0].Length);
        Assert.Equal(payload, results[0].Memory.Memory.Slice(0, results[0].Length).ToArray());
    }

    // ── POOL-010: Multiple DataItems for same host reuse connection slot ──────

    [Fact(Timeout = 10_000,
        DisplayName = "POOL-010: Multiple DataItems for same host reuse the same connection slot")]
    public async Task POOL_010_MultipleDataItems_SameHost_ReuseSlot()
    {
        const string poolKey = "https:example.com:443:1.1";
        var payload1 = new byte[] { 0x01, 0x02 };
        var payload2 = new byte[] { 0x03, 0x04, 0x05 };
        var items = new List<RoutedTransportItem>
        {
            new(poolKey, new ConnectItem(TestOptions())),
            new(poolKey, new DataItem(new SimpleMemoryOwner(payload1), payload1.Length)),
            new(poolKey, new DataItem(new SimpleMemoryOwner(payload2), payload2.Length))
        };

        var results = await Source.From(items)
            .Via(Flow.FromGraph(CreateStage()))
            .RunWith(Sink.Seq<RoutedDataItem>(), Materializer);

        Assert.Equal(2, results.Count);
        Assert.All(results, r => Assert.Equal(poolKey, r.PoolKey));
        Assert.Equal(payload1.Length, results[0].Length);
        Assert.Equal(payload2.Length, results[1].Length);
    }

    // ── TASK-003: Multi-Connection per Host — Dynamic Scaling ──────────────────

    // ── POOL-011: 3 parallel requests → 3 ConnectionStage instances (MaxConnections=3) ──

    [Fact(Timeout = 10_000,
        DisplayName = "POOL-011: 3 parallel requests create 3 connections (HTTP/1.x, MaxConnections=3)")]
    public async Task POOL_011_ThreeParallelRequests_ThreeConnections()
    {
        // Use gated flow: each request blocks until we release the gate.
        // This ensures all 3 connections are created because each prior one is "busy".
        var (factory, gates, getCount) = CreateGatedFlowFactory();
        var config = new PoolConfig(MaxConnectionsPerHost: 3);
        var stage = CreateStage(factory, config);
        const string poolKey = "https:example.com:443:1.1";

        var items = new List<RoutedTransportItem>
        {
            new(poolKey, new ConnectItem(TestOptions())),
            new(poolKey, new DataItem(new SimpleMemoryOwner(new byte[] { 0x01 }), 1)),
            new(poolKey, new DataItem(new SimpleMemoryOwner(new byte[] { 0x02 }), 1)),
            new(poolKey, new DataItem(new SimpleMemoryOwner(new byte[] { 0x03 }), 1))
        };

        var resultTask = Source.From(items)
            .Via(Flow.FromGraph(stage))
            .RunWith(Sink.Seq<RoutedDataItem>(), Materializer);

        // Wait for all 3 gates to appear (meaning 3 connections were created)
        await WaitForConditionAsync(() => gates.Count >= 3, timeout: TimeSpan.FromSeconds(5));
        Assert.Equal(3, getCount());

        // Release all gates so the stream can complete
        while (gates.TryDequeue(out var gate))
        {
            gate.SetResult(true);
        }

        var results = await resultTask;
        Assert.Equal(3, results.Count);
    }

    // ── POOL-012: 4 requests with MaxConnections=2 → 2 connections, 2 queued ──

    [Fact(Timeout = 10_000,
        DisplayName = "POOL-012: 4 requests with MaxConnections=2 → 2 connections, 2 queued then drained")]
    public async Task POOL_012_FourRequests_MaxTwo_TwoQueuedThenDrained()
    {
        var (factory, gates, getCount) = CreateGatedFlowFactory();
        var config = new PoolConfig(MaxConnectionsPerHost: 2);
        var stage = CreateStage(factory, config);
        const string poolKey = "https:example.com:443:1.1";

        var items = new List<RoutedTransportItem>
        {
            new(poolKey, new ConnectItem(TestOptions())),
            new(poolKey, new DataItem(new SimpleMemoryOwner(new byte[] { 0x01 }), 1)),
            new(poolKey, new DataItem(new SimpleMemoryOwner(new byte[] { 0x02 }), 1)),
            new(poolKey, new DataItem(new SimpleMemoryOwner(new byte[] { 0x03 }), 1)),
            new(poolKey, new DataItem(new SimpleMemoryOwner(new byte[] { 0x04 }), 1))
        };

        var resultTask = Source.From(items)
            .Via(Flow.FromGraph(stage))
            .RunWith(Sink.Seq<RoutedDataItem>(), Materializer);

        // Wait for 2 gates (max connections = 2). Items 3 and 4 are queued internally.
        await WaitForConditionAsync(() => gates.Count >= 2, timeout: TimeSpan.FromSeconds(5));
        Assert.Equal(2, getCount());

        // Give a moment for all items to be consumed by the inlet
        await Task.Delay(200);

        // Only 2 connections should exist (not 3 or 4)
        Assert.Equal(2, getCount());

        // Release gates one at a time — each release frees a slot, the pool drains queued items
        // to that slot, which creates a new gate. Repeat until all 4 are done.
        var totalReleased = 0;
        while (totalReleased < 4)
        {
            await WaitForConditionAsync(() => gates.Count >= 1, timeout: TimeSpan.FromSeconds(5));
            Assert.True(gates.TryDequeue(out var gate));
            gate.SetResult(true);
            totalReleased++;
        }

        var results = await resultTask;
        Assert.Equal(4, results.Count);

        // Still only 2 connections were ever created
        Assert.Equal(2, getCount());
    }

    // ── POOL-013: MaxConnectionsPerHost=1 enforces single connection ──────────

    [Fact(Timeout = 10_000,
        DisplayName = "POOL-013: MaxConnectionsPerHost=1 enforces single connection with queuing")]
    public async Task POOL_013_MaxOne_SingleConnectionWithQueuing()
    {
        var (factory, gates, getCount) = CreateGatedFlowFactory();
        var config = new PoolConfig(MaxConnectionsPerHost: 1);
        var stage = CreateStage(factory, config);
        const string poolKey = "https:example.com:443:1.1";

        var items = new List<RoutedTransportItem>
        {
            new(poolKey, new ConnectItem(TestOptions())),
            new(poolKey, new DataItem(new SimpleMemoryOwner(new byte[] { 0x01 }), 1)),
            new(poolKey, new DataItem(new SimpleMemoryOwner(new byte[] { 0x02 }), 1))
        };

        var resultTask = Source.From(items)
            .Via(Flow.FromGraph(stage))
            .RunWith(Sink.Seq<RoutedDataItem>(), Materializer);

        // Wait for first gate
        await WaitForConditionAsync(() => gates.Count >= 1, timeout: TimeSpan.FromSeconds(5));
        Assert.Equal(1, getCount());

        // Release first gate — queued item gets dispatched
        Assert.True(gates.TryDequeue(out var firstGate));
        firstGate.SetResult(true);

        // Wait for second gate (the queued item)
        await WaitForConditionAsync(() => gates.Count >= 1, timeout: TimeSpan.FromSeconds(5));

        // Release second gate
        Assert.True(gates.TryDequeue(out var secondGate));
        secondGate.SetResult(true);

        var results = await resultTask;
        Assert.Equal(2, results.Count);
        Assert.Equal(1, getCount()); // Only 1 connection ever created
    }

    // ── POOL-014: New connection uses same TcpOptions from ConnectItem ─────────

    [Fact(Timeout = 10_000,
        DisplayName = "POOL-014: Each new ConnectionStage receives a ConnectItem with original TcpOptions")]
    public async Task POOL_014_NewConnections_UseSameTcpOptions()
    {
        // Track ConnectItems received by each sub-graph to verify TcpOptions propagation
        var receivedConnectItems = new ConcurrentBag<ConnectItem>();

        IGraph<FlowShape<ITransportItem, (IMemoryOwner<byte>, int)>, NotUsed> Factory()
        {
            return Flow.Create<ITransportItem>()
                .Select(x =>
                {
                    if (x is ConnectItem ci)
                    {
                        receivedConnectItems.Add(ci);
                    }
                    return x;
                })
                .Where(x => x is DataItem)
                .Select(x =>
                {
                    var data = (DataItem)x;
                    return (data.Memory, data.Length);
                });
        }

        var config = new PoolConfig(MaxConnectionsPerHost: 3);
        var stage = CreateStage(Factory, config);
        const string poolKey = "https:example.com:443:1.1";
        var options = TestOptions("example.com", 8080);

        var items = new List<RoutedTransportItem>
        {
            new(poolKey, new ConnectItem(options)),
            new(poolKey, new DataItem(new SimpleMemoryOwner(new byte[] { 0x01 }), 1)),
            new(poolKey, new DataItem(new SimpleMemoryOwner(new byte[] { 0x02 }), 1)),
            new(poolKey, new DataItem(new SimpleMemoryOwner(new byte[] { 0x03 }), 1))
        };

        var results = await Source.From(items)
            .Via(Flow.FromGraph(stage))
            .RunWith(Sink.Seq<RoutedDataItem>(), Materializer);

        Assert.Equal(3, results.Count);

        // Each sub-graph should have received a ConnectItem with the same TcpOptions
        // (at least 1 connection; the echo flow responds instantly so some may reuse idle slots)
        Assert.NotEmpty(receivedConnectItems);
        Assert.All(receivedConnectItems, ci =>
        {
            Assert.Equal("example.com", ci.Options.Host);
            Assert.Equal(8080, ci.Options.Port);
        });
    }

    /// <summary>
    /// Polls a condition until it becomes true or the timeout expires.
    /// </summary>
    private static async Task WaitForConditionAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException($"Condition not met within {timeout}");
            }
            await Task.Delay(25);
        }
    }
}
