namespace TurboHttp.Protocol;

/// <summary>
/// RFC 9113 §5.1 — HTTP/2 stream states.
/// </summary>
public enum Http2StreamLifecycleState
{
    /// <summary>No frames have been exchanged on this stream yet.</summary>
    Idle,

    /// <summary>HEADERS have been received; the stream is active and awaiting DATA or END_STREAM.</summary>
    Open,

    /// <summary>
    /// The remote peer sent END_STREAM; no further DATA or HEADERS may arrive from the remote side.
    /// The stream transitions immediately to <see cref="Closed"/> once the local response is built.
    /// </summary>
    HalfClosedRemote,

    /// <summary>The stream has been fully closed (END_STREAM received or RST_STREAM processed).</summary>
    Closed,
}
