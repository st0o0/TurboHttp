using System.Net;
using System.Text;
using TurboHttp.Protocol;
using TurboHttp.Streams;

namespace TurboHttp.StreamTests;

public sealed class Http20EngineTests : EngineTestBase
{
    private static Http20Engine Engine => new();

    private static readonly byte[] ServerSettings = new SettingsFrame([]).Serialize();

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static byte[] BuildH2Response(int streamId, int status, string body = "")
    {
        var hpack = new HpackEncoder(useHuffman: false);
        var headerBlock = hpack.Encode([(":status", status.ToString())]);
        var headersFrame = new HeadersFrame(streamId, headerBlock,
            endStream: body.Length == 0, endHeaders: true).Serialize();

        if (body.Length == 0)
        {
            return headersFrame;
        }

        var bodyBytes = Encoding.UTF8.GetBytes(body);
        var dataFrame = new DataFrame(streamId, bodyBytes, endStream: true).Serialize();
        var combined = new byte[headersFrame.Length + dataFrame.Length];
        headersFrame.CopyTo(combined, 0);
        dataFrame.CopyTo(combined, headersFrame.Length);
        return combined;
    }

    // ── ST-20-001 ──────────────────────────────────────────────────────────────

    [Fact(DisplayName = "ST-20-001: Simple GET returns 200")]
    public async Task Simple_GET_Returns_200()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/")
        {
            Version = HttpVersion.Version20
        };

        var (response, _) = await SendH2Async(Engine.CreateFlow(), request,
            ServerSettings,
            BuildH2Response(streamId: 1, status: 200));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ── ST-20-002 ──────────────────────────────────────────────────────────────

    [Fact(DisplayName = "ST-20-002: Request encodes HPACK pseudo-headers")]
    public async Task Request_Encodes_HPACK_Pseudo_Headers()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/path?q=1")
        {
            Version = HttpVersion.Version20
        };

        var (_, outboundFrames) = await SendH2Async(Engine.CreateFlow(), request,
            ServerSettings,
            BuildH2Response(streamId: 1, status: 200));

        // Outbound (after preface) contains: SETTINGS ACK, request HEADERS.
        var headersFrame = outboundFrames.OfType<HeadersFrame>().FirstOrDefault();
        Assert.NotNull(headersFrame);

        var hpack = new HpackDecoder();
        var decoded = hpack.Decode(headersFrame.HeaderBlockFragment.Span);

        Assert.Contains(decoded, h => h.Name == ":method" && h.Value == "GET");
        Assert.Contains(decoded, h => h.Name == ":path" && h.Value == "/path?q=1");
        Assert.Contains(decoded, h => h.Name == ":scheme" && h.Value == "https");
        Assert.Contains(decoded, h => h.Name == ":authority" && h.Value == "example.com");
    }

    // ── ST-20-003 ──────────────────────────────────────────────────────────────

    [Fact(DisplayName = "ST-20-003: POST with body sends DATA frame after HEADERS")]
    public async Task POST_With_Body_Sends_DATA_Frame_After_HEADERS()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "https://example.com/submit")
        {
            Version = HttpVersion.Version20,
            Content = new StringContent("hello=world", Encoding.UTF8, "application/x-www-form-urlencoded")
        };

        var (response, outboundFrames) = await SendH2Async(Engine.CreateFlow(), request,
            ServerSettings,
            BuildH2Response(streamId: 1, status: 200));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // Outbound (after preface) contains: SETTINGS ACK, request HEADERS, request DATA.
        var headersFrame = outboundFrames.OfType<HeadersFrame>().FirstOrDefault();
        var dataFrame = outboundFrames.OfType<DataFrame>().FirstOrDefault();

        Assert.NotNull(headersFrame);
        Assert.NotNull(dataFrame);
        Assert.False(headersFrame.EndStream, "HEADERS must not have END_STREAM when body follows");

        // HEADERS must appear before DATA in the frame sequence.
        var frames = outboundFrames.ToList();
        Assert.True(frames.IndexOf(headersFrame) < frames.IndexOf(dataFrame),
            "HEADERS frame must precede DATA frame");
    }

    // ── ST-20-004 ──────────────────────────────────────────────────────────────

    [Fact(DisplayName = "ST-20-004: Response with body is decoded")]
    public async Task Response_With_Body_Is_Decoded()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/")
        {
            Version = HttpVersion.Version20
        };

        var (response, _) = await SendH2Async(Engine.CreateFlow(), request,
            ServerSettings,
            BuildH2Response(streamId: 1, status: 200, body: "hello"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal("hello", body);
    }
}
