using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using Akka.Actor;
using TurboHttp.IO.Stages;

namespace TurboHttp.IO;

public sealed class HostPoolActor : ReceiveActor
{
    public sealed record ConnectionIdle(IActorRef Connection);

    public sealed record ConnectionFailed(IActorRef Connection);

    public sealed record ConnectionResponse(IActorRef Connection, IMemoryOwner<byte> Memory, int Length);

    public sealed record IdleCheck;

    public sealed record Reconnect(IActorRef Connection);

    public sealed record StreamComplete(IActorRef Connection);

    public sealed record MarkConnectionNoReuse(IActorRef Connection);

    private readonly TcpOptions _options;
    private readonly PoolConfig _config;

    private readonly List<ConnectionState> _connections = [];
    private readonly Queue<PendingItem> _pending = new();
    private readonly Dictionary<IActorRef, Queue<PendingReplyTo>> _replyToMap = new();

    public HostPoolActor(TcpOptions options, PoolConfig config)
    {
        _options = options;
        _config = config;

        Receive<PoolRouterActor.SendRequest>(HandleRequest);
        Receive<ConnectionIdle>(HandleIdle);
        Receive<ConnectionResponse>(HandleResponse);
        Receive<ConnectionFailed>(HandleFailure);
        Receive<IdleCheck>(_ => EvictIdleConnections());
        Receive<Reconnect>(HandleReconnect);
        Receive<StreamComplete>(HandleStreamComplete);
        Receive<MarkConnectionNoReuse>(HandleMarkNoReuse);
    }

    protected override void PreStart()
    {
        Context.System.Scheduler.ScheduleTellRepeatedlyCancelable(
            _config.IdleCheckInterval,
            _config.IdleCheckInterval,
            Self,
            new IdleCheck(),
            Self);
    }

    private void HandleRequest(PoolRouterActor.SendRequest msg)
    {
        var version = msg.HttpVersion ?? HttpVersion.Version11;
        var pending = new PendingReplyTo(msg.ReplyTo, msg.PoolKey, version);
        var conn = SelectConnection(version);

        if (conn != null)
        {
            if (version == HttpVersion.Version20)
            {
                conn.AllocateStreamId();
            }

            SendToConnection(conn, msg.Data, pending);
            return;
        }

        if (_connections.Count < _config.MaxConnectionsPerHost)
        {
            conn = SpawnConnection();
            conn.HttpVersion = version;

            if (version == HttpVersion.Version20)
            {
                conn.AllocateStreamId();
            }

            SendToConnection(conn, msg.Data, pending);
            return;
        }

        _pending.Enqueue(new PendingItem(msg.Data, pending));
    }

    private ConnectionState? SelectConnection(Version? version = null)
    {
        version ??= HttpVersion.Version11;

        if (version == HttpVersion.Version20)
        {
            return SelectHttp2Connection();
        }

        return SelectHttp1Connection(version);
    }

    private ConnectionState? SelectHttp1Connection(Version version)
    {
        // HTTP/1.0 without explicit keep-alive: never reuse
        if (version == HttpVersion.Version10)
        {
            // Only select idle, active, reusable connections (those with explicit keep-alive)
            return _connections.FirstOrDefault(x =>
                x is { Active: true, Idle: true, Reusable: true } &&
                x.HttpVersion == HttpVersion.Version10 &&
                x.PendingRequests < _config.MaxRequestsPerConnection);
        }

        // HTTP/1.1: prefer idle, active, reusable connections
        return _connections.FirstOrDefault(x =>
            x is { Active: true, Idle: true, Reusable: true } &&
            x.PendingRequests < _config.MaxRequestsPerConnection);
    }

    private ConnectionState? SelectHttp2Connection()
    {
        // HTTP/2: reuse existing active connection with available stream capacity (multiplexing)
        return _connections.FirstOrDefault(x =>
            x is { Active: true } &&
            x.HttpVersion == HttpVersion.Version20 &&
            x.HasAvailableStreamCapacity);
    }

    private void SendToConnection(ConnectionState conn, DataItem data, PendingReplyTo pending)
    {
        conn.MarkBusy();
        conn.Actor.Tell(data);

        if (!_replyToMap.TryGetValue(conn.Actor, out var queue))
        {
            queue = new Queue<PendingReplyTo>();
            _replyToMap[conn.Actor] = queue;
        }

        queue.Enqueue(pending);
    }

    private ConnectionState SpawnConnection()
    {
        var actor = Context.ActorOf(
            Props.Create(() =>
                new ConnectionActor(_options, Self)));

        Context.Watch(actor);

        var state = new ConnectionState(actor);

        _connections.Add(state);

        return state;
    }

    private void HandleIdle(ConnectionIdle msg)
    {
        var conn = Find(msg.Connection);

        if (conn == null)
            return;

        conn.MarkIdle();

        DrainPending();
    }

    private void HandleResponse(ConnectionResponse msg)
    {
        if (!_replyToMap.TryGetValue(msg.Connection, out var queue) || queue.Count == 0)
        {
            return;
        }

        var pending = queue.Dequeue();
        pending.ReplyTo.Tell(new PoolRouterActor.Response(pending.PoolKey, msg.Memory, msg.Length));
    }

    private void HandleFailure(ConnectionFailed msg)
    {
        var conn = Find(msg.Connection);

        if (conn == null)
            return;

        conn.MarkDead();

        Context.System.Scheduler.ScheduleTellOnceCancelable(
            _config.ReconnectInterval,
            Self,
            new Reconnect(msg.Connection),
            Self);
    }

    private void HandleReconnect(Reconnect msg)
    {
        var conn = Find(msg.Connection);

        if (conn == null)
            return;

        var previousVersion = conn.HttpVersion;
        _replyToMap.Remove(msg.Connection);
        _connections.Remove(conn);

        var newConn = SpawnConnection();
        newConn.HttpVersion = previousVersion;
    }

    private void EvictIdleConnections()
    {
        var now = DateTime.UtcNow;

        foreach (var conn in _connections.ToArray())
        {
            if (!conn.Idle)
            {
                continue;
            }

            if (now - conn.LastActivity > _config.IdleTimeout && _connections.Count > 1)
            {
                Context.Unwatch(conn.Actor);
                conn.Actor.Tell(PoisonPill.Instance);
                _replyToMap.Remove(conn.Actor);
                _connections.Remove(conn);
            }
        }
    }

    private void HandleStreamComplete(StreamComplete msg)
    {
        var conn = Find(msg.Connection);

        if (conn == null)
        {
            return;
        }

        conn.ReleaseStream();

        DrainPending();
    }

    private void HandleMarkNoReuse(MarkConnectionNoReuse msg)
    {
        var conn = Find(msg.Connection);
        conn?.MarkNoReuse();
    }

    private void DrainPending()
    {
        while (_pending.Count > 0)
        {
            var version = _pending.Peek().Pending.HttpVersion;
            var conn = SelectConnection(version);

            if (conn == null)
            {
                break;
            }

            var item = _pending.Dequeue();

            if (version == HttpVersion.Version20)
            {
                conn.AllocateStreamId();
            }

            SendToConnection(conn, item.Data, item.Pending);
        }
    }

    private ConnectionState? Find(IActorRef actor)
        => _connections.Find(x => x.Actor.Equals(actor));

    private readonly record struct PendingReplyTo(IActorRef ReplyTo, string PoolKey, Version HttpVersion);

    private readonly record struct PendingItem(DataItem Data, PendingReplyTo Pending);
}