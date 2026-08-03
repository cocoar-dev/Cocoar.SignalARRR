using System;
using Cocoar.SignalARRR.Common.Interfaces;
using Cocoar.SignalARRR.Server;
using Cocoar.SignalARRR.Server.ExtensionMethods;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Cocoar.SignalARRR.Tests;

/// <summary>
/// Guards which members the assembly scan turns into remotely invokable endpoints.
/// </summary>
/// <remarks>
/// Regression cover for the case where <c>ServerMethods</c> classes were scanned with a bare
/// <c>GetMethods()</c>: that also returned the inherited members, so the accessors of the
/// <see cref="ServerMethods"/> base properties and the <see cref="object"/> members were registered
/// as endpoints. None of them carry <c>[Authorize]</c>, which made <c>get_ClientContext</c> — it
/// returns the caller's principal, claims, client certificate and remote IP — anonymously callable.
/// </remarks>
public class ServerMethodRegistrationTests {

    private static ISignalARRRMethodsCollection GetMethodsFor<THub>() where THub : HARRR {
        var services = new ServiceCollection();
        services.AddSignalARRR(b => b.AddServerMethodsFrom(typeof(ServerMethodRegistrationTests).Assembly));

        using var provider = services.BuildServiceProvider();
        return provider.GetRequiredKeyedService<ISignalARRRMethodsCollection>(typeof(THub).FullName);
    }

    private static bool IsRegistered(ISignalARRRMethodsCollection methods, string name) {
        try {
            methods.GetMethodInformations(name);
            return true;
        }
        catch (Exception) {
            return false;
        }
    }

    [Theory]
    // The declared methods are the whole point — these must stay reachable.
    [InlineData("RegistrationProbeMethods.Ping", true)]
    [InlineData("RegistrationProbeMethods.Echo", true)]
    // Accessors of the ServerMethods base properties: infrastructure, never endpoints.
    [InlineData("RegistrationProbeMethods.get_ClientContext", false)]
    [InlineData("RegistrationProbeMethods.set_ClientContext", false)]
    [InlineData("RegistrationProbeMethods.get_Context", false)]
    [InlineData("RegistrationProbeMethods.get_Clients", false)]
    [InlineData("RegistrationProbeMethods.get_Groups", false)]
    [InlineData("RegistrationProbeMethods.set_Logger", false)]
    // System.Object members.
    [InlineData("RegistrationProbeMethods.ToString", false)]
    [InlineData("RegistrationProbeMethods.GetHashCode", false)]
    [InlineData("RegistrationProbeMethods.GetType", false)]
    [InlineData("RegistrationProbeMethods.Equals", false)]
    // A property the user declared on their own class is still a property, not an endpoint.
    [InlineData("RegistrationProbeMethods.get_OwnProperty", false)]
    public void ServerMethods_registers_only_declared_methods(string methodName, bool expected) {
        var methods = GetMethodsFor<RegistrationProbeHub>();

        Assert.Equal(expected, IsRegistered(methods, methodName));
    }

    [Theory]
    [InlineData("HubPing", true)]
    // Accessors of a property declared on the user's own hub.
    [InlineData("get_HubProperty", false)]
    [InlineData("set_HubProperty", false)]
    public void Hub_registers_only_declared_methods(string methodName, bool expected) {
        var methods = GetMethodsFor<RegistrationProbeHub>();

        Assert.Equal(expected, IsRegistered(methods, methodName));
    }

    /// <summary>
    /// A ServerMethods class need not derive from <c>ServerMethods&lt;THub&gt;</c> directly.
    /// </summary>
    /// <remarks>
    /// The scan used to read <c>BaseType.GenericTypeArguments[0]</c>, which is empty for a
    /// user-defined intermediate base class — <c>AddSignalARRR</c> then threw
    /// <see cref="IndexOutOfRangeException"/> at startup for the whole application.
    /// </remarks>
    [Theory]
    [InlineData("InheritingProbeMethods.OwnOperation", true)]
    [InlineData("InheritingProbeMethods.SharedOperation", true)]
    [InlineData("InheritingProbeMethods.get_ClientContext", false)]
    // The abstract base is not an endpoint of its own.
    [InlineData("InheritingProbeMethodsBase.SharedOperation", false)]
    public void ServerMethods_supports_an_intermediate_base_class(string methodName, bool expected) {
        var methods = GetMethodsFor<RegistrationProbeHub>();

        Assert.Equal(expected, IsRegistered(methods, methodName));
    }
}

public class RegistrationProbeHub : HARRR {
    public RegistrationProbeHub(IServiceProvider serviceProvider) : base(serviceProvider) { }

    public string HubProperty { get; set; } = string.Empty;

    public string HubPing() => "hub-pong";
}

public class RegistrationProbeMethods : ServerMethods<RegistrationProbeHub> {
    public string OwnProperty { get; set; } = string.Empty;

    public string Ping() => "pong";

    public string Echo(string value) => value;
}

public abstract class InheritingProbeMethodsBase : ServerMethods<RegistrationProbeHub> {
    public string SharedOperation() => "shared";
}

public class InheritingProbeMethods : InheritingProbeMethodsBase {
    public string OwnOperation() => "own";
}
