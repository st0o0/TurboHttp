using System.Buffers;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Event;
using Akka.TestKit.Xunit2;
using TurboHttp.IO;
using TurboHttp.IO.Stages;

namespace TurboHttp.Tests.IO;

public sealed class HostPoolActorTests : TestKit
{
    private static TcpOptions MakeOptions()
        => new() { Host = "test.local", Port = 80 };

    private static DataItem MakeDataItem()
    {
        var owner = MemoryPool<byte>.Shared.Rent(16);
        return new DataItem(owner, 16);
    }

    private IActorRef CreateHostPool(
        int maxConnections = 10,
        IActorRef? streamPublisher = null)
    {
        var options = MakeOptions();
        var config = new PoolConfig(
            MaxConnectionsPerHost: maxConnections,
            IdleCheckInterval: TimeSpan.FromHours(1));

        return Sys.ActorOf(Props.Create(() =>
            new HostPoolActor(options, config, streamPublisher ?? TestActor)));
    }

    /// <summary>
    /// When HostPoolActor spawns a ConnectionActor, the child's PreStart sends
    /// ClientManager.CreateTcpRunner to its _clientManager (the HostPoolActor).
    /// Since HostPoolActor doesn't handle CreateTcpRunner, it becomes an
    /// UnhandledMessage — letting us detect connection spawning and extract the child ref.
    /// </summary>
    private IActorRef ExpectConnectionSpawned(TimeSpan? timeout = null)
    {
        var msg = ExpectMsg<UnhandledMessage>(
            m => m.Message is ClientManager.CreateTcpRunner,
            timeout ?? TimeSpan.FromSeconds(3));

        var create = (ClientManager.CreateTcpRunner)msg.Message;
        return create.Handler;
    }

    [Fact(DisplayName = "HPA-001: First Incoming request spawns a new ConnectionActor child")]
    public void HPA_001_FirstIncoming_SpawnsConnectionActor()
    {
        Sys.EventStream.Subscribe(TestActor, typeof(UnhandledMessage));
        var pool = CreateHostPool();

        pool.Tell(new HostPoolActor.Incoming(MakeDataItem()));

        var connectionRef = ExpectConnectionSpawned();
        Assert.NotNull(connectionRef);
    }

    [Fact(DisplayName = "HPA-002: Second request while first connection is busy spawns a second ConnectionActor")]
    public void HPA_002_SecondRequest_WhileBusy_SpawnsSecondConnection()
    {
        Sys.EventStream.Subscribe(TestActor, typeof(UnhandledMessage));
        var pool = CreateHostPool(maxConnections: 2);

        // First request — spawns connection 1
        pool.Tell(new HostPoolActor.Incoming(MakeDataItem()));
        var conn1 = ExpectConnectionSpawned();

        // Second request — connection 1 is busy → spawns connection 2
        pool.Tell(new HostPoolActor.Incoming(MakeDataItem()));
        var conn2 = ExpectConnectionSpawned();

        Assert.NotEqual(conn1, conn2);
    }

    [Fact(DisplayName = "HPA-003: N parallel requests with MaxConnectionsPerHost=N spawns N connections")]
    public void HPA_003_NParallelRequests_SpawnsNConnections()
    {
        const int n = 4;
        Sys.EventStream.Subscribe(TestActor, typeof(UnhandledMessage));
        var pool = CreateHostPool(maxConnections: n);

        for (var i = 0; i < n; i++)
        {
            pool.Tell(new HostPoolActor.Incoming(MakeDataItem()));
            // Wait for each connection to spawn before sending next request,
            // ensuring the prior connection is marked busy before the next Incoming
            ExpectConnectionSpawned();
        }

        // Send one more — should NOT spawn (verified by no new CreateTcpRunner)
        pool.Tell(new HostPoolActor.Incoming(MakeDataItem()));
        ExpectNoMsg(TimeSpan.FromMilliseconds(500));
    }

