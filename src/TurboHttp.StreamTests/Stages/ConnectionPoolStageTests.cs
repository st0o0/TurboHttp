using System.Buffers;
using Akka.Actor;
using Akka.Streams.Dsl;
using TurboHttp.IO;
using TurboHttp.IO.Stages;

namespace TurboHttp.StreamTests.Stages;

/// <summary>
/// Foundation tests for <see cref="ConnectionPoolStage"/>.
/// Covers: ConnectItem registration, unknown PoolKey rejection, empty pass-through.
/// </summary>
public sealed class ConnectionPoolStageTests : StreamTestBase
{
    private static readonly new PoolConfig DefaultConfig = new();

    /// <summary>
    /// Creates a ConnectionPoolStage with a dummy ConnectionStage factory.
    /// The factory is not invoked in TASK-001 foundation tests — it exists only
    /// to satisfy the constructor signature.
    /// </summary>
    private static ConnectionPoolStage CreateStage()
    {
        // Factory creates a ConnectionStage with a Nobody actor ref.
        // Foundation tests never materialise sub-graphs, so this is safe.
        return new ConnectionPoolStage(
            () => new ConnectionStage(ActorRefs.Nobody),
            DefaultConfig);
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

    // ── POOL-007: ConnectItem then DataItem for registered key does not fail ──

    [Fact(Timeout = 10_000,
        DisplayName = "POOL-007: DataItem after ConnectItem for same key does not fail")]
    public async Task POOL_007_DataItem_AfterConnectItem_SameKey_DoesNotFail()
    {
        const string poolKey = "https:example.com:443:1.1";
        var data = new SimpleMemoryOwner(new byte[] { 0x48, 0x49 });
        var items = new List<RoutedTransportItem>
        {
            new(poolKey, new ConnectItem(TestOptions())),
            new(poolKey, new DataItem(data, 2))
        };

        // Should complete without exception — data is accepted (even though
        // no sub-graph is materialised yet in TASK-001 foundation).
        var results = await Source.From(items)
            .Via(Flow.FromGraph(CreateStage()))
            .RunWith(Sink.Seq<RoutedDataItem>(), Materializer);

        // No responses expected in foundation (no sub-graph wired)
        Assert.Empty(results);
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
}
