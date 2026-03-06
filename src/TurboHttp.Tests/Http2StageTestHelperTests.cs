using System.Collections.Generic;
using System.Net;
using TurboHttp.Protocol;

namespace TurboHttp.Tests;

/// <summary>
/// Phase 40 — Unit tests for Http2StageTestHelper.
/// Verifies that the helper methods correctly decode, build, aggregate, and validate
/// HTTP/2 frames according to RFC 9113.
/// </summary>
public sealed class Http2StageTestHelperTests
{
    // ── DecodeFrames ──────────────────────────────────────────────────────────

    [Fact(DisplayName = "40-001: DecodeFrames returns GoAwayFrame for GOAWAY bytes")]
    public void DecodeFrames_ReturnsGoAwayFrame()
    {
        var bytes = Http2Encoder.EncodeGoAway(0, Http2ErrorCode.ProtocolError, "test");

        var frames = Http2StageTestHelper.DecodeFrames(bytes);

        Assert.Single(frames);
        var goAway = Assert.IsType<GoAwayFrame>(frames[0]);
        Assert.Equal(0, goAway.LastStreamId);
        Assert.Equal(Http2ErrorCode.ProtocolError, goAway.ErrorCode);
    }

    [Fact(DisplayName = "40-002: DecodeFrames returns SettingsFrame for SETTINGS bytes")]
    public void DecodeFrames_ReturnsSettingsFrame()
    {
        var bytes = Http2Encoder.EncodeSettings(
            [(SettingsParameter.MaxConcurrentStreams, 100u)]);

        var frames = Http2StageTestHelper.DecodeFrames(bytes);

        Assert.Single(frames);
        var settings = Assert.IsType<SettingsFrame>(frames[0]);
        Assert.Single(settings.Parameters);
        Assert.Equal(SettingsParameter.MaxConcurrentStreams, settings.Parameters[0].Item1);
        Assert.Equal(100u, settings.Parameters[0].Item2);
    }

    [Fact(DisplayName = "40-003: DecodeFrames decodes multiple frames in sequence")]
    public void DecodeFrames_DecodesMultipleFrames()
    {
        var settings = Http2Encoder.EncodeSettings([]);
        var ack = Http2Encoder.EncodeSettingsAck();
        var combined = new byte[settings.Length + ack.Length];
        settings.CopyTo(combined, 0);
        ack.CopyTo(combined, settings.Length);

        var frames = Http2StageTestHelper.DecodeFrames(combined);

        Assert.Equal(2, frames.Count);
        Assert.IsType<SettingsFrame>(frames[0]);
        Assert.IsType<SettingsFrame>(frames[1]);
        var ackFrame = (SettingsFrame)frames[1];
        Assert.True(ackFrame.IsAck);
    }

    [Fact(DisplayName = "40-004: DecodeFrames returns empty list for empty input")]
    public void DecodeFrames_ReturnsEmpty_ForEmptyInput()
    {
        var frames = Http2StageTestHelper.DecodeFrames(System.ReadOnlyMemory<byte>.Empty);

        Assert.Empty(frames);
    }

    // ── BuildHeadersFrame ─────────────────────────────────────────────────────

    [Fact(DisplayName = "40-005: BuildHeadersFrame produces HeadersFrame with correct streamId")]
    public void BuildHeadersFrame_HasCorrectStreamId()
    {
        var headers = new Dictionary<string, string> { [":status"] = "200" };

        var frame = Http2StageTestHelper.BuildHeadersFrame(3, headers);

        Assert.IsType<HeadersFrame>(frame);
        Assert.Equal(3, frame.StreamId);
    }

    [Fact(DisplayName = "40-006: BuildHeadersFrame HPACK block can be decoded back")]
    public void BuildHeadersFrame_BlockIsDecodable()
    {
        var headers = new Dictionary<string, string>
        {
            [":status"] = "200",
            ["content-type"] = "text/plain",
        };

        var frame = Http2StageTestHelper.BuildHeadersFrame(1, headers);

        var hpack = new HpackDecoder();
        var decoded = hpack.Decode(frame.HeaderBlockFragment.Span);

        Assert.Contains(decoded, h => h.Name == ":status" && h.Value == "200");
        Assert.Contains(decoded, h => h.Name == "content-type" && h.Value == "text/plain");
    }

    // ── BuildDataFrame ────────────────────────────────────────────────────────

    [Fact(DisplayName = "40-007: BuildDataFrame produces DataFrame with correct payload")]
    public void BuildDataFrame_HasCorrectPayload()
    {
        var payload = new byte[] { 1, 2, 3, 4, 5 };

        var frame = Http2StageTestHelper.BuildDataFrame(1, payload);

        Assert.IsType<DataFrame>(frame);
        Assert.Equal(1, frame.StreamId);
        Assert.Equal(payload, frame.Data.ToArray());
        Assert.False(frame.EndStream);
    }

    // ── TryBuildResponseFromFrames ────────────────────────────────────────────

    [Fact(DisplayName = "40-008: TryBuildResponseFromFrames extracts status code")]
    public void TryBuildResponseFromFrames_ExtractsStatusCode()
    {
        var headersFrame = Http2StageTestHelper.BuildHeadersFrame(
            1,
            new Dictionary<string, string> { [":status"] = "404" });
        var frames = new List<Http2Frame> { headersFrame };

        var response = Http2StageTestHelper.TryBuildResponseFromFrames(frames);

        Assert.NotNull(response);
        Assert.Equal(HttpStatusCode.NotFound, response!.StatusCode);
    }

    [Fact(DisplayName = "40-009: TryBuildResponseFromFrames returns null when no HEADERS frame")]
    public void TryBuildResponseFromFrames_ReturnsNull_WhenNoHeadersFrame()
    {
        var frames = new List<Http2Frame>
        {
            Http2StageTestHelper.BuildDataFrame(1, new byte[] { 0x42 }),
        };

        var response = Http2StageTestHelper.TryBuildResponseFromFrames(frames);

        Assert.Null(response);
    }

    // ── ValidateFrame ─────────────────────────────────────────────────────────

    [Fact(DisplayName = "40-010: ValidateFrame throws for HEADERS on stream 0")]
    public void ValidateFrame_Throws_ForHeadersOnStream0()
    {
        var frame = Http2StageTestHelper.BuildHeadersFrame(
            0,
            new Dictionary<string, string> { [":status"] = "200" });

        var ex = Assert.Throws<Http2Exception>(() => Http2StageTestHelper.ValidateFrame(frame));
        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
    }

    [Fact(DisplayName = "40-011: ValidateFrame throws for DATA on stream 0")]
    public void ValidateFrame_Throws_ForDataOnStream0()
    {
        var frame = Http2StageTestHelper.BuildDataFrame(0, new byte[] { 0x01 });

        var ex = Assert.Throws<Http2Exception>(() => Http2StageTestHelper.ValidateFrame(frame));
        Assert.Equal(Http2ErrorCode.ProtocolError, ex.ErrorCode);
    }

    [Fact(DisplayName = "40-012: ValidateFrame does not throw for valid HEADERS frame")]
    public void ValidateFrame_DoesNotThrow_ForValidHeadersFrame()
    {
        var frame = Http2StageTestHelper.BuildHeadersFrame(
            1,
            new Dictionary<string, string> { [":status"] = "200" });

        Http2StageTestHelper.ValidateFrame(frame);
    }
}
