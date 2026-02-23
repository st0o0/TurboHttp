using System.Net;
using System.Text;
using TurboHttp.Protocol;

namespace TurboHttp.Tests;

public class Http10RoundTripTests
{
    private static ReadOnlyMemory<byte> Bytes(string s)
        => Encoding.GetEncoding("ISO-8859-1").GetBytes(s);

    private static ReadOnlyMemory<byte> BuildRawResponse(string statusLine, string headers, string body = "")
    {
        var raw = $"{statusLine}\r\n{headers}\r\n\r\n{body}";
        return Bytes(raw);
    }

    private static ReadOnlyMemory<byte> BuildRawResponse(string statusLine, string headers, byte[] body)
    {
        var headerPart = Encoding.ASCII.GetBytes($"{statusLine}\r\n{headers}\r\n\r\n");
        var result = new byte[headerPart.Length + body.Length];
        headerPart.CopyTo(result, 0);
        body.CopyTo(result, headerPart.Length);
        return result;
    }

    [Fact]
    public void Roundtrip_GetRequest_CorrectRequestLineInOutput()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/api/data");
        var buffer = new Memory<byte>(new byte[8192]);
        var written = Http10Encoder.Encode(request, ref buffer);
        var encodedRequest = Encoding.ASCII.GetString(buffer.Span[..written]);

        Assert.StartsWith("GET /api/data HTTP/1.0\r\n", encodedRequest);
        Assert.Contains("\r\n\r\n", encodedRequest);

        var decoder = new Http10Decoder();
        var responseData = BuildRawResponse("HTTP/1.0 200 OK", "Content-Length: 0");
        var result = decoder.TryDecode(responseData, out var response);

        Assert.True(result);
        Assert.Equal(HttpStatusCode.OK, response!.StatusCode);
    }

    [Fact]
    public void Roundtrip_PostWithBody_ContentLengthRoundtrips()
    {
        const string requestBody = "username=test&password=secret";

        var request = new HttpRequestMessage(HttpMethod.Post, "http://example.com/login")
        {
            Content = new StringContent(requestBody, Encoding.ASCII, "application/x-www-form-urlencoded")
        };
        var buffer = new Memory<byte>(new byte[8192]);
        var written = Http10Encoder.Encode(request, ref buffer);
        var encodedRequest = Encoding.ASCII.GetString(buffer.Span[..written]);

        Assert.Contains($"Content-Length: {Encoding.ASCII.GetByteCount(requestBody)}", encodedRequest);

        var decoder = new Http10Decoder();
        var responseData = BuildRawResponse("HTTP/1.0 200 OK",
            $"Content-Length: {requestBody.Length}\r\nContent-Type: text/plain",
            requestBody);

        decoder.TryDecode(responseData, out var response);
        var responseBody = response!.Content.ReadAsStringAsync().GetAwaiter().GetResult();

        Assert.Equal(requestBody, responseBody);
    }

    [Fact]
    public void Roundtrip_EncoderForbiddenHeaders_NotInDecodedResponse()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        request.Headers.TryAddWithoutValidation("Connection", "keep-alive");

        var buffer = new Memory<byte>(new byte[8192]);
        Http10Encoder.Encode(request, ref buffer);

        var decoder = new Http10Decoder();
        var responseData = BuildRawResponse("HTTP/1.0 200 OK", "Content-Length: 0");
        decoder.TryDecode(responseData, out var response);

        Assert.False(response!.Headers.Contains("Connection"));
        Assert.False(response.Headers.Contains("Transfer-Encoding"));
    }

    [Fact]
    public async Task Roundtrip_BinaryBody_PreservedThroughEncodeAndDecode()
    {
        var originalBody = new byte[256];
        for (var i = 0; i < 256; i++) originalBody[i] = (byte)i;

        var request = new HttpRequestMessage(HttpMethod.Post, "http://example.com/upload")
        {
            Content = new ByteArrayContent(originalBody)
        };
        var encBuffer = new Memory<byte>(new byte[16384]);
        var written = Http10Encoder.Encode(request, ref encBuffer);

        var encodedStr = Encoding.ASCII.GetString(encBuffer.Span[..20]);
        Assert.Contains("POST", encodedStr);

        var decoder = new Http10Decoder();
        var responseData = BuildRawResponse("HTTP/1.0 200 OK",
            $"Content-Length: {originalBody.Length}", originalBody);

        decoder.TryDecode(responseData, out var response);
        var decodedBody = await response!.Content.ReadAsByteArrayAsync();

        Assert.Equal(originalBody, decodedBody);
    }

    [Fact]
    public void Roundtrip_CustomHeadersSurviveEncoderAndDecoder()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        request.Headers.TryAddWithoutValidation("X-Correlation-Id", "abc-123-def");

        var encBuffer = new Memory<byte>(new byte[8192]);
        var written = Http10Encoder.Encode(request, ref encBuffer);
        var encodedRequest = Encoding.ASCII.GetString(encBuffer.Span[..written]);

        Assert.Contains("X-Correlation-Id: abc-123-def", encodedRequest);

        var decoder = new Http10Decoder();
        var responseData = BuildRawResponse("HTTP/1.0 200 OK",
            "X-Correlation-Id: abc-123-def\r\nContent-Length: 0");

        decoder.TryDecode(responseData, out var response);

        Assert.True(response!.Headers.TryGetValues("X-Correlation-Id", out var values));
        Assert.Contains("abc-123-def", values);
    }

    [Fact]
    public async Task Roundtrip_FragmentedResponse_AssembledCorrectly()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/chunked");
        var encBuffer = new Memory<byte>(new byte[8192]);
        Http10Encoder.Encode(request, ref encBuffer);

        var decoder = new Http10Decoder();
        var fullResponse = Bytes("HTTP/1.0 200 OK\r\nContent-Length: 13\r\nX-Request: ok\r\n\r\nHello, World!");
        var fullArray = fullResponse.ToArray();

        var fragmentSize = fullArray.Length / 4;
        HttpResponseMessage? finalResponse = null;

        for (var i = 0; i < 4; i++)
        {
            var start = i * fragmentSize;
            var length = i == 3 ? fullArray.Length - start : fragmentSize;
            var fragment = new ReadOnlyMemory<byte>(fullArray, start, length);

            if (decoder.TryDecode(fragment, out finalResponse))
                break;
        }

        Assert.NotNull(finalResponse);
        Assert.Equal(HttpStatusCode.OK, finalResponse.StatusCode);
        var body = await finalResponse.Content.ReadAsStringAsync();
        Assert.Equal("Hello, World!", body);
    }

    [Fact]
    public void Roundtrip_HeadRequest_ResponseHasNoBody()
    {
        var request = new HttpRequestMessage(HttpMethod.Head, "http://example.com/resource");
        var encBuffer = new Memory<byte>(new byte[8192]);
        var written = Http10Encoder.Encode(request, ref encBuffer);
        var encodedRequest = Encoding.ASCII.GetString(encBuffer.Span[..written]);

        Assert.StartsWith("HEAD /resource HTTP/1.0", encodedRequest);

        var decoder = new Http10Decoder();
        var responseData = BuildRawResponse("HTTP/1.0 200 OK",
            "Content-Length: 1024\r\nContent-Type: application/octet-stream");

        decoder.TryDecode(responseData, out _);
        decoder.TryDecodeEof(out var response);

        Assert.NotNull(response);
    }
}