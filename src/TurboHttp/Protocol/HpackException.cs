using System;

namespace TurboHttp.Protocol;

public sealed class HpackException(string message) : Exception(message);