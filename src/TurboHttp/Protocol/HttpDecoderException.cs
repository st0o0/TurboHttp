using System;

namespace TurboMqtt.Protocol;

public sealed class HttpDecoderException(HttpDecodeError error)
    : Exception($"HTTP Decoder Fehler: {error}")
{
    public HttpDecodeError DecodeError { get; } = error;
}