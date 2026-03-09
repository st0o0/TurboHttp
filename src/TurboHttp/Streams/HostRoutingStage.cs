using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Sockets;
using Akka.Streams;
using Akka.Streams.Stage;
using TurboHttp.IO;

namespace TurboHttp.Streams;

public sealed class HostRoutingStage : GraphStage<FlowShape<HttpRequestMessage, HttpResponseMessage>>
    {
        private readonly Inlet<HttpRequestMessage> _inlet = new("host.pool.in");
        private readonly Outlet<HttpResponseMessage> _outlet = new("host.pool.out");

        public override FlowShape<HttpRequestMessage, HttpResponseMessage> Shape { get; }

        public HostRoutingStage()
        {
            Shape = new FlowShape<HttpRequestMessage, HttpResponseMessage>(_inlet, _outlet);
        }

        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
            => new Logic(this);

        private sealed class Logic : GraphStageLogic
        {
            private readonly HostRoutingStage _stage;
            private readonly Dictionary<string, HostConnectionPool> _pools = new();
            private readonly Queue<HttpResponseMessage> _responseBuffer = new();
            private bool _downstreamWaiting;

            private Action<HttpResponseMessage>? _onResponse;

            public Logic(HostRoutingStage stage) : base(stage.Shape)
            {
                _stage = stage;

                SetHandler(stage._inlet,
                    onPush: () =>
                    {
                        var request = Grab(stage._inlet);
                        var uri = request.RequestUri!;
                        int port;
                        if (uri.Port is -1)
                        {
                            port = uri.Scheme == "https" ? 443 : 80;
                        }
                        else
                        {
                            port = uri.Port;
                        }

                        var pool = GetOrCreatePool(new TcpOptions
                        {
                            Host = uri.Host,
                            Port = port,
                            AddressFamily = uri.HostNameType switch
                            {
                                UriHostNameType.IPv4 => AddressFamily.InterNetwork,
                                UriHostNameType.IPv6 => AddressFamily.InterNetworkV6,
                                _ => AddressFamily.Unspecified
                            }
                        });

                        pool.Send(request);
                        Pull(stage._inlet);
                    });

                SetHandler(stage._outlet,
                    onPull: () =>
                    {
                        if (_responseBuffer.TryDequeue(out var response))
                        {
                            Push(stage._outlet, response);
                        }
                        else
                        {
                            _downstreamWaiting = true;
                        }
                    });
            }

            public override void PreStart()
            {
                _onResponse = GetAsyncCallback<HttpResponseMessage>(response =>
                {
                    if (_downstreamWaiting)
                    {
                        _downstreamWaiting = false;
                        Push(_stage._outlet, response);
                    }
                    else
                    {
                        _responseBuffer.Enqueue(response);
                    }
                });

                Pull(_stage._inlet);
            }

            private HostConnectionPool GetOrCreatePool(TcpOptions options)
            {
                var host = options.Host;
                var port = options.Port;

                var key = $"{host}:{port}";

                if (_pools.TryGetValue(key, out var pool))
                {
                    return pool;
                }

                var system = (Materializer as ActorMaterializer)!.System;

                var newPool = new HostConnectionPool(options, system, _onResponse!);

                _pools[key] = newPool;
                return newPool;
            }
        }
    }