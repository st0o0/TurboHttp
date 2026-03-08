using System.Buffers;

namespace TurboHttp.StreamTests;

internal sealed class SimpleMemoryOwner : IMemoryOwner<byte>
{
    public Memory<byte> Memory { get; }
    public SimpleMemoryOwner(byte[] data) => Memory = data;

    public void Dispose()
    {
    }
}
