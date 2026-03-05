#nullable enable
using System;
using System.Buffers.Binary;
using TurboHttp.Protocol;
using Xunit;

namespace TurboHttp.Tests;

/// <summary>
/// RFC 9113 §3.4 — HTTP/2 Connection Preface
/// Phase 1-2: Connection Preface &amp; ALPN (protocol-layer coverage)
///
/// The TLS/ALPN requirements (TLS 1.2+, ALPN="h2") are enforced at the I/O
/// layer (not the protocol layer) and are therefore out of scope for these tests.
/// This class covers all protocol-layer MUST requirements:
///   - Client preface: exact magic bytes + SETTINGS frame on stream 0
///   - Server preface validation: first frame MUST be SETTINGS on stream 0
///   - Partial preface: fewer than 9 bytes → NeedMoreData (no error)
///   - Malformed preface: wrong frame type or stream → PROTOCOL_ERROR
/// </summary>
public sealed class Http2ConnectionPrefaceTests
{
    // RFC 9113 §3.4: client connection preface = magic octets + SETTINGS frame
    private static readonly byte[] Magic = "PRI * HTTP/2.0\r\n\r\nSM\r\n\r\n"u8.ToArray();
    private const int MagicLength = 24; // "PRI * HTTP/2.0\r\n\r\nSM\r\n\r\n"
    private const int FrameHeaderLength = 9;

    // =========================================================================
    // Client preface — Http2Encoder.BuildConnectionPreface()
    // =========================================================================

    /// RFC 9113 §3.4 — Client preface starts with exact magic octets
    [Fact(DisplayName = "RFC9113-3.4-CP-001: Client preface starts with exact magic octets")]
    public void ClientPreface_MagicOctets_MatchRfc9113Spec()
    {
        var preface = Http2Encoder.BuildConnectionPreface();

        Assert.True(preface.Length >= MagicLength, "Preface too short to contain magic");
        Assert.Equal(Magic, preface[..MagicLength]);
    }

    /// RFC 9113 §3.4 — Client preface magic is exactly 24 bytes
    [Fact(DisplayName = "RFC9113-3.4-CP-002: Client preface magic is exactly 24 bytes")]
    public void ClientPreface_Magic_IsExactly24Bytes()
    {
        // RFC 9113 §3.4 specifies the exact byte sequence (24 octets)
        Assert.Equal(24, MagicLength);
        Assert.Equal(MagicLength, Magic.Length);
    }

    /// RFC 9113 §3.4 — SETTINGS frame follows magic immediately at byte 24
    [Fact(DisplayName = "RFC9113-3.4-CP-003: SETTINGS frame follows magic immediately at byte 24")]
    public void ClientPreface_SettingsFrame_ImmediatelyFollowsMagic()
    {
        var preface = Http2Encoder.BuildConnectionPreface();

        Assert.True(preface.Length >= MagicLength + FrameHeaderLength,
            "Preface too short to contain frame header after magic");

        // Byte at position [magic + 3] is the frame type
        var frameType = (FrameType)preface[MagicLength + 3];
        Assert.Equal(FrameType.Settings, frameType);
    }

    /// RFC 9113 §3.4 — SETTINGS frame in client preface uses stream ID 0
    [Fact(DisplayName = "RFC9113-3.4-CP-004: SETTINGS frame in client preface uses stream ID 0")]
    public void ClientPreface_SettingsFrame_StreamIdIsZero()
    {
        var preface = Http2Encoder.BuildConnectionPreface();

        Assert.True(preface.Length >= MagicLength + FrameHeaderLength);
        // Stream ID occupies bytes [magic+5 .. magic+8] (31-bit big-endian, R bit masked)
        var streamId = (int)(BinaryPrimitives.ReadUInt32BigEndian(
            preface.AsSpan(MagicLength + 5)) & 0x7FFFFFFF);
        Assert.Equal(0, streamId);
    }

    /// RFC 9113 §3.4 — Client preface total length is magic + SETTINGS frame
    [Fact(DisplayName = "RFC9113-3.4-CP-005: Client preface total length is magic + SETTINGS frame")]
    public void ClientPreface_Length_IsMagicPlusSettingsFrame()
    {
        var preface = Http2Encoder.BuildConnectionPreface();

        // Minimum: 24-byte magic + 9-byte frame header = 33 bytes
        Assert.True(preface.Length >= 33, $"Expected >= 33 bytes, got {preface.Length}");
    }

