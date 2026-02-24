namespace TurboHttp.Protocol;

public enum HttpDecodeError
{
    NeedMoreData,
    InvalidStatusLine,
    InvalidHeader,
    InvalidContentLength,
    InvalidChunkedEncoding,
    DecompressionFailed,
}