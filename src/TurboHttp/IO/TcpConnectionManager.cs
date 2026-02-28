using System;
using System.Collections.Generic;
using System.Net.Sockets;
using Akka.Actor;

namespace Servus.Akka.IO;

public sealed class TcpConnectionManagerActor : ReceiveActor
{
    public sealed record OpenConnection(string Host, int Port, IActorRef Handler, int MaxFrameSize = 65536);

    public sealed record ConnectionReady(IActorRef Runner, string Host, int Port);

    public sealed record ConnectionFailed(string Host, int Port);

    private readonly Dictionary<IActorRef, (string Host, int Port)> _runners = new();
    private int _connCounter;

    public TcpConnectionManagerActor()
    {
        Receive<OpenConnection>(HandleOpenConnection);
        Receive<TcpClientRunner.ClientDisconnected>(HandleDisconnected);
    }

    private void HandleOpenConnection(OpenConnection msg)
    {
        var caller = Sender;
        try
        {
            var client = new TcpClient();
            client.Connect(msg.Host, msg.Port);
            var connId = ++_connCounter;
            var runnerName = $"tcp-runner-{msg.Host.Replace(".", "-")}-{msg.Port}-{connId}";
            var runner = Context.ResolveChildActor<TcpClientRunner>(runnerName, client, msg.Handler, msg.MaxFrameSize);
            _runners[runner] = (msg.Host, msg.Port);

            caller.Tell(new ConnectionReady(runner, msg.Host, msg.Port));
        }
        catch (Exception)
        {
            caller.Tell(new ConnectionFailed(msg.Host, msg.Port));
        }
    }

    private void HandleDisconnected(TcpClientRunner.ClientDisconnected msg)
    {
        _runners.Remove(Sender, out _);
    }

    protected override SupervisorStrategy SupervisorStrategy() =>
        new OneForOneStrategy(maxNrOfRetries: 3, withinTimeRange: TimeSpan.FromSeconds(30), decider: new Restart());
}

public class Restart : IDecider
{
    public Directive Decide(Exception cause)
    {
        return Directive.Restart;
    }
}