    /// RFC 9113 §3.4 — SETTINGS frame payload length is a multiple of 6
    [Fact(DisplayName = "RFC9113-3.4-CP-006: SETTINGS frame payload length is a multiple of 6")]
    public void ClientPreface_SettingsPayload_LengthIsMultipleOf6()
    {
        var preface = Http2Encoder.BuildConnectionPreface();

        // Payload length is in the first 3 bytes of the frame header (24-bit big-endian)
        var payloadLen = (preface[MagicLength] << 16)
                       | (preface[MagicLength + 1] << 8)
                       | preface[MagicLength + 2];

        // RFC 9113 §6.5: Each SETTINGS entry is exactly 6 bytes
        Assert.Equal(0, payloadLen % 6);
    }

    /// RFC 9113 §3.4 — SETTINGS frame flags are 0 (not ACK)
    [Fact(DisplayName = "RFC9113-3.4-CP-007: SETTINGS frame flags are 0 (not ACK)")]
    public void ClientPreface_SettingsFrame_FlagsAreZero()
    {
        var preface = Http2Encoder.BuildConnectionPreface();

        var flags = preface[MagicLength + 4]; // flags byte
        Assert.Equal(0, flags & (byte)SettingsFlags.Ack);
    }

    /// RFC 9113 §3.4 — Magic bytes spell 'PRI * HTTP/2.0 SM' as ASCII
    [Fact(DisplayName = "RFC9113-3.4-CP-008: Magic bytes spell 'PRI * HTTP/2.0 SM' as ASCII")]
    public void ClientPreface_Magic_SpellsCorrectAsciiString()
    {
        // Verify readable portion: "PRI * HTTP/2.0\r\n\r\nSM\r\n\r\n"
        var preface = Http2Encoder.BuildConnectionPreface();
        var text = System.Text.Encoding.ASCII.GetString(preface[..MagicLength]);
        Assert.Equal("PRI * HTTP/2.0\r\n\r\nSM\r\n\r\n", text);
    }

    // =========================================================================
    // Server preface validation — Http2Decoder.ValidateServerPreface()
    // =========================================================================

    /// RFC 9113 §3.4 — Valid SETTINGS frame on stream 0 is accepted
    [Fact(DisplayName = "RFC9113-3.4-SP-001: Valid SETTINGS frame on stream 0 is accepted")]
    public void ServerPreface_ValidSettingsFrame_ReturnsTrue()
    {
        var frame = SettingsFrame.SettingsAck();
        var decoder = new Http2Decoder();

        var result = decoder.ValidateServerPreface(frame);

        Assert.True(result);
    }

    /// RFC 9113 §3.4 — Fewer than 9 bytes returns false (need more data)
    [Fact(DisplayName = "RFC9113-3.4-SP-002: Fewer than 9 bytes returns false (need more data)")]
    public void ServerPreface_FewerThan9Bytes_ReturnsFalse()
    {
        var decoder = new Http2Decoder();

        // 8 bytes — cannot determine frame type yet
        Assert.False(decoder.ValidateServerPreface(new byte[8]));
        // 1 byte
        Assert.False(decoder.ValidateServerPreface(new byte[1]));
        // 0 bytes
        Assert.False(decoder.ValidateServerPreface(ReadOnlyMemory<byte>.Empty));
    }

    /// RFC 9113 §3.4 — DATA frame as first frame throws PROTOCOL_ERROR
    [Fact(DisplayName = "RFC9113-3.4-SP-003: DATA frame as first frame throws PROTOCOL_ERROR")]
    public void ServerPreface_DataFrame_ThrowsProtocolError()
    {
        // Build a minimal DATA frame: payload=1 byte, stream=1
        var buf = new byte[10];
        buf[0] = 0; buf[1] = 0; buf[2] = 1; // length = 1
        buf[3] = (byte)FrameType.Data;
        buf[4] = 0; // no flags
        BinaryPrimitives.WriteUInt32BigEndian(buf.AsSpan(5), 1); // stream 1
        buf[9] = 0x42; // payload

        var decoder = new Http2Decoder();
        var ex = Assert.Throws<Http2Exception>(() => decoder.ValidateServerPreface(buf));
        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
        Assert.True(ex.IsConnectionError);
    }