    [Fact(DisplayName = "HPA-004: Request when MaxConnectionsPerHost is reached is queued in _pending")]
    public void HPA_004_MaxReached_RequestQueued()
    {
        Sys.EventStream.Subscribe(TestActor, typeof(UnhandledMessage));
        var pool = CreateHostPool(maxConnections: 1);

        // First request spawns a connection
        pool.Tell(new HostPoolActor.Incoming(MakeDataItem()));
        var connectionRef = ExpectConnectionSpawned();

        // Second request — connection is busy, max reached → should be queued (no new spawn)
        pool.Tell(new HostPoolActor.Incoming(MakeDataItem()));
        ExpectNoMsg(TimeSpan.FromMilliseconds(500));

        // Verify the queued request drains when the connection becomes idle:
        // Send ConnectionIdle to free the connection, which triggers DrainPending
        pool.Tell(new HostPoolActor.ConnectionIdle(connectionRef));

        // The queued DataItem is sent to the connection actor (no new spawn, still 1 connection).
        // No new CreateTcpRunner should appear.
        ExpectNoMsg(TimeSpan.FromMilliseconds(500));
    }

    [Fact(DisplayName = "HPA-005: Each spawned ConnectionActor is Context.Watch'ed")]
    public void HPA_005_SpawnedConnections_AreWatched()
    {
        Sys.EventStream.Subscribe(TestActor, typeof(UnhandledMessage));
        var pool = CreateHostPool(maxConnections: 2);

        pool.Tell(new HostPoolActor.Incoming(MakeDataItem()));
        var connectionRef = ExpectConnectionSpawned();

        // Watch the HostPoolActor from the test
        Watch(pool);

        // Stop the child ConnectionActor. If Context.Watch was called by HostPoolActor,
        // it receives Terminated — but has no handler, causing a DeathPactException.
        // This crashes (and stops) the HostPoolActor, proving Watch was active.
        Sys.Stop(connectionRef);

        // HostPoolActor terminates due to DeathPactException — confirms it was watching the child
        ExpectTerminated(pool, TimeSpan.FromSeconds(3));
    }

    // ================================================================
    // TASK-008: Idle Handling & Queue Draining
    // ================================================================

    [Fact(DisplayName = "HPA-020: ConnectionIdle decrements PendingRequests — connection becomes reusable")]
    public void HPA_020_ConnectionIdle_DecrementsPendingRequests()
    {
        Sys.EventStream.Subscribe(TestActor, typeof(UnhandledMessage));
        var pool = CreateHostPool(maxConnections: 1);

        // Send request → spawns connection, marks it busy (PendingRequests=1, Idle=false)
        pool.Tell(new HostPoolActor.Incoming(MakeDataItem()));
        var connectionRef = ExpectConnectionSpawned();

        // Send ConnectionIdle → MarkIdle: PendingRequests=0, Idle=true
        pool.Tell(new HostPoolActor.ConnectionIdle(connectionRef));

        // Send another request — if PendingRequests was decremented and Idle=true,
        // the existing connection is reused (no new spawn)
        pool.Tell(new HostPoolActor.Incoming(MakeDataItem()));
        ExpectNoMsg(TimeSpan.FromMilliseconds(500));
    }

    [Fact(DisplayName = "HPA-021: ConnectionIdle sets Idle=true when PendingRequests reaches 0")]
    public void HPA_021_ConnectionIdle_SetsIdleTrue_WhenPendingReachesZero()
    {
        Sys.EventStream.Subscribe(TestActor, typeof(UnhandledMessage));
        var pool = CreateHostPool(maxConnections: 2);

        // Spawn connection via request, then idle it
        pool.Tell(new HostPoolActor.Incoming(MakeDataItem()));
        var connectionRef = ExpectConnectionSpawned();

        pool.Tell(new HostPoolActor.ConnectionIdle(connectionRef));

        // Send two more requests. If connection is Idle=true, first request reuses it.
        // Second request should spawn a new connection (first is now busy again).
        pool.Tell(new HostPoolActor.Incoming(MakeDataItem()));
        pool.Tell(new HostPoolActor.Incoming(MakeDataItem()));

        // Only one new spawn expected (for second request) — proves first request reused the idle connection
        var conn2 = ExpectConnectionSpawned();
        Assert.NotEqual(connectionRef, conn2);

        // No further spawns
        ExpectNoMsg(TimeSpan.FromMilliseconds(500));
    }

