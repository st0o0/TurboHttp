using System;

namespace TurboHttp.Protocol;

public sealed class HttpDecoderException(HttpDecodeError error)
    : Exception($"HTTP Decoder Fehler: {error}")
{
    public HttpDecodeError DecodeError { get; } = error;
}