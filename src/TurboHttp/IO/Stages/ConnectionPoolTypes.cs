using System;
using System.Buffers;

namespace TurboHttp.IO.Stages;

public sealed record RoutedTransportItem(string PoolKey, ITransportItem Item);

public sealed record RoutedDataItem(string PoolKey, IMemoryOwner<byte> Memory, int Length);

/// <summary>
/// Strategy for handling queue overflow when a per-host backpressure queue is full.
/// </summary>
public enum QueueOverflowStrategy
{
    /// <summary>
    /// Fail the stage when the per-host queue overflows. This is the default.
    /// </summary>
    Fail,

    /// <summary>
    /// Drop the oldest item in the queue to make room for the new item.
    /// </summary>
    DropOldest,

    /// <summary>
    /// Drop the newest (incoming) item when the queue is full.
    /// </summary>
    DropNewest
}

/// <summary>
/// Strategy for distributing requests across active connections for a host.
/// </summary>
public enum LoadBalancingStrategy
{
    /// <summary>
    /// Selects the connection with the fewest pending requests.
    /// Idle connections are always preferred.
    /// </summary>
    LeastLoaded,

    /// <summary>
    /// Cycles through all active connections in order.
    /// Idle connections are always preferred.
    /// </summary>
    RoundRobin
}

public sealed record PoolConfig(
    int MaxConnectionsPerHost = 10,
    TimeSpan IdleTimeout = default,
    TimeSpan ConnectionTimeout = default,
    LoadBalancingStrategy Strategy = LoadBalancingStrategy.LeastLoaded,
    int MaxReconnectAttempts = 3,
    TimeSpan ReconnectInterval = default,
    TimeSpan IdleCheckInterval = default,
    int PerHostQueueSize = 100,
    QueueOverflowStrategy OverflowStrategy = QueueOverflowStrategy.Fail)
{
    public TimeSpan IdleTimeout { get; init; } =
        IdleTimeout == TimeSpan.Zero ? TimeSpan.FromMinutes(5) : IdleTimeout;

    public TimeSpan ConnectionTimeout { get; init; } =
        ConnectionTimeout == TimeSpan.Zero ? TimeSpan.FromSeconds(30) : ConnectionTimeout;

    public TimeSpan ReconnectInterval { get; init; } =
        ReconnectInterval == TimeSpan.Zero ? TimeSpan.FromSeconds(5) : ReconnectInterval;

    /// <summary>
    /// How often the idle eviction timer fires to check for stale connections.
    /// Defaults to 30 seconds.
    /// </summary>
    public TimeSpan IdleCheckInterval { get; init; } =
        IdleCheckInterval == TimeSpan.Zero ? TimeSpan.FromSeconds(30) : IdleCheckInterval;
}

/// <summary>
/// Thrown when the connection pool exhausts reconnect attempts for a host.
/// </summary>
public sealed class ConnectionPoolException : Exception
{
    public ConnectionPoolException(string message) : base(message) { }
    public ConnectionPoolException(string message, Exception innerException) : base(message, innerException) { }
}
