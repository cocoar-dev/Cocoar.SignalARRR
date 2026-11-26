using System;
using System.Text.Json;
using Cocoar.SignalARRR.Common;
using Cocoar.SignalARRR.Server;
using Xunit;

namespace Cocoar.SignalARRR.Tests;

/// <summary>
/// The backplane envelopes are serialized by a source-generated context since 6.0. Nodes of
/// different versions share one cluster during a rolling update, so the format on the wire must
/// not change: these hold the generated contract against the reflection-based Web defaults the
/// backplanes used before, and read an envelope as a 5.x node writes it.
/// </summary>
public class BackplaneWireFormatTests {

    private static readonly JsonSerializerOptions Reflection = new(JsonSerializerDefaults.Web);

    [Fact]
    public void An_envelope_serializes_exactly_as_before() {
        var envelope = new SignalARRRBackplaneEnvelope {
            OriginNodeId = "node-a",
            TargetNodeId = "node-b",
            Kind = SignalARRRBackplaneEnvelopeKind.InvokeRequest,
            HubType = "My.Hub, MyApp",
            ConnectionIds = new[] { "c1" },
            Message = new ServerRequestMessage("IPush|Notify", "hello", 3),
            RequestId = Guid.Parse("6f9619ff-8b86-d011-b42d-00cf4fc964ff"),
            ResultType = "System.String, System.Private.CoreLib",
        };

        // The arguments are the app's types; a node gets their contracts from the hub protocol's
        // resolvers, which reflection stands in for here. The envelope's own contract is the context's.
        var generated = new JsonSerializerOptions(JsonSerializerDefaults.Web) {
            TypeInfoResolver = System.Text.Json.Serialization.Metadata.JsonTypeInfoResolver.Combine(
                SignalARRRBackplaneJsonContext.Default, new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver()),
        };

        Assert.Equal(JsonSerializer.Serialize(envelope, Reflection), JsonSerializer.Serialize(envelope, generated));
    }

    [Fact]
    public void An_envelope_from_a_5x_node_is_read() {
        // ResultType and a HubType with build identity: what a 5.x node sends. A 6.0 node no
        // longer needs ResultType, but must still read the envelope and find its hub.
        var json = """
            {"originNodeId":"old","targetNodeId":"new","kind":1,"hubType":"Cocoar.SignalARRR.Tests.OverloadProbeHub, Cocoar.SignalARRR.Tests, Version=5.2.2.0, Culture=neutral, PublicKeyToken=null","targetKind":3,"connectionIds":["c1"],"message":{"Id":"6f9619ff-8b86-d011-b42d-00cf4fc964ff","Method":"IPush|Notify","Arguments":["hello"],"GenericArguments":[]},"requestId":"6f9619ff-8b86-d011-b42d-00cf4fc964ff","resultType":"System.String, System.Private.CoreLib, Version=8.0.0.0"}
            """;

        var generated = new JsonSerializerOptions(JsonSerializerDefaults.Web) { TypeInfoResolver = SignalARRRBackplaneJsonContext.Default };
        var envelope = JsonSerializer.Deserialize<SignalARRRBackplaneEnvelope>(json, generated)!;

        Assert.Equal("IPush|Notify", envelope.Message!.Method);
        System.Runtime.CompilerServices.RuntimeHelpers.RunModuleConstructor(typeof(OverloadProbeHub).Module.ModuleHandle);
        Assert.Same(typeof(OverloadProbeHub), ServerDispatchRegistry.HubByName(envelope.HubType));
    }

    [Fact]
    public void A_registration_serializes_exactly_as_before() {
        var registration = new SignalARRRConnectionRegistration {
            ConnectionId = "c1",
            NodeId = "node-a",
        };

        var generated = new JsonSerializerOptions(JsonSerializerDefaults.Web) { TypeInfoResolver = SignalARRRBackplaneJsonContext.Default };

        Assert.Equal(JsonSerializer.Serialize(registration, Reflection), JsonSerializer.Serialize(registration, generated));
    }
}
