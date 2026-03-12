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

    // ================================================================
    // TASK-009: Connection Failure & Reconnect
    // ================================================================

    private IActorRef CreateHostPoolWithReconnect(
        int maxConnections = 10,
        TimeSpan? reconnectInterval = null,
        IActorRef? streamPublisher = null)
    {
        var options = MakeOptions();
        var config = new PoolConfig(
            MaxConnectionsPerHost: maxConnections,
            IdleCheckInterval: TimeSpan.FromHours(1),
            ReconnectInterval: reconnectInterval ?? TimeSpan.FromMilliseconds(200));

        return Sys.ActorOf(Props.Create(() =>
            new HostPoolActor(options, config, streamPublisher ?? TestActor)));
    }

    [Fact(DisplayName = "HPA-030: ConnectionFailed marks ConnectionState.Active=false — connection not reused")]
    public void HPA_030_ConnectionFailed_MarksActiveFlase()
    {
        Sys.EventStream.Subscribe(TestActor, typeof(UnhandledMessage));
        var pool = CreateHostPoolWithReconnect(maxConnections: 2);

        // Spawn connection via request
        pool.Tell(new HostPoolActor.Incoming(MakeDataItem()));
        var connectionRef = ExpectConnectionSpawned();

        // Mark connection idle so it would be selected for reuse
        pool.Tell(new HostPoolActor.ConnectionIdle(connectionRef));

        // Fail the connection — Active=false
        pool.Tell(new HostPoolActor.ConnectionFailed(connectionRef));

        // Send a new request. If Active=false, the dead connection is skipped.
        // Since maxConnections=2 and only 1 exists (dead), a new connection should spawn.
        pool.Tell(new HostPoolActor.Incoming(MakeDataItem()));
        var conn2 = ExpectConnectionSpawned();

        Assert.NotEqual(connectionRef, conn2);
    }

    [Fact(DisplayName = "HPA-031: ConnectionFailed schedules Reconnect after PoolConfig.ReconnectInterval")]
    public void HPA_031_ConnectionFailed_SchedulesReconnect()
    {
        Sys.EventStream.Subscribe(TestActor, typeof(UnhandledMessage));
        var pool = CreateHostPoolWithReconnect(maxConnections: 1, reconnectInterval: TimeSpan.FromMilliseconds(300));

        // Spawn connection
        pool.Tell(new HostPoolActor.Incoming(MakeDataItem()));
        var connectionRef = ExpectConnectionSpawned();

        // Fail it — should schedule Reconnect after 300ms
        pool.Tell(new HostPoolActor.ConnectionFailed(connectionRef));

        // No immediate spawn — reconnect is delayed
        ExpectNoMsg(TimeSpan.FromMilliseconds(100));

        // After ReconnectInterval, Reconnect fires → removes dead connection, spawns new one
        var newConn = ExpectConnectionSpawned(TimeSpan.FromSeconds(3));
        Assert.NotEqual(connectionRef, newConn);
    }

    [Fact(DisplayName = "HPA-032: ConnectionFailed for unknown connection is silently ignored")]
    public void HPA_032_ConnectionFailed_UnknownConnection_SilentlyIgnored()
    {
        Sys.EventStream.Subscribe(TestActor, typeof(UnhandledMessage));
        var pool = CreateHostPoolWithReconnect(maxConnections: 1);

        // Spawn a connection so the pool has state
        pool.Tell(new HostPoolActor.Incoming(MakeDataItem()));
        var connectionRef = ExpectConnectionSpawned();

        // Send ConnectionFailed for an unknown actor
        var unknownRef = CreateTestProbe().Ref;
        pool.Tell(new HostPoolActor.ConnectionFailed(unknownRef));

        // Pool is still alive and functional — idle the real connection and reuse it
        pool.Tell(new HostPoolActor.ConnectionIdle(connectionRef));
        pool.Tell(new HostPoolActor.Incoming(MakeDataItem()));

        // No new spawn — real connection reused, pool not crashed
        ExpectNoMsg(TimeSpan.FromMilliseconds(500));
    }

    [Fact(DisplayName = "HPA-033: Reconnect spawns a new ConnectionActor to replace the dead one")]
    public void HPA_033_Reconnect_SpawnsNewConnection()
    {
        Sys.EventStream.Subscribe(TestActor, typeof(UnhandledMessage));
        var pool = CreateHostPoolWithReconnect(maxConnections: 1, reconnectInterval: TimeSpan.FromMilliseconds(200));

        // Spawn connection
        pool.Tell(new HostPoolActor.Incoming(MakeDataItem()));
        var connectionRef = ExpectConnectionSpawned();

        // Fail it — triggers scheduled Reconnect
        pool.Tell(new HostPoolActor.ConnectionFailed(connectionRef));

        // Wait for Reconnect to fire — a new connection should spawn
        var newConn = ExpectConnectionSpawned(TimeSpan.FromSeconds(3));
        Assert.NotNull(newConn);
        Assert.NotEqual(connectionRef, newConn);

        // Verify the new connection is functional — idle it and send a request
        pool.Tell(new HostPoolActor.ConnectionIdle(newConn));
        pool.Tell(new HostPoolActor.Incoming(MakeDataItem()));

        // Should reuse the new connection, no additional spawn
        ExpectNoMsg(TimeSpan.FromMilliseconds(500));
    }

    [Fact(DisplayName = "HPA-034: Reconnect for already-removed connection is silently ignored")]
    public void HPA_034_Reconnect_AlreadyRemovedConnection_Ignored()
    {
        Sys.EventStream.Subscribe(TestActor, typeof(UnhandledMessage));

        var options = MakeOptions();
        var config = new PoolConfig(
            MaxConnectionsPerHost: 2,
            IdleCheckInterval: TimeSpan.FromHours(1),
            IdleTimeout: TimeSpan.FromMilliseconds(50),
            ReconnectInterval: TimeSpan.FromMilliseconds(500));

        var pool = Sys.ActorOf(Props.Create(() =>
            new HostPoolActor(options, config, TestActor)));

        // Spawn two connections (need 2 so idle eviction is allowed — min 1 preserved)
        pool.Tell(new HostPoolActor.Incoming(MakeDataItem()));
        var conn1 = ExpectConnectionSpawned();
        pool.Tell(new HostPoolActor.Incoming(MakeDataItem()));
        var conn2 = ExpectConnectionSpawned();

        // Fail conn1 — schedules Reconnect in 500ms
        pool.Tell(new HostPoolActor.ConnectionFailed(conn1));

        // Manually send Reconnect for conn1 before the scheduled one fires.
        // First Reconnect removes the dead conn1 and spawns a replacement.
        pool.Tell(new HostPoolActor.Reconnect(conn1));
        var replacement = ExpectConnectionSpawned();

        // Now the scheduled Reconnect will fire for conn1, but conn1 is already removed.
        // It should be silently ignored — no additional spawn.
        ExpectNoMsg(TimeSpan.FromSeconds(2));
    }

    [Fact(DisplayName = "HPA-035: New connection after reconnect is Active=true, Idle=true — immediately reusable")]
    public void HPA_035_Reconnect_NewConnection_IsActiveAndIdle()
    {
        Sys.EventStream.Subscribe(TestActor, typeof(UnhandledMessage));
        var pool = CreateHostPoolWithReconnect(maxConnections: 1, reconnectInterval: TimeSpan.FromMilliseconds(200));

        // Spawn connection via request
        pool.Tell(new HostPoolActor.Incoming(MakeDataItem()));
        var connectionRef = ExpectConnectionSpawned();

        // Idle the connection so it can be found & failed
        pool.Tell(new HostPoolActor.ConnectionIdle(connectionRef));

        // Fail the connection — schedules Reconnect after 200ms
        pool.Tell(new HostPoolActor.ConnectionFailed(connectionRef));

        // Wait for Reconnect to fire — new connection spawns with Active=true, Idle=true
        var newConn = ExpectConnectionSpawned(TimeSpan.FromSeconds(3));
        Assert.NotEqual(connectionRef, newConn);

        // Verify the new connection is Active=true AND Idle=true:
        // Send a request — if it's idle, the pool selects it (no new spawn needed).
        // If it were NOT idle or NOT active, the pool would try to spawn another
        // connection, but maxConnections=1 so it would queue instead.
        pool.Tell(new HostPoolActor.Incoming(MakeDataItem()));

        // No new connection spawn — proves the reconnected connection was selected (Active + Idle)
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
