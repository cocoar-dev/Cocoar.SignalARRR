using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using Cocoar.SignalARRR.Common.RemoteReferenceTypes;

namespace Cocoar.SignalARRR.Common.Serialization {
    /// <summary>
    /// The JSON contracts of SignalARRR's own wire types: the message envelopes, the remote
    /// references and the error envelope, plus the primitives they carry.
    /// </summary>
    /// <remarks>
    /// Source-generated so the envelopes serialize without reflection, which NativeAOT apps (and
    /// apps that switch reflection-based JSON off) do not have. SignalARRR adds it to the hub
    /// protocol's options itself, behind whatever the app configured (<see cref="SignalARRRJson.AddTo"/>);
    /// the types of an app's own arguments and results stay in the app's context.
    /// </remarks>
    [JsonSerializable(typeof(ClientRequestMessage))]
    [JsonSerializable(typeof(ServerRequestMessage))]
    [JsonSerializable(typeof(StreamReference))]
    [JsonSerializable(typeof(CancellationTokenReference))]
    [JsonSerializable(typeof(HARRRError))]
    [JsonSerializable(typeof(object))]
    [JsonSerializable(typeof(object[]))]
    [JsonSerializable(typeof(JsonElement))]
    [JsonSerializable(typeof(string))]
    [JsonSerializable(typeof(string[]))]
    [JsonSerializable(typeof(bool))]
    [JsonSerializable(typeof(int))]
    [JsonSerializable(typeof(long))]
    [JsonSerializable(typeof(double))]
    [JsonSerializable(typeof(Guid))]
    [JsonSerializable(typeof(Guid?))]
    [JsonSerializable(typeof(DateTime))]
    [JsonSerializable(typeof(DateTimeOffset))]
    [JsonSerializable(typeof(Dictionary<string, object>))]
    public partial class SignalARRRJsonContext : JsonSerializerContext {
    }
}
