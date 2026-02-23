using System.Net.Http.Headers;
using System.Text;
using TurboHttp.Protocol;

namespace TurboHttp.Tests;

public sealed class Http10EncoderTests
{
    private static Memory<byte> MakeBuffer(int size = 8192) => new byte[size];

    private static string Encode(HttpRequestMessage request, int bufferSize = 8192)
    {
        var buffer = MakeBuffer(bufferSize);
        var written = Http10Encoder.Encode(request, ref buffer);
        return Encoding.ASCII.GetString(buffer.Span[..written]);
    }

    private static (string requestLine, string[] headerLines, byte[] body) ParseRaw(HttpRequestMessage request,
        int bufferSize = 8192)
    {
        var buffer = MakeBuffer(bufferSize);
        var written = Http10Encoder.Encode(request, ref buffer);
        var raw = Encoding.ASCII.GetString(buffer.Span[..written]);

        var separatorIndex = raw.IndexOf("\r\n\r\n", StringComparison.Ordinal);
        var headerSection = raw[..separatorIndex];
        var bodyString = raw[(separatorIndex + 4)..];

        var lines = headerSection.Split("\r\n");
        var requestLine = lines[0];
        var headerLines = lines[1..];

        return (requestLine, headerLines, Encoding.ASCII.GetBytes(bodyString));
    }

    [Fact]
    public void RequestLine_Get_IsCorrectlyFormatted()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/path");
        var (requestLine, _, _) = ParseRaw(request);