    [Fact(DisplayName = "HPA-022: ConnectionIdle does NOT set Idle=true when PendingRequests > 0 after decrement (drain re-busies)")]
    public void HPA_022_ConnectionIdle_DoesNotSetIdle_WhenDrainReBusiesConnection()
    {
        Sys.EventStream.Subscribe(TestActor, typeof(UnhandledMessage));
        var pool = CreateHostPool(maxConnections: 1);

        // Spawn connection, make it busy
        pool.Tell(new HostPoolActor.Incoming(MakeDataItem()));
        var connectionRef = ExpectConnectionSpawned();

        // Queue a second request (max=1, connection is busy)
        pool.Tell(new HostPoolActor.Incoming(MakeDataItem()));
        ExpectNoMsg(TimeSpan.FromMilliseconds(300));

        // ConnectionIdle → MarkIdle (Idle=true), then DrainPending dequeues req2 → MarkBusy (Idle=false)
        pool.Tell(new HostPoolActor.ConnectionIdle(connectionRef));

        // Now send a third request. Connection is busy again (drained req2), so it should be queued.
        pool.Tell(new HostPoolActor.Incoming(MakeDataItem()));

        // No new spawn — proves the connection is NOT idle after drain
        ExpectNoMsg(TimeSpan.FromMilliseconds(500));
    }

    [Fact(DisplayName = "HPA-023: ConnectionIdle updates LastActivity — connection survives immediate idle eviction")]
    public async Task HPA_023_ConnectionIdle_UpdatesLastActivity()
    {
        Sys.EventStream.Subscribe(TestActor, typeof(UnhandledMessage));

        // Create pool with a very short idle timeout
        var options = MakeOptions();
        var config = new PoolConfig(
            MaxConnectionsPerHost: 2,
            IdleCheckInterval: TimeSpan.FromHours(1),
            IdleTimeout: TimeSpan.FromMilliseconds(200));

        var pool = Sys.ActorOf(Props.Create(() =>
            new HostPoolActor(options, config, TestActor)));

        // Spawn connection via request
        pool.Tell(new HostPoolActor.Incoming(MakeDataItem()));
        var connectionRef = ExpectConnectionSpawned();

        // Spawn a second connection (so eviction is allowed — min 1 preserved)
        pool.Tell(new HostPoolActor.Incoming(MakeDataItem()));
        var conn2 = ExpectConnectionSpawned();

        // Wait past idle timeout, then send ConnectionIdle (updates LastActivity to now)
        await Task.Delay(TimeSpan.FromMilliseconds(300));
        pool.Tell(new HostPoolActor.ConnectionIdle(connectionRef));

        // Trigger manual idle check — connection should NOT be evicted because
        // LastActivity was just updated by ConnectionIdle
        pool.Tell(new HostPoolActor.IdleCheck());

        // Verify connection survives: send a new request, should reuse the idle connection (no new spawn)
        pool.Tell(new HostPoolActor.Incoming(MakeDataItem()));
        ExpectNoMsg(TimeSpan.FromMilliseconds(500));
    }

    [Fact(DisplayName = "HPA-024: After ConnectionIdle, pending requests are drained to freed connection")]
    public void HPA_024_ConnectionIdle_DrainsPendingToFreedConnection()
    {
        Sys.EventStream.Subscribe(TestActor, typeof(UnhandledMessage));
        var pool = CreateHostPool(maxConnections: 1);

        // Spawn connection, mark busy
        pool.Tell(new HostPoolActor.Incoming(MakeDataItem()));
        var connectionRef = ExpectConnectionSpawned();

        // Queue a request
        pool.Tell(new HostPoolActor.Incoming(MakeDataItem()));
        ExpectNoMsg(TimeSpan.FromMilliseconds(300));

        // ConnectionIdle → connection freed → DrainPending sends queued request to it
        // No new connection should be spawned (the queued request goes to the existing connection)
        pool.Tell(new HostPoolActor.ConnectionIdle(connectionRef));
        ExpectNoMsg(TimeSpan.FromMilliseconds(500));
    }

