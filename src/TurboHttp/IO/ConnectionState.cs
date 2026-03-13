using System;
using System.Net;
using Akka.Actor;

namespace TurboHttp.IO;

internal sealed class ConnectionState
{
    public IActorRef Actor { get; }
    public bool Active { get; private set; } = true;
    public bool Idle { get; private set; } = true;
    public int PendingRequests { get; private set; }
    public DateTime LastActivity { get; private set; } = DateTime.UtcNow;

    /// <summary>
    /// Whether this connection can be reused for subsequent requests.
    /// Set to false when a Connection: close header is received or server signals close.
    /// </summary>
    public bool Reusable { get; private set; } = true;

    /// <summary>
    /// The HTTP version this connection is operating under.
    /// Determines connection selection semantics (reuse vs multiplexing).
    /// </summary>
    public Version HttpVersion { get; set; } = System.Net.HttpVersion.Version11;

    /// <summary>
    /// Maximum concurrent streams allowed on this connection (HTTP/2 only).
    /// Corresponds to the SETTINGS_MAX_CONCURRENT_STREAMS parameter from RFC 9113 §6.5.2.
    /// </summary>
    public int MaxConcurrentStreams { get; set; } = 100;

    /// <summary>
    /// Number of currently active streams on this connection (HTTP/2 only).
    /// </summary>
    public int ActiveStreamCount { get; private set; }

    /// <summary>
    /// Next stream ID to allocate for client-initiated streams (HTTP/2 only).
    /// Client stream IDs are odd and increment by 2 per RFC 9113 §5.1.1.
    /// </summary>
    public int NextStreamId { get; private set; } = 1;

    public ConnectionState(IActorRef actor)
    {
        Actor = actor;
    }

    public void MarkBusy()
    {
        Idle = false;
        PendingRequests++;
        LastActivity = DateTime.UtcNow;
    }

    public void MarkIdle()
    {
        PendingRequests--;

        if (PendingRequests == 0)
        {
            Idle = true;
        }

        LastActivity = DateTime.UtcNow;
    }

    public void MarkDead()
    {
        Active = false;
    }

    /// <summary>
    /// Marks this connection as non-reusable (e.g., after receiving Connection: close).
    /// </summary>
    public void MarkNoReuse()
    {
        Reusable = false;
    }

    /// <summary>
    /// Allocates the next stream ID for an HTTP/2 request.
    /// Returns the allocated stream ID and increments ActiveStreamCount.
    /// Client-initiated stream IDs are odd and increment by 2 (RFC 9113 §5.1.1).
    /// </summary>
    public int AllocateStreamId()
    {
        var id = NextStreamId;
        NextStreamId += 2;
        ActiveStreamCount++;
        LastActivity = DateTime.UtcNow;
        return id;
    }

    /// <summary>
    /// Releases an HTTP/2 stream slot, making room for new streams.
    /// </summary>
    public void ReleaseStream()
    {
        if (ActiveStreamCount > 0)
        {
            ActiveStreamCount--;
        }

        LastActivity = DateTime.UtcNow;
    }

    /// <summary>
    /// Whether this HTTP/2 connection has available stream capacity.
    /// </summary>
    public bool HasAvailableStreamCapacity => ActiveStreamCount < MaxConcurrentStreams;
}
