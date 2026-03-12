using System.Buffers;
using Akka.Actor;
using Akka.Streams;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using Akka.TestKit;
using TurboHttp.IO;
using TurboHttp.IO.Stages;

namespace TurboHttp.StreamTests.IO;

public sealed class ConnectionPoolStageTests : StreamTestBase
{
    // ── helpers ────────────────────────────────────────────────────────────────

    private static DataItem MakeDataItem(byte[] payload)
    {
        var owner = MemoryPool<byte>.Shared.Rent(payload.Length);
        payload.CopyTo(owner.Memory.Span);
        return new DataItem(owner, payload.Length);
    }

    private static RoutedTransportItem MakeRoutedData(string poolKey, byte[] payload)
    {
        return new RoutedTransportItem(poolKey, MakeDataItem(payload));
    }

    private static RoutedTransportItem MakeRoutedConnect(string poolKey)
    {
        return new RoutedTransportItem(
            poolKey,
            new ConnectItem(new TcpOptions { Host = "example.com", Port = 80 }));
    }

    /// <summary>
    /// Wires up the ConnectionPoolStage with manual publisher/subscriber probes
    /// for precise demand control. Returns the upstream subscription and downstream probe.
    /// </summary>
    private (TestPublisher.ManualProbe<RoutedTransportItem> upstream,
             TestSubscriber.ManualProbe<RoutedDataItem> downstream)
        MaterializeWithProbes(TestProbe router)
    {
        var stage = new ConnectionPoolStage(router);

        var upstream = this.CreateManualPublisherProbe<RoutedTransportItem>();
        var downstream = this.CreateManualSubscriberProbe<RoutedDataItem>();

        RunnableGraph.FromGraph(GraphDsl.Create(b =>
        {
            var flow = b.Add(Flow.FromGraph(stage));
            var src = b.Add(Source.FromPublisher(upstream));
            var snk = b.Add(Sink.FromSubscriber(downstream));

            b.From(src).Via(flow).To(snk);
            return ClosedShape.Instance;
        })).Run(Materializer);

        return (upstream, downstream);
    }

    // ── tests ─────────────────────────────────────────────────────────────────

    [Fact(Timeout = 10_000, DisplayName = "POOL-STAGE-001: DataItem is forwarded as SendRequest to the router")]
    public async Task DataItem_Forwarded_As_SendRequest()
    {
        var router = CreateTestProbe();
        var (upstream, downstream) = MaterializeWithProbes(router);

        var upSub = await upstream.ExpectSubscriptionAsync(CancellationToken.None);
        var downSub = await downstream.ExpectSubscriptionAsync(CancellationToken.None);

        downSub.Request(1);

        var item = MakeRoutedData("host-a", [0x01, 0x02]);
        upSub.SendNext(item);

        var msg = router.ExpectMsg<PoolRouterActor.SendRequest>();
        Assert.Equal("host-a", msg.PoolKey);
        Assert.Same(((DataItem)item.Item).Memory, msg.Data.Memory);
        Assert.Equal(2, msg.Data.Length);
    }

    [Fact(Timeout = 10_000, DisplayName = "POOL-STAGE-002: StageActor ref is used as ReplyTo in SendRequest")]
    public async Task StageActor_Ref_Used_As_ReplyTo()
    {
        var router = CreateTestProbe();
        var (upstream, downstream) = MaterializeWithProbes(router);

        var upSub = await upstream.ExpectSubscriptionAsync(CancellationToken.None);
        var downSub = await downstream.ExpectSubscriptionAsync(CancellationToken.None);

        downSub.Request(1);
        upSub.SendNext(MakeRoutedData("host-b", [0xAA]));

        var msg = router.ExpectMsg<PoolRouterActor.SendRequest>();

        // ReplyTo must be a valid actor ref (the StageActor), not the router itself
        Assert.NotNull(msg.ReplyTo);
        Assert.NotEqual(router.Ref, msg.ReplyTo);

        // Verify the ReplyTo is functional: send a response back through it
        var responseMemory = MemoryPool<byte>.Shared.Rent(3);
        new byte[] { 0xBB, 0xCC, 0xDD }.CopyTo(responseMemory.Memory.Span);
        msg.ReplyTo.Tell(new PoolRouterActor.Response("host-b", responseMemory, 3));

        var response = await downstream.ExpectNextAsync(CancellationToken.None);
        Assert.Equal("host-b", response.PoolKey);
        Assert.Equal(3, response.Length);
    }