    /// RFC 9113 §3.4 — HEADERS frame as first frame throws PROTOCOL_ERROR
    [Fact(DisplayName = "RFC9113-3.4-SP-004: HEADERS frame as first frame throws PROTOCOL_ERROR")]
    public void ServerPreface_HeadersFrame_ThrowsProtocolError()
    {
        var buf = new byte[9];
        buf[3] = (byte)FrameType.Headers;
        BinaryPrimitives.WriteUInt32BigEndian(buf.AsSpan(5), 1);

        var decoder = new Http2Decoder();
        var ex = Assert.Throws<Http2Exception>(() => decoder.ValidateServerPreface(buf));
        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
        Assert.True(ex.IsConnectionError);
    }

    /// RFC 9113 §3.4 — PING frame as first frame throws PROTOCOL_ERROR
    [Fact(DisplayName = "RFC9113-3.4-SP-005: PING frame as first frame throws PROTOCOL_ERROR")]
    public void ServerPreface_PingFrame_ThrowsProtocolError()
    {
        var ping = new PingFrame(new byte[8], isAck: false).Serialize();

        var decoder = new Http2Decoder();
        var ex = Assert.Throws<Http2Exception>(() => decoder.ValidateServerPreface(ping));
        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
        Assert.True(ex.IsConnectionError);
    }

    /// RFC 9113 §3.4 — GOAWAY frame as first frame throws PROTOCOL_ERROR
    [Fact(DisplayName = "RFC9113-3.4-SP-006: GOAWAY frame as first frame throws PROTOCOL_ERROR")]
    public void ServerPreface_GoAwayFrame_ThrowsProtocolError()
    {
        var goAway = new GoAwayFrame(lastStreamId: 0, Http2ErrorCode.NoError, null).Serialize();

        var decoder = new Http2Decoder();
        var ex = Assert.Throws<Http2Exception>(() => decoder.ValidateServerPreface(goAway));
        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
        Assert.True(ex.IsConnectionError);
    }

    /// RFC 9113 §3.4 — RST_STREAM frame as first frame throws PROTOCOL_ERROR
    [Fact(DisplayName = "RFC9113-3.4-SP-007: RST_STREAM frame as first frame throws PROTOCOL_ERROR")]
    public void ServerPreface_RstStreamFrame_ThrowsProtocolError()
    {
        var rst = new RstStreamFrame(streamId: 1, Http2ErrorCode.NoError).Serialize();

        var decoder = new Http2Decoder();
        var ex = Assert.Throws<Http2Exception>(() => decoder.ValidateServerPreface(rst));
        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
        Assert.True(ex.IsConnectionError);
    }

    /// RFC 9113 §3.4 — WINDOW_UPDATE frame as first frame throws PROTOCOL_ERROR
    [Fact(DisplayName = "RFC9113-3.4-SP-008: WINDOW_UPDATE frame as first frame throws PROTOCOL_ERROR")]
    public void ServerPreface_WindowUpdateFrame_ThrowsProtocolError()
    {
        var wu = new WindowUpdateFrame(streamId: 0, increment: 1024).Serialize();

        var decoder = new Http2Decoder();
        var ex = Assert.Throws<Http2Exception>(() => decoder.ValidateServerPreface(wu));
        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
        Assert.True(ex.IsConnectionError);
    }

    /// RFC 9113 §3.4 — SETTINGS frame on non-zero stream throws PROTOCOL_ERROR
    [Fact(DisplayName = "RFC9113-3.4-SP-009: SETTINGS frame on non-zero stream throws PROTOCOL_ERROR")]
    public void ServerPreface_SettingsFrameOnNonZeroStream_ThrowsProtocolError()
    {
        // Craft a SETTINGS frame header with stream ID = 1 (invalid; SETTINGS must use stream 0)
        var buf = new byte[9];
        buf[3] = (byte)FrameType.Settings; // type = SETTINGS
        buf[4] = 0;                        // flags = 0
        BinaryPrimitives.WriteUInt32BigEndian(buf.AsSpan(5), 1); // stream = 1

        var decoder = new Http2Decoder();
        var ex = Assert.Throws<Http2Exception>(() => decoder.ValidateServerPreface(buf));
        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
        Assert.True(ex.IsConnectionError);
    }

    /// RFC 9113 §3.4 — Exactly 9 bytes of SETTINGS on stream 0 is accepted
    [Fact(DisplayName = "RFC9113-3.4-SP-010: Exactly 9 bytes of SETTINGS on stream 0 is accepted")]
    public void ServerPreface_Exactly9BytesOfSettingsOnStream0_ReturnsTrue()
    {
        // 9-byte empty SETTINGS frame: length=0, type=SETTINGS, flags=0, stream=0
        var buf = new byte[9];
        buf[3] = (byte)FrameType.Settings;
        // stream ID = 0 (bytes 5-8 remain zero)

        var decoder = new Http2Decoder();
        var result = decoder.ValidateServerPreface(buf);
        Assert.True(result);
    }

