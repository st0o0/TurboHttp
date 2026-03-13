using System.Buffers;
using System.Collections.Generic;
using Akka.Actor;
using Akka.Streams;
using Servus.Akka;
using TurboHttp.IO.Stages;

namespace TurboHttp.IO;

public sealed class PoolRouterActor : ReceiveActor
{
    public sealed record RegisterHost(string PoolKey, TcpOptions Options);

    public sealed record GetPoolRefs;

    public sealed record PoolRefs(ISinkRef<ITransportItem> Sink, ISourceRef<IDataItem> Source);

    // TODO TASK-4B-004: replace with SinkRef-based routing
    public sealed record SendRequest(string PoolKey, DataItem Data, IActorRef ReplyTo, System.Version? HttpVersion = null);

    // TODO TASK-4B-004: replace with SourceRef-based response
    public sealed record Response(string PoolKey, IMemoryOwner<byte> Memory, int Length);

    private readonly Dictionary<string, IActorRef> _hosts = new();

    public PoolRouterActor()
    {
        Receive<RegisterHost>(msg =>
        {
            if (_hosts.ContainsKey(msg.PoolKey))
            {
                return;
            }

            var host = Context.ResolveChildActor<HostPoolActor>(msg.PoolKey, msg.Options);
            _hosts[msg.PoolKey] = host;
        });
    }
}
