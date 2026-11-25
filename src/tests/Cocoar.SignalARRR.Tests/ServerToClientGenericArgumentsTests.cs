using System;
using Cocoar.SignalARRR.ProxyGenerator;
using Cocoar.SignalARRR.Server;
using TestShared;
using Xunit;

namespace Cocoar.SignalARRR.Tests;

/// <summary>
/// No client resolves generic arguments any more, so the server refuses to send a call to a
/// generic client method instead of letting it fail on the client.
/// </summary>
public class ServerToClientGenericArgumentsTests {

    [Fact]
    public void ACallToAGenericClientMethod_IsRefusedBeforeItIsSent() {
        var proxy = ProxyCreator.CreateInstanceFromInterface<IGeneric>(new CapturingProxyCreatorHelper());

        var error = Assert.Throws<NotSupportedException>(() => proxy.Invoke<int>("command"));

        Assert.Contains("generic", error.Message);
        Assert.Contains("Invoke", error.Message);
    }

    [Fact]
    public void NonGenericCalls_PassUnchanged() {
        ServerToClientGenericArguments.Reject("TestShared.IGeneric|Plain", Array.Empty<string>());
        ServerToClientGenericArguments.Reject("TestShared.IGeneric|Plain", null);
    }
}