    [Fact(Timeout = 10_000, DisplayName = "POOL-STAGE-003: After push, inlet pulls immediately (demand signaling)")]
    public async Task After_Push_Inlet_Pulls_Immediately()
    {
        var router = CreateTestProbe();
        var (upstream, downstream) = MaterializeWithProbes(router);

        var upSub = await upstream.ExpectSubscriptionAsync(CancellationToken.None);
        var downSub = await downstream.ExpectSubscriptionAsync(CancellationToken.None);

        downSub.Request(10);

        // Send first item — stage should immediately pull for another
        upSub.SendNext(MakeRoutedData("host-c", [0x01]));
        router.ExpectMsg<PoolRouterActor.SendRequest>();

        // The stage calls Pull(_inlet) right after processing the push,
        // so upstream should be able to send a second item without blocking
        upSub.SendNext(MakeRoutedData("host-c", [0x02]));
        var msg2 = router.ExpectMsg<PoolRouterActor.SendRequest>();
        Assert.Equal(1, msg2.Data.Length);

        // And a third — proving continuous demand
        upSub.SendNext(MakeRoutedData("host-c", [0x03]));
        var msg3 = router.ExpectMsg<PoolRouterActor.SendRequest>();
        Assert.Equal(1, msg3.Data.Length);
    }

    // ── TASK-018: Response Flow tests ─────────────────────────────────────────

    /// <summary>
    /// Obtains the StageActor ref by sending a request and capturing ReplyTo from the SendRequest.
    /// </summary>
    private async Task<IActorRef> GetStageActorRef(
        TestProbe router,
        TestPublisher.ManualProbe<RoutedTransportItem> upstream,
        TestSubscriber.ManualProbe<RoutedDataItem> downstream)
    {
        var upSub = await upstream.ExpectSubscriptionAsync(CancellationToken.None);
        var downSub = await downstream.ExpectSubscriptionAsync(CancellationToken.None);

        downSub.Request(100);
        upSub.SendNext(MakeRoutedData("probe-host", [0x00]));

        var msg = router.ExpectMsg<PoolRouterActor.SendRequest>();
        return msg.ReplyTo;
    }

    [Fact(Timeout = 10_000, DisplayName = "POOL-STAGE-005: Response via StageActor is converted to RoutedDataItem and pushed to outlet")]
    public async Task Response_Converted_To_RoutedDataItem_And_Pushed()
    {
        var router = CreateTestProbe();
        var (upstream, downstream) = MaterializeWithProbes(router);
        var stageActor = await GetStageActorRef(router, upstream, downstream);

        var mem = MemoryPool<byte>.Shared.Rent(4);
        new byte[] { 0xDE, 0xAD, 0xBE, 0xEF }.CopyTo(mem.Memory.Span);
        stageActor.Tell(new PoolRouterActor.Response("host-x", mem, 4));

        var response = await downstream.ExpectNextAsync(CancellationToken.None);
        Assert.Equal("host-x", response.PoolKey);
        Assert.Equal(4, response.Length);
        Assert.Equal(0xDE, response.Memory.Memory.Span[0]);
        Assert.Equal(0xEF, response.Memory.Memory.Span[3]);
    }

    [Fact(Timeout = 10_000, DisplayName = "POOL-STAGE-006: Response buffered when outlet has no demand")]
    public async Task Response_Buffered_When_No_Demand()
    {
        var router = CreateTestProbe();
        var (upstream, downstream) = MaterializeWithProbes(router);

        var upSub = await upstream.ExpectSubscriptionAsync(CancellationToken.None);
        var downSub = await downstream.ExpectSubscriptionAsync(CancellationToken.None);

        // Do NOT request any downstream elements — outlet has zero demand.
        // The inlet still works because PreStart calls Pull(_inlet) unconditionally.
        upSub.SendNext(MakeRoutedData("probe-host", [0x00]));
        var msg = router.ExpectMsg<PoolRouterActor.SendRequest>();
        var stageActor = msg.ReplyTo;

        // Send a response while downstream has no demand — should be buffered
        var mem = MemoryPool<byte>.Shared.Rent(2);
        new byte[] { 0xAA, 0xBB }.CopyTo(mem.Memory.Span);
        stageActor.Tell(new PoolRouterActor.Response("host-buffered", mem, 2));

        // No element should arrive yet (no demand)
        await downstream.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(300), CancellationToken.None);

