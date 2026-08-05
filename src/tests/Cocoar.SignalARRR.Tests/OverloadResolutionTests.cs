using System;
using System.Threading;
using System.Threading.Tasks;
using Cocoar.SignalARRR.Common;
using Cocoar.SignalARRR.Common.Interfaces;
using Cocoar.SignalARRR.Server;
using Cocoar.SignalARRR.Server.ExtensionMethods;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Cocoar.SignalARRR.Tests;

/// <summary>
/// Covers how overloads are told apart on the wire (F-6).
/// </summary>
/// <remarks>
/// The wire carries no parameter types, only an argument array — so methods sharing a name are
/// distinguishable exactly when their argument counts differ. Registration indexes every method
/// under each argument count it accepts (trailing defaults make that a range), dispatch resolves
/// with the count of the incoming message, and two methods reachable under the same name and count
/// fail hard at registration. Before, the last registration silently won with an unspecified
/// order — and possibly different <c>[Authorize]</c> data than the overload the caller believed
/// was checked.
/// </remarks>
public class OverloadResolutionTests {

    // ---- SignalARRRMethodsCollection, server slot rules ------------------------------------

    private static SignalARRRMethodsCollection ServerCollection() => new(ServerWireSlots.Policy);

    [Fact]
    public void Overloads_with_different_argument_counts_resolve_by_count() {
        var collection = ServerCollection();
        collection.AddMethod("Fetch", Method(nameof(MethodProbe.Fetch), typeof(int)));
        collection.AddMethod("Fetch", Method(nameof(MethodProbe.Fetch), typeof(int), typeof(int)));

        Assert.Single(collection.GetMethodInformations("Fetch", 1).MethodInfo.GetParameters());
        Assert.Equal(2, collection.GetMethodInformations("Fetch", 2).MethodInfo.GetParameters().Length);
    }

    [Fact]
    public void Overloads_with_the_same_argument_count_fail_at_registration() {
        var collection = ServerCollection();
        collection.AddMethod("GetById", Method(nameof(MethodProbe.GetById), typeof(string)));

        var ex = Assert.Throws<InvalidOperationException>(() =>
            collection.AddMethod("GetById", Method(nameof(MethodProbe.GetById), typeof(Guid))));

        Assert.Contains("GetById", ex.Message);
        Assert.Contains("argument count", ex.Message);
    }

    [Fact]
    public void A_cancellation_token_does_not_occupy_a_slot_on_the_server() {
        var collection = ServerCollection();
        collection.AddMethod("WithToken", Method(nameof(MethodProbe.WithToken)));

        // The server binds its own token from the invocation; the message carries one argument.
        Assert.NotNull(collection.GetMethodInformations("WithToken", 1).MethodInfo);
        Assert.Throws<Exception>(() => collection.GetMethodInformations("WithToken", 2));
    }

    [Fact]
    public void A_FromServices_parameter_does_not_occupy_a_slot() {
        var collection = ServerCollection();
        collection.AddMethod("WithService", Method(nameof(MethodProbe.WithService)));

        Assert.NotNull(collection.GetMethodInformations("WithService", 1).MethodInfo);
        Assert.Throws<Exception>(() => collection.GetMethodInformations("WithService", 2));
    }

    [Fact]
    public void Trailing_defaults_make_a_method_reachable_under_a_range_of_counts() {
        var collection = ServerCollection();
        collection.AddMethod("WithDefaults", Method(nameof(MethodProbe.WithDefaults)));

        Assert.NotNull(collection.GetMethodInformations("WithDefaults", 1).MethodInfo);
        Assert.NotNull(collection.GetMethodInformations("WithDefaults", 2).MethodInfo);
        Assert.NotNull(collection.GetMethodInformations("WithDefaults", 3).MethodInfo);
        Assert.Throws<Exception>(() => collection.GetMethodInformations("WithDefaults", 0));
    }

    [Fact]
    public void Overloads_whose_ranges_overlap_through_defaults_fail_at_registration() {
        var collection = ServerCollection();
        collection.AddMethod("Bar", Method(nameof(MethodProbe.Bar), typeof(int)));

        // A one-argument message would be genuinely ambiguous between Bar(int) and
        // Bar(int, int = 5) — C# only resolves that at the call site, which the wire does not have.
        Assert.Throws<InvalidOperationException>(() =>
            collection.AddMethod("Bar", Method(nameof(MethodProbe.Bar), typeof(int), typeof(int))));
    }

    [Fact]
    public void The_error_for_a_wrong_count_names_the_registered_counts() {
        var collection = ServerCollection();
        collection.AddMethod("Fetch", Method(nameof(MethodProbe.Fetch), typeof(int)));

        var ex = Assert.Throws<Exception>(() => collection.GetMethodInformations("Fetch", 3));

        Assert.Contains("3 argument", ex.Message);
        Assert.Contains("1", ex.Message);
    }

