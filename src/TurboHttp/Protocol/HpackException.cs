using System;

namespace TurboMqtt.Protocol;

public sealed class HpackException(string message) : Exception(message);