        Assert.Equal("GET /path HTTP/1.0", requestLine);
    }

    [Fact]
    public void RequestLine_Head_IsCorrectlyFormatted()
    {
        var request = new HttpRequestMessage(HttpMethod.Head, "http://example.com/resource");
        var (requestLine, _, _) = ParseRaw(request);

        Assert.Equal("HEAD /resource HTTP/1.0", requestLine);
    }

    [Fact]
    public void RequestLine_Post_IsCorrectlyFormatted()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "http://example.com/submit")
        {
            Content = new StringContent("data")
        };
        var (requestLine, _, _) = ParseRaw(request);

        Assert.Equal("POST /submit HTTP/1.0", requestLine);
    }

    [Fact]
    public void RequestLine_ContainsExactlyOneSpaceBetweenParts()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        var (requestLine, _, _) = ParseRaw(request);

        var parts = requestLine.Split(' ');
        Assert.Equal(3, parts.Length);
    }

    [Fact]
    public void RequestLine_ProtocolVersionIsHttp10()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        var (requestLine, _, _) = ParseRaw(request);

        Assert.EndsWith("HTTP/1.0", requestLine);
    }

    [Fact]
    public void RequestLine_EndsWithCrLf()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        var raw = Encode(request);

        Assert.StartsWith("GET / HTTP/1.0\r\n", raw);
    }

    [Fact]
    public void RequestLine_WithQueryString_IncludesQueryInUri()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/search?q=hello&page=2");
        var (requestLine, _, _) = ParseRaw(request);

        Assert.Equal("GET /search?q=hello&page=2 HTTP/1.0", requestLine);
    }

    [Fact]
    public void RequestLine_RootPath_IsForwardSlash()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com");
        var (requestLine, _, _) = ParseRaw(request);

        Assert.Equal("GET / HTTP/1.0", requestLine);
    }

    [Fact]
    public void RequestLine_DeepPath_IsPreserved()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/a/b/c/d");
        var (requestLine, _, _) = ParseRaw(request);

        Assert.Equal("GET /a/b/c/d HTTP/1.0", requestLine);
    }

    [Fact]
    public void Headers_HostHeader_IsRemovedForHttp10()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        var (_, headerLines, _) = ParseRaw(request);

        Assert.DoesNotContain(headerLines, h => h.StartsWith("Host:", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Headers_ConnectionHeader_IsRemoved()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        request.Headers.TryAddWithoutValidation("Connection", "keep-alive");

        var (_, headerLines, _) = ParseRaw(request);

        Assert.DoesNotContain(headerLines, h => h.StartsWith("Connection:", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Headers_KeepAliveHeader_IsRemoved()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        request.Headers.TryAddWithoutValidation("Keep-Alive", "timeout=5");

        var (_, headerLines, _) = ParseRaw(request);

        Assert.DoesNotContain(headerLines, h => h.StartsWith("Keep-Alive:", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Headers_TransferEncodingHeader_IsRemoved()
    {
        // Transfer-Encoding ist HTTP/1.1 (RFC 2616 §14.41)
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        request.Headers.TryAddWithoutValidation("Transfer-Encoding", "chunked");

        var (_, headerLines, _) = ParseRaw(request);

        Assert.DoesNotContain(headerLines, h => h.StartsWith("Transfer-Encoding:", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Headers_CustomHeader_IsPreserved()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        request.Headers.TryAddWithoutValidation("X-Custom-Header", "my-value");

        var (_, headerLines, _) = ParseRaw(request);

        Assert.Contains(headerLines, h => h == "X-Custom-Header: my-value");
    }

    [Fact]
    public void Headers_MultipleCustomHeaders_AllPreserved()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        request.Headers.TryAddWithoutValidation("X-Header-A", "value-a");
        request.Headers.TryAddWithoutValidation("X-Header-B", "value-b");

        var (_, headerLines, _) = ParseRaw(request);

        Assert.Contains(headerLines, h => h == "X-Header-A: value-a");
        Assert.Contains(headerLines, h => h == "X-Header-B: value-b");
    }

    [Fact]
    public void Headers_HeaderFormat_IsNameColonSpaceValue()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        request.Headers.TryAddWithoutValidation("X-Test", "test-value");

        var (_, headerLines, _) = ParseRaw(request);

        var header = headerLines.Single(h => h.StartsWith("X-Test:"));
        Assert.Equal("X-Test: test-value", header);
    }

    [Fact]
    public void Headers_EachHeaderEndsWithCrLf()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        request.Headers.TryAddWithoutValidation("X-Test", "value");

        var raw = Encode(request);

        var headerSection = raw[..raw.IndexOf("\r\n\r\n", StringComparison.Ordinal)];
        foreach (var line in headerSection.Split("\r\n").Skip(1))
        {
            Assert.Contains(line + "\r\n", raw);
        }
    }

    [Fact]
    public void Headers_MultiValueHeader_EachValueOnSeparateLine()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/html"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        var (_, headerLines, _) = ParseRaw(request);

        var acceptLines = headerLines.Where(h => h.StartsWith("Accept:", StringComparison.OrdinalIgnoreCase)).ToArray();
        Assert.Equal(2, acceptLines.Length);
    }

    [Fact]
    public void Headers_AcceptHeader_IsPreserved()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/html"));

        var (_, headerLines, _) = ParseRaw(request);

        Assert.Contains(headerLines, h => h.StartsWith("Accept:", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Headers_RequestWithNoCustomHeaders_OnlyContainsRfcMandatoryHeaders()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        var (_, headerLines, _) = ParseRaw(request);

        Assert.DoesNotContain(headerLines, h => h.StartsWith("Host:", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(headerLines, h => h.StartsWith("Connection:", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(headerLines, h => h.StartsWith("Transfer-Encoding:", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Headers_HeaderSeparator_IsDoubleCrLf()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        var raw = Encode(request);

        Assert.Contains("\r\n\r\n", raw);
    }

    [Fact]
    public void Body_PostWithBody_ContentLengthIsCorrect()
    {
        const string bodyText = "Hello, World!";
        var request = new HttpRequestMessage(HttpMethod.Post, "http://example.com/")
        {
            Content = new StringContent(bodyText, Encoding.ASCII)
        };

        var (_, headerLines, _) = ParseRaw(request);

        var contentLength = headerLines
            .Single(h => h.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase));
        Assert.Equal($"Content-Length: {Encoding.ASCII.GetByteCount(bodyText)}", contentLength);
    }

    [Fact]
    public void Body_PostWithBody_BodyIsCorrectlyWritten()
    {
        const string bodyText = "Hello, World!";
        var request = new HttpRequestMessage(HttpMethod.Post, "http://example.com/")
        {
            Content = new StringContent(bodyText, Encoding.ASCII, "text/plain")
        };

        var (_, _, body) = ParseRaw(request);

        Assert.Equal(bodyText, Encoding.ASCII.GetString(body));
    }

    [Fact]
    public void Body_GetWithNoBody_ContentLengthAbsent()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");

        var (_, headerLines, _) = ParseRaw(request);

        Assert.DoesNotContain(headerLines, h => h.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Body_GetWithNoBody_ContentTypeAbsent()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");

        var (_, headerLines, _) = ParseRaw(request);

        Assert.DoesNotContain(headerLines, h => h.StartsWith("Content-Type:", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Body_PostWithBinaryBody_BytesExactlyPreserved()
    {
        var bodyBytes = new byte[] { 0x00, 0x01, 0x02, 0xFF, 0xFE, 0x7F };
        var request = new HttpRequestMessage(HttpMethod.Post, "http://example.com/")
        {
            Content = new ByteArrayContent(bodyBytes)
        };

        var buffer = MakeBuffer(8192);
        var written = Http10Encoder.Encode(request, ref buffer);
        var raw = buffer.Span[..written];

        var separator = "\r\n\r\n"u8.ToArray();
        var sepIndex = FindSequence(raw, separator);
        var actualBody = raw[(sepIndex + 4)..].ToArray();

        Assert.Equal(bodyBytes, actualBody);
    }

    [Fact]
    public void Body_PostWithEmptyBody_ContentLengthIsZeroOrAbsent()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "http://example.com/")
        {
            Content = new ByteArrayContent([])
        };

        var (_, headerLines, body) = ParseRaw(request);

        Assert.DoesNotContain(headerLines, h => h.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase));
        Assert.Empty(body);
    }

    [Fact]
    public void Body_PostWithLargeBody_ContentLengthMatchesBodySize()
    {
        var largeBody = new byte[4096];
        new Random(42).NextBytes(largeBody);

        var request = new HttpRequestMessage(HttpMethod.Post, "http://example.com/")
        {
            Content = new ByteArrayContent(largeBody)
        };

        var buffer = MakeBuffer(16384);
        var written = Http10Encoder.Encode(request, ref buffer);
        var raw = Encoding.ASCII.GetString(buffer.Span[..written]);

        var headerSection = raw[..raw.IndexOf("\r\n\r\n", StringComparison.Ordinal)];
        var contentLengthLine = headerSection.Split("\r\n")
            .Single(h => h.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase));

        var reportedLength = int.Parse(contentLengthLine.Split(": ")[1]);
        Assert.Equal(4096, reportedLength);
    }

    [Fact]
    public void Body_PostWithBody_BodyAppearsAfterHeaderSeparator()
    {
        const string bodyText = "BODY_CONTENT";
        var request = new HttpRequestMessage(HttpMethod.Post, "http://example.com/")
        {
            Content = new StringContent(bodyText, Encoding.ASCII, "text/plain")
        };

        var raw = Encode(request);
        var separatorIndex = raw.IndexOf("\r\n\r\n", StringComparison.Ordinal);
        var bodyPart = raw[(separatorIndex + 4)..];

        Assert.Equal(bodyText, bodyPart);
    }

    [Theory]
    [InlineData("GET")]
    [InlineData("HEAD")]
    [InlineData("POST")]
    public void Method_ValidMethods_DoNotThrow(string method)
    {
        var request = new HttpRequestMessage(new HttpMethod(method), "http://example.com/");
        if (method == "POST")
            request.Content = new StringContent("x");

        var buffer = MakeBuffer();
        var ex = Record.Exception(() => Http10Encoder.Encode(request, ref buffer));

        Assert.Null(ex);
    }

    [Fact]
    public void HeaderInjection_CrInValue_ThrowsArgumentException()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        request.Headers.TryAddWithoutValidation("X-Evil", "value\rX-Injected: attack");

        var buffer = MakeBuffer();

        Assert.Throws<ArgumentException>(() =>
            Http10Encoder.Encode(request, ref buffer));
    }

    [Fact]
    public void HeaderInjection_LfInValue_ThrowsArgumentException()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        request.Headers.TryAddWithoutValidation("X-Evil", "value\nX-Injected: attack");

        var buffer = MakeBuffer();

        Assert.Throws<ArgumentException>(() =>
            Http10Encoder.Encode(request, ref buffer));
    }

    [Fact]
    public void HeaderInjection_CrLfInValue_ThrowsArgumentException()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        request.Headers.TryAddWithoutValidation("X-Evil", "value\r\nX-Injected: attack");

        var buffer = MakeBuffer();

        Assert.Throws<ArgumentException>(() =>
            Http10Encoder.Encode(request, ref buffer));
    }

    [Fact]
    public void HeaderInjection_Exception_ContainsHeaderName()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        request.Headers.TryAddWithoutValidation("X-Dangerous", "bad\r\nvalue");

        var buffer = MakeBuffer();

        var ex = Assert.Throws<ArgumentException>(() =>
            Http10Encoder.Encode(request, ref buffer));

        Assert.Contains("X-Dangerous", ex.Message);
    }

    [Fact]
    public void HeaderInjection_NormalValue_DoesNotThrow()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        request.Headers.TryAddWithoutValidation("X-Safe", "perfectly-normal-value-123");

        var buffer = MakeBuffer();
        var ex = Record.Exception(() => Http10Encoder.Encode(request, ref buffer));

        Assert.Null(ex);
    }

    [Fact]
    public void BufferOverflow_BufferTooSmallForHeaders_ThrowsInvalidOperationException()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        var buffer = MakeBuffer(5);

        Assert.Throws<InvalidOperationException>(() =>
            Http10Encoder.Encode(request, ref buffer));
    }

    [Fact]
    public void BufferOverflow_BufferTooSmallForBody_ThrowsInvalidOperationException()
    {
        var largeBody = new byte[1000];
        var request = new HttpRequestMessage(HttpMethod.Post, "http://example.com/")
        {
            Content = new ByteArrayContent(largeBody)
        };

        var buffer = MakeBuffer(100);

        Assert.Throws<InvalidOperationException>(() =>
            Http10Encoder.Encode(request, ref buffer));
    }

    [Fact]
    public void BufferOverflow_ExactSizeBuffer_DoesNotThrow()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");

        var measureBuffer = MakeBuffer();
        var needed = Http10Encoder.Encode(request, ref measureBuffer);

        var exactBuffer = MakeBuffer(needed);
        var ex = Record.Exception(() => Http10Encoder.Encode(request, ref exactBuffer));

        Assert.Null(ex);
    }

    [Fact]
    public void BufferOverflow_EmptyBuffer_ThrowsInvalidOperationException()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        var buffer = MakeBuffer(0);

        Assert.Throws<InvalidOperationException>(() =>
            Http10Encoder.Encode(request, ref buffer));
    }

    [Fact]
    public void Uri_NonAsciiPath_IsPercentEncoded()
    {
        var request = new HttpRequestMessage(HttpMethod.Get,
            new Uri("http://example.com/pfad/mit/%C3%BCmlauten"));
        var (requestLine, _, _) = ParseRaw(request);

        Assert.Contains("%C3%BC", requestLine);
    }

    [Fact]
    public void Uri_SpaceInPath_IsPercentEncoded()
    {
        var request = new HttpRequestMessage(HttpMethod.Get,
            new Uri("http://example.com/path%20with%20spaces"));
        var (requestLine, _, _) = ParseRaw(request);

        Assert.Contains("%20", requestLine);
        var uri = requestLine.Split(' ')[1];
        Assert.DoesNotContain(" ", uri);
    }

    [Fact]
    public void Uri_QueryStringWithSpecialChars_IsPreserved()
    {
        var request = new HttpRequestMessage(HttpMethod.Get,
            "http://example.com/search?q=hello+world&lang=de");
        var (requestLine, _, _) = ParseRaw(request);

        Assert.Contains("?q=hello+world&lang=de", requestLine);
    }

    [Fact]
    public void Uri_EmptyPath_NormalizesToSlash()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com");
        var (requestLine, _, _) = ParseRaw(request);

        Assert.StartsWith("GET /", requestLine);
    }

    [Fact]
    public void Uri_PathWithFragment_FragmentIsNotIncluded()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/page");
        var (requestLine, _, _) = ParseRaw(request);

        Assert.DoesNotContain("#", requestLine);
    }

    [Fact]
    public void Uri_NonStandardPort_IsNotInRequestLine()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com:8080/api");
        var (requestLine, _, _) = ParseRaw(request);

        Assert.Equal("GET /api HTTP/1.0", requestLine);
    }

    [Fact]
    public void ContentType_WhenSetExplicitly_IsPreserved()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "http://example.com/")
        {
            Content = new StringContent("data", Encoding.ASCII, "application/json")
        };

        var (_, headerLines, _) = ParseRaw(request);

        Assert.Contains(headerLines, h => h.StartsWith("Content-Type: application/json",
            StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ContentType_WithoutBody_IsNotSet()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");

        var (_, headerLines, _) = ParseRaw(request);

        Assert.DoesNotContain(headerLines, h => h.StartsWith("Content-Type:",
            StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ContentType_NoDefaultIsInjected_WhenMissingAndBodyExists()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "http://example.com/")
        {
            Content = new ByteArrayContent([1, 2, 3])
        };

        var (_, headerLines, _) = ParseRaw(request);

        var contentTypeLines = headerLines
            .Where(h => h.StartsWith("Content-Type:", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        Assert.Empty(contentTypeLines);
    }

    [Fact]
    public void BytesWritten_MatchesActualEncodedLength()
    {
        const string body = "test body content";
        var request = new HttpRequestMessage(HttpMethod.Post, "http://example.com/submit")
        {
            Content = new StringContent(body, Encoding.ASCII, "text/plain")
        };

        var buffer = MakeBuffer();
        var written = Http10Encoder.Encode(request, ref buffer);
        var raw = Encoding.ASCII.GetString(buffer.Span[..written]);

        Assert.Equal(written, Encoding.ASCII.GetByteCount(raw));
    }

    [Fact]
    public void BytesWritten_IsGreaterThanZero()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        var buffer = MakeBuffer();

        var written = Http10Encoder.Encode(request, ref buffer);

        Assert.True(written > 0);
    }

    [Fact]
    public void BytesWritten_WithBody_IsLargerThanWithout()
    {
        var requestWithoutBody = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        var requestWithBody = new HttpRequestMessage(HttpMethod.Post, "http://example.com/")
        {
            Content = new StringContent("body content", Encoding.ASCII, "text/plain")
        };

        var buf1 = MakeBuffer();
        var buf2 = MakeBuffer();

        var writtenWithout = Http10Encoder.Encode(request: requestWithoutBody, buffer: ref buf1);
        var writtenWith = Http10Encoder.Encode(request: requestWithBody, buffer: ref buf2);

        Assert.True(writtenWith > writtenWithout);
    }

    [Fact]
    public void BytesWritten_BufferBeyondWrittenBytes_IsUntouched()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        var buffer = MakeBuffer(8192);
        buffer.Span[8191] = 0xAB;

        var written = Http10Encoder.Encode(request, ref buffer);

        Assert.Equal(0xAB, buffer.Span[8191]);
        Assert.True(written < 8191);
    }

    [Fact]
    public void Idempotent_SameRequestEncodedTwice_ProducesIdenticalOutput()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "http://example.com/api")
        {
            Content = new StringContent("payload", Encoding.ASCII, "text/plain")
        };
        request.Headers.TryAddWithoutValidation("X-Request-Id", "abc123");

        var buffer1 = MakeBuffer();
        var written1 = Http10Encoder.Encode(request, ref buffer1);
        var result1 = buffer1.Span[..written1].ToArray();

        var buffer2 = MakeBuffer();
        var written2 = Http10Encoder.Encode(request, ref buffer2);
        var result2 = buffer2.Span[..written2].ToArray();

        Assert.Equal(result1, result2);
    }

    [Fact]
    public void Idempotent_SameGetRequestEncodedTwice_ProducesIdenticalOutput()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/resource?id=42");
        request.Headers.TryAddWithoutValidation("Accept", "application/json");

        var buf1 = MakeBuffer();
        var buf2 = MakeBuffer();

        var w1 = Http10Encoder.Encode(request, ref buf1);
        var w2 = Http10Encoder.Encode(request, ref buf2);

        Assert.Equal(buf1.Span[..w1].ToArray(), buf2.Span[..w2].ToArray());
    }

    [Fact]
    public void Integration_MinimalGetRequest_IsFullyRfc1945Compliant()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/index.html");
        var raw = Encode(request);

        Assert.StartsWith("GET /index.html HTTP/1.0\r\n", raw);
        Assert.Contains("\r\n\r\n", raw);
        Assert.DoesNotContain("Host:", raw);
        Assert.DoesNotContain("Connection:", raw);
        Assert.DoesNotContain("Transfer-Encoding:", raw);
    }

    [Fact]
    public void Integration_PostWithJsonBody_IsFullyRfc1945Compliant()
    {
        const string json = "{\"key\":\"value\"}";
        var request = new HttpRequestMessage(HttpMethod.Post, "http://api.example.com/resource")
        {
            Content = new StringContent(json, Encoding.ASCII, "application/json")
        };

        var raw = Encode(request);

        Assert.StartsWith("POST /resource HTTP/1.0\r\n", raw);
        Assert.Contains($"Content-Length: {Encoding.ASCII.GetByteCount(json)}", raw);
        Assert.Contains("Content-Type: application/json", raw);
        Assert.EndsWith(json, raw);
        Assert.DoesNotContain("Host:", raw);
        Assert.DoesNotContain("Transfer-Encoding:", raw);
    }

    [Fact]
    public void Integration_HeadRequest_HasNoBody()
    {
        var request = new HttpRequestMessage(HttpMethod.Head, "http://example.com/resource");
        var raw = Encode(request);

        var separatorIndex = raw.IndexOf("\r\n\r\n", StringComparison.Ordinal);
        var body = raw[(separatorIndex + 4)..];

        Assert.Empty(body);
        Assert.DoesNotContain("Content-Length:", raw);
    }

    [Fact]
    public void Integration_RequestWithMultipleHeaders_AllWrittenCorrectly()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/data");
        request.Headers.TryAddWithoutValidation("X-Api-Key", "secret-123");
        request.Headers.TryAddWithoutValidation("X-Request-Id", "req-456");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        var (_, headerLines, _) = ParseRaw(request);

        Assert.Contains(headerLines, h =>
            h.StartsWith("X-Api-Key:", StringComparison.OrdinalIgnoreCase) &&
            h.EndsWith("secret-123"));

        Assert.Contains(headerLines, h =>
            h.StartsWith("X-Request-Id:", StringComparison.OrdinalIgnoreCase) &&
            h.EndsWith("req-456"));

        Assert.Contains(headerLines, h =>
            h.StartsWith("Accept:", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Integration_ContentHeadersMergedWithRequestHeaders()
    {
        var content = new StringContent("body", Encoding.ASCII, "text/plain");
        var request = new HttpRequestMessage(HttpMethod.Post, "http://example.com/")
        {
            Content = content
        };
        request.Headers.TryAddWithoutValidation("X-Custom", "value");

        var (_, headerLines, _) = ParseRaw(request);

        Assert.Contains(headerLines, h => h == "X-Custom: value");
        Assert.Contains(headerLines, h => h.StartsWith("Content-Type:", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(headerLines, h => h.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase));
    }

    private static int FindSequence(ReadOnlySpan<byte> haystack, ReadOnlySpan<byte> needle)
    {
        for (var i = 0; i <= haystack.Length - needle.Length; i++)
        {
            if (haystack.Slice(i, needle.Length).SequenceEqual(needle))
            {
                return i;
            }
        }

        return -1;
    }
}