    [Fact(DisplayName = "HPA-025: DrainPending stops when no idle connection is available")]
    public void HPA_025_DrainPending_StopsWhenNoIdleConnection()
    {
        Sys.EventStream.Subscribe(TestActor, typeof(UnhandledMessage));
        var pool = CreateHostPool(maxConnections: 1);

        // Spawn connection
        pool.Tell(new HostPoolActor.Incoming(MakeDataItem()));
        var connectionRef = ExpectConnectionSpawned();

        // Queue two more requests
        pool.Tell(new HostPoolActor.Incoming(MakeDataItem()));
        pool.Tell(new HostPoolActor.Incoming(MakeDataItem()));
        ExpectNoMsg(TimeSpan.FromMilliseconds(300));

        // ConnectionIdle → drain first pending (connection becomes busy), second pending stays queued
        pool.Tell(new HostPoolActor.ConnectionIdle(connectionRef));

        // No new spawn — only one pending drained, second stays queued
        ExpectNoMsg(TimeSpan.FromMilliseconds(300));

        // ConnectionIdle again → drain second pending
        pool.Tell(new HostPoolActor.ConnectionIdle(connectionRef));

        // Now send one more request — should still be queued (connection busy from draining second pending)
        pool.Tell(new HostPoolActor.Incoming(MakeDataItem()));
        ExpectNoMsg(TimeSpan.FromMilliseconds(500));
    }

    [Fact(DisplayName = "HPA-026: Multiple pending requests are drained sequentially across idle cycles")]
    public void HPA_026_MultiplePendingRequests_DrainedSequentially()
    {
        Sys.EventStream.Subscribe(TestActor, typeof(UnhandledMessage));
        var pool = CreateHostPool(maxConnections: 1);

        // Spawn one connection
        pool.Tell(new HostPoolActor.Incoming(MakeDataItem()));
        var connectionRef = ExpectConnectionSpawned();

        // Queue 3 requests
        pool.Tell(new HostPoolActor.Incoming(MakeDataItem()));
        pool.Tell(new HostPoolActor.Incoming(MakeDataItem()));
        pool.Tell(new HostPoolActor.Incoming(MakeDataItem()));
        ExpectNoMsg(TimeSpan.FromMilliseconds(300));

        // Drain cycle 1: idles connection → drains pending #1 → connection busy again
        pool.Tell(new HostPoolActor.ConnectionIdle(connectionRef));
        // Drain cycle 2: idles connection → drains pending #2 → connection busy again
        pool.Tell(new HostPoolActor.ConnectionIdle(connectionRef));
        // Drain cycle 3: idles connection → drains pending #3 → connection busy again
        pool.Tell(new HostPoolActor.ConnectionIdle(connectionRef));

        // All 3 pending drained, no new connections spawned
        ExpectNoMsg(TimeSpan.FromMilliseconds(300));

        // Final idle: connection is now truly idle (no more pending)
        pool.Tell(new HostPoolActor.ConnectionIdle(connectionRef));

        // New request should reuse the idle connection
        pool.Tell(new HostPoolActor.Incoming(MakeDataItem()));
        ExpectNoMsg(TimeSpan.FromMilliseconds(500));
    }

    [Fact(DisplayName = "HPA-027: ConnectionIdle for unknown connection is silently ignored")]
    public void HPA_027_ConnectionIdle_UnknownConnection_SilentlyIgnored()
    {
        Sys.EventStream.Subscribe(TestActor, typeof(UnhandledMessage));
        var pool = CreateHostPool(maxConnections: 1);

        // Spawn a connection so the pool has state
        pool.Tell(new HostPoolActor.Incoming(MakeDataItem()));
        var connectionRef = ExpectConnectionSpawned();

        // Send ConnectionIdle for an unknown actor ref
        var unknownRef = CreateTestProbe().Ref;
        pool.Tell(new HostPoolActor.ConnectionIdle(unknownRef));

        // Pool should still be alive and functional — send another request
        // Connection is busy, so this queues. Then idle the real connection to drain it.
        pool.Tell(new HostPoolActor.Incoming(MakeDataItem()));
        ExpectNoMsg(TimeSpan.FromMilliseconds(300));

        pool.Tell(new HostPoolActor.ConnectionIdle(connectionRef));

        // No crash, no new spawn — pool is healthy
        ExpectNoMsg(TimeSpan.FromMilliseconds(500));
    }
}
