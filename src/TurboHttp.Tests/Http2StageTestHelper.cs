using System.Buffers.Binary;
using System.Collections.Generic;
using TurboHttp.Protocol;

namespace TurboHttp.Tests;

/// <summary>
/// Test helpers for stage-based HTTP/2 testing without Http2Decoder.
/// Wraps Http2FrameDecoder for raw frame parsing and provides server
/// preface validation matching RFC 9113 §3.4.
/// </summary>
public static class Http2StageTestHelper
{
    /// <summary>
    /// Decodes all complete frames from <paramref name="data"/> using a fresh
    /// <see cref="Http2FrameDecoder"/>. Partial trailing bytes are discarded.
    /// </summary>
    public static IReadOnlyList<Http2Frame> DecodeFrames(ReadOnlyMemory<byte> data) =>
        new Http2FrameDecoder().Decode(data);

    /// <summary>
    /// Validates that <paramref name="bytes"/> begins with a SETTINGS frame on stream 0
    /// (the mandatory server connection preface per RFC 9113 §3.4).
    /// Returns false if fewer than 9 bytes are available (need more data).
    /// Throws <see cref="Http2Exception"/> (PROTOCOL_ERROR) if the first frame
    /// is not a SETTINGS frame on stream 0.
    /// </summary>
    public static bool ValidateServerPreface(ReadOnlyMemory<byte> bytes)
    {
        if (bytes.Length < 9)
        {
            return false;
        }

        var span = bytes.Span;
        var frameType = (FrameType)span[3];
        var streamId = (int)(BinaryPrimitives.ReadUInt32BigEndian(span[5..]) & 0x7FFFFFFFu);

        if (frameType != FrameType.Settings || streamId != 0)
        {
            throw new Http2Exception(
                $"RFC 9113 §3.4: Server connection preface must be a SETTINGS frame on stream 0; " +
                $"got type={frameType}, streamId={streamId}.");
        }

        return true;
    }
}
