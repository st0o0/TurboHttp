using System.Collections.Generic;
using System.Collections.Immutable;
using System.Net.Http;

namespace TurboHttp.Protocol;

public sealed class Http2DecodeResult
{
    public ImmutableList<(int StreamId, HttpResponseMessage Response)> Responses { get; }

    public ImmutableList<IReadOnlyList<(SettingsParameter, uint)>> ReceivedSettings { get; }

    public ImmutableList<byte[]> PingRequests { get; }

    public ImmutableList<byte[]> PingAcks { get; }

    public ImmutableList<(int StreamId, int Increment)> WindowUpdates { get; }

    public ImmutableList<(int StreamId, Http2ErrorCode Error)> RstStreams { get; }

    public GoAwayFrame? GoAway { get; }

    public ImmutableList<Http2Frame> ControlFrames { get; }

    public bool HasResponses => !Responses.IsEmpty;
    public bool HasGoAway => GoAway is not null;
    public bool HasNewSettings => !ReceivedSettings.IsEmpty;
    public bool HasPingRequests => !PingRequests.IsEmpty;

    public Http2DecodeResult(
        ImmutableList<(int, HttpResponseMessage)> responses,
        ImmutableList<Http2Frame> controlFrames,
        ImmutableList<IReadOnlyList<(SettingsParameter, uint)>> settings,
        ImmutableList<byte[]> pingAcks,
        ImmutableList<(int, int)> windowUpdates,
        ImmutableList<(int, Http2ErrorCode)> rstStreams,
        GoAwayFrame? goAway)
    {
        Responses = responses;
        ControlFrames = controlFrames;
        ReceivedSettings = settings;
        PingAcks = pingAcks;
        WindowUpdates = windowUpdates;
        RstStreams = rstStreams;
        GoAway = goAway;

        var pings = ImmutableList.CreateBuilder<byte[]>();
        foreach (var f in controlFrames)
        {
            if (f is PingFrame { IsAck: false } p)
            {
                pings.Add(p.Data);
            }
        }

        PingRequests = pings.ToImmutable();
    }

    public static Http2DecodeResult Empty { get; } = new(
        ImmutableList<(int, HttpResponseMessage)>.Empty,
        ImmutableList<Http2Frame>.Empty,
        ImmutableList<IReadOnlyList<(SettingsParameter, uint)>>.Empty,
        ImmutableList<byte[]>.Empty,
        ImmutableList<(int, int)>.Empty,
        ImmutableList<(int, Http2ErrorCode)>.Empty,
        null);
}