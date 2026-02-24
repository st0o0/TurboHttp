using System;

namespace TurboHttp.Protocol;

public sealed class HttpDecoderException(HttpDecodeError error)
    : Exception($"{error}")
{
    public HttpDecodeError DecodeError { get; } = error;
}