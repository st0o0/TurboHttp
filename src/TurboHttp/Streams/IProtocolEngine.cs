using System.Net.Http;
using Akka;
using Akka.Streams.Dsl;
using TurboHttp.IO.Stages;

namespace TurboHttp.Streams;

public interface IHttpProtocolEngine
{
    BidiFlow<
        HttpRequestMessage,
        ITransportItem,
        IDataItem, 
        HttpResponseMessage,
        NotUsed> CreateFlow();
}