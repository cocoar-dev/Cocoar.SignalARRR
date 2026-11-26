using System;
using System.Linq;
using Cocoar.SignalARRR.Server;
using Cocoar.SignalARRR.Server.ExtensionMethods;
using Microsoft.AspNetCore.Authorization;
using Xunit;

namespace Cocoar.SignalARRR.Tests;

/// <summary>
/// Covers the authorization plan the source generator resolves for each server method. The rules
/// are the ones the reflection registry applied at startup: [AllowAnonymous] anywhere wins; otherwise
/// the most specific level with data decides — the method with its interface declarations, then the
/// class chain, then the hub.
/// </summary>
public class AuthorizationResolutionTests {

    private static SignalARRRAuthorizationPlan Plan<THub>(string name) where THub : HARRR =>
        ServerTables.For<THub>().Resolve(name, 0).Authorization;

    // ---- S-2: resolution via the registered class, not the declaring one --------------------

    [Fact]
    public void Method_inherited_from_undecorated_base_is_protected_by_the_derived_class() {
        // Delete is declared on the undecorated base and registered from the [Authorize]d derived class.
        var plan = Plan<AuthProbeHub>("AuthDerivedMethods.Delete");

        Assert.True(plan.RequiresAuthorization);
        Assert.Contains(plan.AuthorizeData, d => d.Policy == "admin");
    }

    [Fact]
    public void Method_level_attribute_still_wins_over_the_class() {
        var plan = Plan<AuthProbeHub>("AuthDerivedMethods.Escalate");

        Assert.Contains(plan.AuthorizeData, d => d.Policy == "superuser");
        Assert.DoesNotContain(plan.AuthorizeData, d => d.Policy == "admin");
    }

    // ---- S-3: the implementation counts, not only the contract ------------------------------

    [Fact]
    public void Attribute_on_the_implementation_is_honoured() {
        var plan = Plan<AuthProbeHub>("AuthImplementation.ProtectedByImplementation");

        Assert.True(plan.RequiresAuthorization);
        Assert.Contains(plan.AuthorizeData, d => d.Policy == "impl");
    }

    [Fact]
    public void Attribute_on_the_contract_is_still_honoured() {
        var plan = Plan<AuthProbeHub>("AuthImplementation.ProtectedByContract");

        Assert.True(plan.RequiresAuthorization);
        Assert.Contains(plan.AuthorizeData, d => d.Policy == "contract");
    }

    [Fact]
    public void Interface_dispatch_carries_the_implementations_plan() {
        var plan = Plan<AuthProbeHub>($"{typeof(IAuthContract).FullName}|{nameof(IAuthContract.ProtectedByImplementation)}");

        Assert.Contains(plan.AuthorizeData, d => d.Policy == "impl");
    }

    // ---- S-4: IAuthorizeData, not just AuthorizeAttribute -----------------------------------

    [Fact]
    public void Custom_IAuthorizeData_attribute_is_collected() {
        var plan = Plan<AuthProbeHub>("AuthCustomAttributeMethods.Protected");

        Assert.True(plan.RequiresAuthorization);
        Assert.Contains(plan.AuthorizeData, d => d.Policy == "custom");
    }

    [Fact]
    public void Attribute_arguments_are_reproduced() {
        var data = Assert.Single(Plan<AuthProbeHub>("AuthCustomAttributeMethods.WithArguments").AuthorizeData);

        Assert.IsType<AuthorizeAttribute>(data);
        Assert.Equal("named", data.Policy);
        Assert.Equal("a,b", data.Roles);
        Assert.Equal("Bearer", data.AuthenticationSchemes);
    }

    // ---- AllowAnonymous --------------------------------------------------------------------

    [Fact]
    public void Class_level_AllowAnonymous_is_honoured() {
        var plan = Plan<AuthProbeHub>("AuthAnonymousMethods.Open");

        Assert.True(plan.AllowAnonymous);
        Assert.False(plan.RequiresAuthorization);
    }

    [Fact]
    public void Method_level_AllowAnonymous_overrides_a_protected_class() {
        var plan = Plan<AuthProbeHub>("AuthDerivedMethods.Open");

        Assert.True(plan.AllowAnonymous);
        Assert.False(plan.RequiresAuthorization);
    }

    // ---- unrestricted stays unrestricted ---------------------------------------------------

    [Fact]
    public void Undecorated_method_on_undecorated_class_requires_nothing() {
        var plan = Plan<AuthProbeHub>("AuthOpenMethods.Ping");

        Assert.False(plan.RequiresAuthorization);
        Assert.Empty(plan.AuthorizeData);
    }

    [Fact]
    public void Hub_level_attribute_applies_to_its_ServerMethods() {
        var plan = Plan<AuthProtectedHub>("AuthHubScopedMethods.Ping");

        Assert.True(plan.RequiresAuthorization);
        Assert.Contains(plan.AuthorizeData, d => d.Policy == "hub");
    }

    [Fact]
    public void Hub_level_attribute_applies_to_the_hubs_own_methods() {
        var plan = Plan<AuthProtectedHub>(nameof(AuthProtectedHub.HubPing));

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

    public string HubPing() => "pong";
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

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public sealed class TenantAuthorizeAttribute : Attribute, IAuthorizeData {
    public string? Policy { get; set; } = "custom";
    public string? Roles { get; set; }
    public string? AuthenticationSchemes { get; set; }
}

public class AuthCustomAttributeMethods : ServerMethods<AuthProbeHub> {
    [TenantAuthorize]
    public string Protected() => "protected";

    [Authorize("named", Roles = "a,b", AuthenticationSchemes = "Bearer")]
    public string WithArguments() => "protected";
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