    [Fact]
    public void Registering_the_same_method_again_is_not_a_conflict() {
        var collection = ServerCollection();
        var method = Method(nameof(MethodProbe.Fetch), typeof(int));

        collection.AddMethod("Fetch", method);
        collection.AddMethod("Fetch", method);

        Assert.Same(method, collection.GetMethodInformations("Fetch", 1).MethodInfo);
    }

    // ---- Client side: every declared parameter is a slot -----------------------------------

    [Fact]
    public void A_cancellation_token_occupies_a_slot_on_the_client() {
        // Going out to a client the token reference sits in the argument array — it is the only
        // thing telling a TypeScript or Swift client which argument is the token.
        var collection = new SignalARRRMethodsCollection();
        collection.AddMethod("WithToken", Method(nameof(MethodProbe.WithToken)));

        Assert.NotNull(collection.GetMethodInformations("WithToken", 2).MethodInfo);
        Assert.Throws<Exception>(() => collection.GetMethodInformations("WithToken", 1));
    }

    // ---- Interface registration ------------------------------------------------------------

    [Fact]
    public void Interface_overloads_with_the_same_count_fail_at_registration() {
        var collection = new SignalARRRInterfaceCollection();

        var ex = Assert.Throws<InvalidOperationException>(() =>
            collection.RegisterInterface(typeof(ICollidingContract), typeof(CollidingContractImpl)));

        Assert.Contains(nameof(ICollidingContract.Get), ex.Message);
    }

    [Fact]
    public void Interface_overloads_with_different_counts_resolve_by_count() {
        var collection = new SignalARRRInterfaceCollection();
        collection.RegisterInterface(typeof(IOverloadedContract), typeof(OverloadedContractImpl));

        var one = collection.GetInvokeInformation($"{typeof(IOverloadedContract).FullName}|Get", 1);
        var two = collection.GetInvokeInformation($"{typeof(IOverloadedContract).FullName}|Get", 2);

        Assert.Single(one.MethodInfo.GetParameters());
        Assert.Equal(2, two.MethodInfo.GetParameters().Length);
    }

    [Fact]
    public void A_name_declared_on_the_registered_interface_hides_every_inherited_one() {
        var collection = new SignalARRRInterfaceCollection();
        collection.RegisterInterface(typeof(IHidingContract), typeof(HidingContractImpl));

        // IHidingContract declares Do(int, int); the inherited IHiddenBase.Do(int) is hidden
        // entirely — same rule as before: registering a derived contract cannot change what one
        // of its own members means.
        Assert.NotNull(collection.GetInvokeInformation($"{typeof(IHidingContract).FullName}|Do", 2).MethodInfo);
        Assert.Throws<Exception>(() =>
            collection.GetInvokeInformation($"{typeof(IHidingContract).FullName}|Do", 1));
    }

    [Fact]
    public void Two_base_interfaces_with_the_same_member_fail_at_registration() {
        var collection = new SignalARRRInterfaceCollection();

        // Neither member is declared on the registered interface itself, so neither may hide the
        // other — an incoming 'Ping' with zero arguments would be genuinely ambiguous. This used
        // to be first-enumerated-wins in unspecified order.
        var ex = Assert.Throws<InvalidOperationException>(() =>
            collection.RegisterInterface(typeof(IAmbiguousCombined), typeof(AmbiguousCombinedImpl)));

        Assert.Contains("Ping", ex.Message);
    }

    [Fact]
    public void Two_base_interfaces_with_the_same_name_but_different_counts_both_resolve() {
        var collection = new SignalARRRInterfaceCollection();
        collection.RegisterInterface(typeof(IDistinctCombined), typeof(DistinctCombinedImpl));

        Assert.Empty(collection.GetInvokeInformation($"{typeof(IDistinctCombined).FullName}|Ping", 0).MethodInfo.GetParameters());
        Assert.Single(collection.GetInvokeInformation($"{typeof(IDistinctCombined).FullName}|Ping", 1).MethodInfo.GetParameters());
    }

    [Fact]
    public void Server_slot_rules_apply_to_interface_registrations_too() {
        var collection = new SignalARRRInterfaceCollection(ServerWireSlots.Policy);
        collection.RegisterInterface(typeof(ITokenContract), typeof(TokenContractImpl));

        Assert.NotNull(collection.GetInvokeInformation($"{typeof(ITokenContract).FullName}|Wait", 1).MethodInfo);
        Assert.Throws<Exception>(() =>
            collection.GetInvokeInformation($"{typeof(ITokenContract).FullName}|Wait", 2));
    }

    // ---- The real server wiring ------------------------------------------------------------

