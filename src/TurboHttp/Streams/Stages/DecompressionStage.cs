using System;
using System.Net.Http;
using System.Net.Http.Headers;
using Akka.Streams;
using Akka.Streams.Stage;
using TurboHttp.Protocol;

namespace TurboHttp.Streams.Stages;

/// <summary>
/// Decompresses HTTP response bodies according to the Content-Encoding header (RFC 9110 §8.4).
/// Handles gzip, x-gzip, deflate, and br (Brotli) encodings.
/// Responses with no Content-Encoding or "identity" pass through unchanged.
/// After decompression the Content-Encoding header is removed and Content-Length is updated.
/// </summary>
internal sealed class DecompressionStage
    : GraphStage<FlowShape<HttpResponseMessage, HttpResponseMessage>>
{
    private readonly Inlet<HttpResponseMessage> _inlet = new("decompression.in");
    private readonly Outlet<HttpResponseMessage> _outlet = new("decompression.out");

    public override FlowShape<HttpResponseMessage, HttpResponseMessage> Shape { get; }

    public DecompressionStage()
    {
        Shape = new FlowShape<HttpResponseMessage, HttpResponseMessage>(_inlet, _outlet);
    }

    protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        => new Logic(this);

    private sealed class Logic : GraphStageLogic
    {
        private readonly DecompressionStage _stage;

        public Logic(DecompressionStage stage) : base(stage.Shape)
        {
            _stage = stage;

            SetHandler(stage._inlet,
                onPush: () =>
                {
                    var response = Grab(stage._inlet);
                    Push(stage._outlet, Decompress(response));
                },
                onUpstreamFinish: CompleteStage,
                onUpstreamFailure: FailStage);

            SetHandler(stage._outlet,
                onPull: () => Pull(stage._inlet),
                onDownstreamFinish: _ => CompleteStage());
        }

        private static HttpResponseMessage Decompress(HttpResponseMessage response)
        {
            if (!response.Content.Headers.TryGetValues("Content-Encoding", out var values))
            {
                return response;
            }

            var encoding = string.Join(", ", values).Trim();

            if (string.IsNullOrEmpty(encoding) ||
                encoding.Equals(WellKnownHeaders.Identity, StringComparison.OrdinalIgnoreCase))
            {
                return response;
            }

            var body = response.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();
            var decompressed = ContentEncodingDecoder.Decompress(body, encoding);

            var newContent = new ByteArrayContent(decompressed);

            // Copy all content headers except Content-Encoding
            foreach (var header in response.Content.Headers)
            {
                if (header.Key.Equals("Content-Encoding", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                newContent.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            // Update Content-Length to reflect decompressed size
            newContent.Headers.ContentLength = decompressed.Length;

            response.Content = newContent;

            return response;
        }
    }
}
