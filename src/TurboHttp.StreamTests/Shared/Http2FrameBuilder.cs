using TurboHttp.Protocol;

namespace TurboHttp.StreamTests;

/// <summary>
/// Helpers for building raw HTTP/2 frame byte arrays for use in fake server responses.
/// </summary>
internal static class Http2FrameBuilder
{
    /// <summary>
    /// Builds a server SETTINGS frame (stream 0, empty payload).
    /// RFC 9113 §6.5: server sends SETTINGS as part of connection preface.
    /// </summary>
    public static byte[] BuildServerPreface() =>
        new SettingsFrame([]).Serialize();

    /// <summary>
    /// Builds a SETTINGS+ACK frame (stream 0, ACK flag set).
    /// RFC 9113 §6.5: recipient must send SETTINGS ACK after processing a SETTINGS frame.
    /// </summary>
    public static byte[] BuildSettingsAck() =>
        SettingsFrame.SettingsAck();

    /// <summary>
    /// Builds a HEADERS frame with the given HPACK block.
    /// RFC 9113 §6.2: HEADERS frame carries HTTP header fields.
    /// </summary>
    public static byte[] BuildHeadersFrame(int streamId, byte[] hpackBlock, bool endStream) =>
        new HeadersFrame(streamId, hpackBlock, endStream: endStream, endHeaders: true).Serialize();

    /// <summary>
    /// Builds a DATA frame with the given payload.
    /// RFC 9113 §6.1: DATA frame carries request/response body bytes.
    /// </summary>
    public static byte[] BuildDataFrame(int streamId, byte[] body, bool endStream) =>
        new DataFrame(streamId, body, endStream: endStream).Serialize();
}
