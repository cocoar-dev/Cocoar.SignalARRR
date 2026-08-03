using System;
using System.Linq;
using System.Reflection;
using Cocoar.SignalARRR.Common;
using Cocoar.SignalARRR.Server;
using Cocoar.SignalARRR.Server.ExtensionMethods;
using Microsoft.AspNetCore.Authorization;
using Xunit;

namespace Cocoar.SignalARRR.Tests;

/// <summary>
/// Covers which <c>[Authorize]</c> metadata the dispatcher sees for a given method.
/// </summary>
/// <remarks>
/// Every case here previously resolved to an empty result, and an empty result means "allow" —
/// so each one was a way to reach a method that its author had marked as protected.
/// </remarks>
public class AuthorizationResolutionTests {

    private static MethodInfo MethodOn<T>(string name) =>
        typeof(T).GetMethod(name, BindingFlags.Public | BindingFlags.Instance)
        ?? throw new InvalidOperationException($"'{name}' not found on {typeof(T).Name}.");

    // ---- S-2: resolution via ReflectedType, not DeclaringType -------------------------------

    [Fact]
    public void Method_inherited_from_undecorated_base_is_protected_by_the_derived_class() {
        // Delete is declared on the undecorated base; it is registered from the [Authorize]d
        // derived class, so DeclaringType pointed at the base and found nothing.
        var method = MethodOn<AuthDerivedMethods>(nameof(AuthDerivedMethods.Delete));

        var plan = method.GetAuthorizationPlan();

        Assert.True(plan.RequiresAuthorization);
        Assert.Contains(plan.AuthorizeData, d => d.Policy == "admin");
    }

    [Fact]
    public void Method_level_attribute_still_wins_over_the_class() {
        var method = MethodOn<AuthDerivedMethods>(nameof(AuthDerivedMethods.Escalate));

        var plan = method.GetAuthorizationPlan();

        Assert.Contains(plan.AuthorizeData, d => d.Policy == "superuser");
        Assert.DoesNotContain(plan.AuthorizeData, d => d.Policy == "admin");
    }

    // ---- S-3: the implementation counts, not only the contract ------------------------------

    [Fact]
    public void Attribute_on_the_implementation_is_honoured() {
        var method = MethodOn<AuthImplementation>(nameof(AuthImplementation.ProtectedByImplementation));

        var plan = method.GetAuthorizationPlan();

        Assert.True(plan.RequiresAuthorization);
        Assert.Contains(plan.AuthorizeData, d => d.Policy == "impl");
    }

    [Fact]
    public void Attribute_on_the_contract_is_still_honoured() {
        var method = MethodOn<AuthImplementation>(nameof(AuthImplementation.ProtectedByContract));

        var plan = method.GetAuthorizationPlan();

        Assert.True(plan.RequiresAuthorization);
        Assert.Contains(plan.AuthorizeData, d => d.Policy == "contract");
    }

    [Fact]
    public void Interface_dispatch_resolves_to_the_implementation_method() {
        // The cache used to store the interface declaration, so the implementation's attributes
        // were invisible to authorization even though the implementation is what runs.
        var collection = new SignalARRRInterfaceCollection();
        collection.RegisterInterface(typeof(IAuthContract), typeof(AuthImplementation));

        var (_, methodInfo) = collection.GetInvokeInformation($"{typeof(IAuthContract).FullName}|{nameof(IAuthContract.ProtectedByImplementation)}");

        Assert.Equal(typeof(AuthImplementation), methodInfo.DeclaringType);
        Assert.Contains(methodInfo.GetAuthorizationPlan().AuthorizeData, d => d.Policy == "impl");
    }

    // ---- S-4: IAuthorizeData, not just AuthorizeAttribute -----------------------------------

    [Fact]
    public void Custom_IAuthorizeData_attribute_is_collected() {
        var method = MethodOn<AuthCustomAttributeMethods>(nameof(AuthCustomAttributeMethods.Protected));

        var plan = method.GetAuthorizationPlan();

        Assert.True(plan.RequiresAuthorization);
        Assert.Contains(plan.AuthorizeData, d => d.Policy == "custom");
    }

    // ---- AllowAnonymous --------------------------------------------------------------------

    [Fact]
    public void Class_level_AllowAnonymous_is_honoured() {
        var method = MethodOn<AuthAnonymousMethods>(nameof(AuthAnonymousMethods.Open));

        var plan = method.GetAuthorizationPlan();

        Assert.True(plan.AllowAnonymous);
        Assert.False(plan.RequiresAuthorization);
    }

    [Fact]
    public void Method_level_AllowAnonymous_overrides_a_protected_class() {
        var method = MethodOn<AuthDerivedMethods>(nameof(AuthDerivedMethods.Open));

        var plan = method.GetAuthorizationPlan();

        Assert.True(plan.AllowAnonymous);
        Assert.False(plan.RequiresAuthorization);
    }

    // ---- unrestricted stays unrestricted ---------------------------------------------------

    [Fact]
    public void Undecorated_method_on_undecorated_class_requires_nothing() {
        var method = MethodOn<AuthOpenMethods>(nameof(AuthOpenMethods.Ping));

        var plan = method.GetAuthorizationPlan();

        Assert.False(plan.RequiresAuthorization);
        Assert.Empty(plan.AuthorizeData);
    }

    [Fact]
    public void Hub_level_attribute_applies_to_its_ServerMethods() {
        var method = MethodOn<AuthHubScopedMethods>(nameof(AuthHubScopedMethods.Ping));

        var plan = method.GetAuthorizationPlan();

        Assert.True(plan.RequiresAuthorization);
        Assert.Contains(plan.AuthorizeData, d => d.Policy == "hub");
    }
}

// ---- fixtures ------------------------------------------------------------------------------

public class AuthProbeHub : HARRR {
    public AuthProbeHub(IServiceProvider sp) : base(sp) { }
}

[Authorize(Policy = "hub")]
public class AuthProtectedHub : HARRR {
    public AuthProtectedHub(IServiceProvider sp) : base(sp) { }
}

public class AuthBaseMethods : ServerMethods<AuthProbeHub> {
    public string Delete() => "deleted";
}

[Authorize(Policy = "admin")]
public class AuthDerivedMethods : AuthBaseMethods {
    [Authorize(Policy = "superuser")]
    public string Escalate() => "escalated";

    [AllowAnonymous]
    public string Open() => "open";
}

public interface IAuthContract {
    string ProtectedByImplementation();

    [Authorize(Policy = "contract")]
    string ProtectedByContract();
}

public class AuthImplementation : ServerMethods<AuthProbeHub>, IAuthContract {
    [Authorize(Policy = "impl")]
    public string ProtectedByImplementation() => "impl";

    public string ProtectedByContract() => "contract";
}

/// <summary>A custom attribute implementing the actual ASP.NET Core contract.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public sealed class TenantAuthorizeAttribute : Attribute, IAuthorizeData {
    public string? Policy { get; set; } = "custom";
    public string? Roles { get; set; }
    public string? AuthenticationSchemes { get; set; }
}

public class AuthCustomAttributeMethods : ServerMethods<AuthProbeHub> {
    [TenantAuthorize]
    public string Protected() => "protected";
}

[AllowAnonymous]
public class AuthAnonymousMethods : ServerMethods<AuthProbeHub> {
    public string Open() => "open";
}

public class AuthOpenMethods : ServerMethods<AuthProbeHub> {
    public string Ping() => "pong";
}

public class AuthHubScopedMethods : ServerMethods<AuthProtectedHub> {
    public string Ping() => "pong";
}