    [Fact]
    public void The_hub_registration_applies_the_server_slot_rules() {
        var methods = OverloadServerSetup.MethodsFor<OverloadProbeHub>();

        // OverloadProbeMethods.Wait(string, CancellationToken): the token is not a slot.
        Assert.NotNull(methods.GetMethodInformations("OverloadProbeMethods.Wait", 1).MethodInfo);
        // OverloadProbeMethods.Page(int, int = 25): reachable with and without the page size.
        Assert.NotNull(methods.GetMethodInformations("OverloadProbeMethods.Page", 1).MethodInfo);
        Assert.NotNull(methods.GetMethodInformations("OverloadProbeMethods.Page", 2).MethodInfo);
        // OverloadProbeMethods.Fetch(int) / Fetch(int, int): both reachable, told apart by count.
        Assert.Single(methods.GetMethodInformations("OverloadProbeMethods.Fetch", 1).MethodInfo.GetParameters());
        Assert.Equal(2, methods.GetMethodInformations("OverloadProbeMethods.Fetch", 2).MethodInfo.GetParameters().Length);
    }

    private static System.Reflection.MethodInfo Method(string name, params Type[] parameterTypes) =>
        typeof(MethodProbe).GetMethod(name, parameterTypes)
        ?? typeof(MethodProbe).GetMethod(name)
        ?? throw new InvalidOperationException($"probe method '{name}' not found");

    private class MethodProbe {
        public string Fetch(int id) => "one";
        public string Fetch(int id, int count) => "two";

        public string GetById(string id) => id;
        public string GetById(Guid id) => id.ToString();

        public Task WithToken(string value, CancellationToken cancellationToken) => Task.CompletedTask;

        public string WithService(string value, [FromServices] IServiceProvider services) => value;

        public string WithDefaults(int id, int count = 5, string mode = "all") => mode;

        public string Bar(int id) => "one";
        public string Bar(int id, int count = 5) => "two";
    }
}

// ---- Probe contracts (top level: nested types cannot implement interface maps cleanly) ------

public interface ICollidingContract {
    Task Get(string id);
    Task Get(Guid id);
}

public class CollidingContractImpl : ICollidingContract {
    public Task Get(string id) => Task.CompletedTask;
    public Task Get(Guid id) => Task.CompletedTask;
}

public interface IOverloadedContract {
    Task Get(int id);
    Task Get(int id, int count);
}

public class OverloadedContractImpl : IOverloadedContract {
    public Task Get(int id) => Task.CompletedTask;
    public Task Get(int id, int count) => Task.CompletedTask;
}

public interface IHiddenBase {
    Task Do(int a);
}

public interface IHidingContract : IHiddenBase {
    Task Do(int a, int b);
}

public class HidingContractImpl : IHidingContract {
    public Task Do(int a) => Task.CompletedTask;
    public Task Do(int a, int b) => Task.CompletedTask;
}

public interface IAmbiguousLeft {
    Task Ping();
}

public interface IAmbiguousRight {
    Task Ping();
}

public interface IAmbiguousCombined : IAmbiguousLeft, IAmbiguousRight {
}

public class AmbiguousCombinedImpl : IAmbiguousCombined {
    public Task Ping() => Task.CompletedTask;
}

public interface IDistinctLeft {
    Task Ping();
}

public interface IDistinctRight {
    Task Ping(int delay);
}

public interface IDistinctCombined : IDistinctLeft, IDistinctRight {
}

public class DistinctCombinedImpl : IDistinctCombined {
    public Task Ping() => Task.CompletedTask;
    public Task Ping(int delay) => Task.CompletedTask;
}

public interface ITokenContract {
    Task Wait(int seconds, CancellationToken cancellationToken);
}

public class TokenContractImpl : ITokenContract {
    public Task Wait(int seconds, CancellationToken cancellationToken) => Task.CompletedTask;
}

// ---- Real-wiring probes ---------------------------------------------------------------------

public class OverloadProbeHub : HARRR {
    public OverloadProbeHub(IServiceProvider serviceProvider) : base(serviceProvider) { }
}

public class OverloadProbeMethods : ServerMethods<OverloadProbeHub> {
    public Task Wait(string value, CancellationToken cancellationToken) => Task.CompletedTask;

    public int Page(int index, int size = 25) => index * size;

    public string Fetch(int id) => "one";
    public string Fetch(int id, int count) => "two";
}

internal static class OverloadServerSetup {
    public static ISignalARRRMethodsCollection MethodsFor<THub>() where THub : HARRR {
        var services = new ServiceCollection();
        services.AddSignalARRR(b => b.AddServerMethodsFrom(typeof(OverloadServerSetup).Assembly));

        using var provider = services.BuildServiceProvider();
        return provider.GetRequiredKeyedService<ISignalARRRMethodsCollection>(typeof(THub).FullName);
    }
}