        // Now signal demand — the buffered response should arrive
        downSub.Request(1);
        var response = await downstream.ExpectNextAsync(CancellationToken.None);
        Assert.Equal("host-buffered", response.PoolKey);
        Assert.Equal(2, response.Length);
    }

    [Fact(Timeout = 10_000, DisplayName = "POOL-STAGE-007: Multiple responses pushed in order when demand arrives")]
    public async Task Multiple_Responses_Pushed_In_Order()
    {
        var router = CreateTestProbe();
        var (upstream, downstream) = MaterializeWithProbes(router);
        var stageActor = await GetStageActorRef(router, upstream, downstream);

        // Send 3 responses — demand is already signaled (100 from helper)
        for (var i = 0; i < 3; i++)
        {
            var mem = MemoryPool<byte>.Shared.Rent(1);
            mem.Memory.Span[0] = (byte)(i + 1);
            stageActor.Tell(new PoolRouterActor.Response($"host-{i}", mem, 1));
        }

        var r1 = await downstream.ExpectNextAsync(CancellationToken.None);
        var r2 = await downstream.ExpectNextAsync(CancellationToken.None);
        var r3 = await downstream.ExpectNextAsync(CancellationToken.None);

        // Verify FIFO order
        Assert.Equal("host-0", r1.PoolKey);
        Assert.Equal(1, r1.Memory.Memory.Span[0]);

        Assert.Equal("host-1", r2.PoolKey);
        Assert.Equal(2, r2.Memory.Memory.Span[0]);

        Assert.Equal("host-2", r3.PoolKey);
        Assert.Equal(3, r3.Memory.Memory.Span[0]);
    }

    [Fact(Timeout = 10_000, DisplayName = "POOL-STAGE-008: Response PoolKey correlates with request PoolKey")]
    public async Task Response_PoolKey_Matches_Request_PoolKey()
    {
        var router = CreateTestProbe();
        var (upstream, downstream) = MaterializeWithProbes(router);

        var upSub = await upstream.ExpectSubscriptionAsync(CancellationToken.None);
        var downSub = await downstream.ExpectSubscriptionAsync(CancellationToken.None);

        downSub.Request(10);

        // Send requests for two different hosts
        upSub.SendNext(MakeRoutedData("alpha.example.com:443", [0x01]));
        var req1 = router.ExpectMsg<PoolRouterActor.SendRequest>();
        Assert.Equal("alpha.example.com:443", req1.PoolKey);

        upSub.SendNext(MakeRoutedData("beta.example.com:80", [0x02]));
        var req2 = router.ExpectMsg<PoolRouterActor.SendRequest>();
        Assert.Equal("beta.example.com:80", req2.PoolKey);

        // Both requests go through the same StageActor
        var stageActor = req1.ReplyTo;
        Assert.Equal(stageActor, req2.ReplyTo);

        // Send responses in reverse order — PoolKey on each response determines correlation
        var mem2 = MemoryPool<byte>.Shared.Rent(1);
        mem2.Memory.Span[0] = 0xBB;
        stageActor.Tell(new PoolRouterActor.Response("beta.example.com:80", mem2, 1));

        var mem1 = MemoryPool<byte>.Shared.Rent(1);
        mem1.Memory.Span[0] = 0xAA;
        stageActor.Tell(new PoolRouterActor.Response("alpha.example.com:443", mem1, 1));

        // Responses arrive in the order they were sent to the StageActor (FIFO)
        var resp1 = await downstream.ExpectNextAsync(CancellationToken.None);
        Assert.Equal("beta.example.com:80", resp1.PoolKey);
        Assert.Equal(0xBB, resp1.Memory.Memory.Span[0]);

        var resp2 = await downstream.ExpectNextAsync(CancellationToken.None);
        Assert.Equal("alpha.example.com:443", resp2.PoolKey);
        Assert.Equal(0xAA, resp2.Memory.Memory.Span[0]);
    }

    // ── TASK-017: Request Flow tests (continued) ───────────────────────────────

    [Fact(Timeout = 10_000, DisplayName = "POOL-STAGE-004: Non-DataItem (ConnectItem) does not crash, inlet pulls again")]
    public async Task NonDataItem_Handled_Gracefully()
    {
        var router = CreateTestProbe();
        var (upstream, downstream) = MaterializeWithProbes(router);

        var upSub = await upstream.ExpectSubscriptionAsync(CancellationToken.None);
        var downSub = await downstream.ExpectSubscriptionAsync(CancellationToken.None);

        downSub.Request(10);

        // Send a ConnectItem (non-DataItem) — should NOT crash
        upSub.SendNext(MakeRoutedConnect("host-d"));

        // Router should NOT have received a SendRequest for ConnectItem
        router.ExpectNoMsg(TimeSpan.FromMilliseconds(200));

        // Stage should still be alive and pulling — send a DataItem afterward
        upSub.SendNext(MakeRoutedData("host-d", [0xFF]));
        var msg = router.ExpectMsg<PoolRouterActor.SendRequest>();
        Assert.Equal("host-d", msg.PoolKey);
        Assert.Equal(1, msg.Data.Length);
    }
}