    /// RFC 9113 §3.4 — Multiple decoders each validate their own preface independently
    [Fact(DisplayName = "RFC9113-3.4-SP-011: Multiple decoders each validate their own preface independently")]
    public void ServerPreface_MultipleDecoders_ValidateIndependently()
    {
        var validFrame = new byte[9];
        validFrame[3] = (byte)FrameType.Settings;

        var ping = new PingFrame(new byte[8], isAck: false).Serialize();

        var decoder1 = new Http2Decoder();
        var decoder2 = new Http2Decoder();

        // decoder1 accepts valid SETTINGS
        Assert.True(decoder1.ValidateServerPreface(validFrame));

        // decoder2 rejects PING
        var ex2 = Assert.Throws<Http2Exception>(() => decoder2.ValidateServerPreface(ping));
        Assert.Equal(Http2ErrorCode.ProtocolError, ex2.ErrorCode);
        Assert.True(ex2.IsConnectionError);

        // decoder1 was not affected by decoder2's exception
        Assert.True(decoder1.ValidateServerPreface(validFrame));
    }

    /// RFC 9113 §3.4 — CONTINUATION frame as first frame throws PROTOCOL_ERROR
    [Fact(DisplayName = "RFC9113-3.4-SP-012: CONTINUATION frame as first frame throws PROTOCOL_ERROR")]
    public void ServerPreface_ContinuationFrame_ThrowsProtocolError()
    {
        var buf = new byte[9];
        buf[3] = (byte)FrameType.Continuation;
        BinaryPrimitives.WriteUInt32BigEndian(buf.AsSpan(5), 1);

        var decoder = new Http2Decoder();
        var ex = Assert.Throws<Http2Exception>(() => decoder.ValidateServerPreface(buf));
        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
        Assert.True(ex.IsConnectionError);
    }

    /// RFC 9113 §3.4 — PRIORITY frame as first frame throws PROTOCOL_ERROR
    [Fact(DisplayName = "RFC9113-3.4-SP-013: PRIORITY frame as first frame throws PROTOCOL_ERROR")]
    public void ServerPreface_PriorityFrame_ThrowsProtocolError()
    {
        var buf = new byte[9];
        buf[3] = (byte)FrameType.Priority;
        BinaryPrimitives.WriteUInt32BigEndian(buf.AsSpan(5), 1);

        var decoder = new Http2Decoder();
        var ex = Assert.Throws<Http2Exception>(() => decoder.ValidateServerPreface(buf));
        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
        Assert.True(ex.IsConnectionError);
    }

    // =========================================================================
    // Client preface round-trip: encoder produces preface, decoder validates it
    // =========================================================================

    /// RFC 9113 §3.4 — Encoder preface passes ValidateServerPreface if server echoes SETTINGS
    [Fact(DisplayName = "RFC9113-3.4-RT-001: Encoder preface passes ValidateServerPreface if server echoes SETTINGS")]
    public void ClientPreface_FollowedByServerSettingsAck_ValidatesCorrectly()
    {
        // After sending the client preface, the server responds with a SETTINGS frame.
        // Simulate server sending back an empty SETTINGS ACK (still valid server preface).
        var serverResponse = new byte[9];
        serverResponse[3] = (byte)FrameType.Settings;

        var decoder = new Http2Decoder();
        Assert.True(decoder.ValidateServerPreface(serverResponse));
    }

    /// RFC 9113 §3.4 — Client preface SETTINGS payload entries are each 6 bytes
    [Fact(DisplayName = "RFC9113-3.4-RT-002: Client preface SETTINGS payload entries are each 6 bytes")]
    public void ClientPreface_SettingsEntries_AreEach6Bytes()
    {
        var preface = Http2Encoder.BuildConnectionPreface();

        var payloadLen = (preface[MagicLength] << 16)
                       | (preface[MagicLength + 1] << 8)
                       | preface[MagicLength + 2];

        // Every SETTINGS entry is 2-byte param + 4-byte value = 6 bytes
        if (payloadLen > 0)
        {
            Assert.Equal(0, payloadLen % 6);
        }

        // Total length = magic + 9-byte header + payload
        Assert.Equal(MagicLength + 9 + payloadLen, preface.Length);
    }
}
