using System.Net;
using System.Text;
using TurboHttp.Streams;

namespace TurboHttp.StreamTests;

public sealed class Http10EngineTests : EngineTestBase
{
    private static readonly Func<byte[]> Http10Response =
        () => "HTTP/1.0 200 OK\r\nContent-Length: 0\r\n\r\n"u8.ToArray();

    private static readonly Func<byte[]> Http10ResponseWithBody =
        () => "HTTP/1.0 200 OK\r\nContent-Length: 5\r\n\r\nhello"u8.ToArray();

    private static Http10Engine Engine => new();

    [Fact(DisplayName = "ST-10-001: Simple GET returns 200")]
    public async Task Simple_GET_Returns_200()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/")
        {
            Version = HttpVersion.Version10
        };

        var (response, _) = await SendAsync(Engine.CreateFlow(), request, Http10Response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(HttpVersion.Version10, response.Version);
    }

    [Fact(DisplayName = "ST-10-002: Simple GET encodes request line")]
    public async Task Simple_GET_Encodes_Request_Line()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/foo?bar=1")
        {
            Version = HttpVersion.Version10
        };

        var (_, raw) = await SendAsync(Engine.CreateFlow(), request, Http10Response);

        Assert.StartsWith("GET /foo?bar=1 HTTP/1.0\r\n", raw);
    }

    [Fact(DisplayName = "ST-10-003: POST with body encodes Content-Length")]
    public async Task POST_With_Body_Encodes_Content_Length()
    {
        const string body = "hello=world";
        var request = new HttpRequestMessage(HttpMethod.Post, "http://example.com/submit")
        {
            Version = HttpVersion.Version10,
            Content = new StringContent(body, Encoding.UTF8, "application/x-www-form-urlencoded")
        };

        var (response, raw) = await SendAsync(Engine.CreateFlow(), request, Http10Response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains($"Content-Length: {Encoding.UTF8.GetByteCount(body)}", raw);
    }

    [Fact(DisplayName = "ST-10-004: POST without body encodes Content-Length zero")]
    public async Task POST_Without_Body_Encodes_Content_Length_Zero()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "http://example.com/empty")
        {
            Version = HttpVersion.Version10
        };

        var (_, raw) = await SendAsync(Engine.CreateFlow(), request, Http10Response);

        Assert.Contains("Content-Length: 0", raw);
    }

    [Fact(DisplayName = "ST-10-005: Request does not contain Connection header")]
    public async Task Request_Does_Not_Contain_Connection_Header()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/")
        {
            Version = HttpVersion.Version10
        };
        request.Headers.TryAddWithoutValidation("Connection", "keep-alive");

        var (_, raw) = await SendAsync(Engine.CreateFlow(), request, Http10Response);

        Assert.DoesNotContain("Connection:", raw);
    }

    [Fact(DisplayName = "ST-10-006: Request does not contain Host header")]
    public async Task Request_Does_Not_Contain_Host_Header()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/")
        {
            Version = HttpVersion.Version10
        };

        var (_, raw) = await SendAsync(Engine.CreateFlow(), request, Http10Response);

        Assert.DoesNotContain("Host:", raw);
    }

    [Fact(DisplayName = "ST-10-007: Custom header is forwarded")]
    public async Task Custom_Header_Is_Forwarded()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/")
        {
            Version = HttpVersion.Version10
        };
        request.Headers.TryAddWithoutValidation("X-Custom", "test-value");

        var (_, raw) = await SendAsync(Engine.CreateFlow(), request, Http10Response);

        Assert.Contains("X-Custom: test-value", raw);
    }

    [Fact(DisplayName = "ST-10-008: Response with body is decoded")]
    public async Task Response_With_Body_Is_Decoded()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/")
        {
            Version = HttpVersion.Version10
        };

        var (response, _) = await SendAsync(Engine.CreateFlow(), request, Http10ResponseWithBody);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal("hello", body);
    }

    [Fact(DisplayName = "ST-10-009: Response 404 is decoded correctly")]
    public async Task Response_404_Is_Decoded_Correctly()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/missing")
        {
            Version = HttpVersion.Version10
        };

        var (response, _) = await SendAsync(
            Engine.CreateFlow(),
            request,
            () => "HTTP/1.0 404 Not Found\r\nContent-Length: 0\r\n\r\n"u8.ToArray());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact(DisplayName = "ST-10-010: Multiple requests all return 200")]
    public async Task Multiple_Requests_All_Return_200()
    {
        var requests = new[]
        {
            new HttpRequestMessage(HttpMethod.Get, "http://example.com/1") { Version = HttpVersion.Version10 },
            new HttpRequestMessage(HttpMethod.Get, "http://example.com/2") { Version = HttpVersion.Version10 },
            new HttpRequestMessage(HttpMethod.Get, "http://example.com/3") { Version = HttpVersion.Version10 },
        };

        var (responses, _) = await SendManyAsync(Engine.CreateFlow(), requests, Http10Response, 3);

        Assert.Equal(3, responses.Count);
        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        Assert.All(responses, r => Assert.Equal(HttpVersion.Version10, r.Version));
    }
}
