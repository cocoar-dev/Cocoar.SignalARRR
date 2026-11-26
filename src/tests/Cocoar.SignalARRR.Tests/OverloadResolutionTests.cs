using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Cocoar.SignalARRR.Common.Exceptions;
using Cocoar.SignalARRR.Server;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace Cocoar.SignalARRR.Tests;

/// <summary>
/// Covers how overloads are told apart on the wire (F-6).
/// </summary>
/// <remarks>
/// The wire carries no parameter types, only an argument array — so methods sharing a name are
/// distinguishable exactly when their argument counts differ. The generated table holds every
/// method with the range of argument counts it accepts (trailing defaults make that a range),
/// dispatch resolves with the count of the incoming message, and two methods reachable under the
/// same name and count fail the build (SARRR002). Before 5.x the last registration silently won —
/// with possibly different <c>[Authorize]</c> data than the overload the caller believed was checked.
/// </remarks>
public class OverloadResolutionTests {

    private static ServerMethodTable Table => ServerTables.For<OverloadProbeHub>();

    [Fact]
    public void Overloads_with_different_argument_counts_resolve_by_count() {
        Assert.Contains("Fetch(int)", Table.Resolve("OverloadProbeMethods.Fetch", 1).Declaration);
        Assert.Contains("Fetch(int, int)", Table.Resolve("OverloadProbeMethods.Fetch", 2).Declaration);
    }

    [Fact]
    public void A_cancellation_token_does_not_occupy_a_slot() {
        // The server binds its own token from the invocation; the message carries one argument.
        Assert.True(Table.IsRegistered("OverloadProbeMethods.Wait", 1));
        Assert.False(Table.IsRegistered("OverloadProbeMethods.Wait", 2));
    }

    [Fact]
    public void A_FromServices_parameter_does_not_occupy_a_slot() {
        Assert.True(Table.IsRegistered("OverloadProbeMethods.WithService", 1));
        Assert.False(Table.IsRegistered("OverloadProbeMethods.WithService", 2));
    }

    [Fact]
    public void Trailing_defaults_make_a_method_reachable_under_a_range_of_counts() {
        Assert.True(Table.IsRegistered("OverloadProbeMethods.WithDefaults", 1));
        Assert.True(Table.IsRegistered("OverloadProbeMethods.WithDefaults", 2));
        Assert.True(Table.IsRegistered("OverloadProbeMethods.WithDefaults", 3));
        Assert.False(Table.IsRegistered("OverloadProbeMethods.WithDefaults", 0));
    }

    [Fact]
    public void The_error_for_a_wrong_count_names_the_registered_counts() {
        var ex = Assert.Throws<MethodResolutionException>(() => Table.Resolve("OverloadProbeMethods.Fetch", 3));

        Assert.Contains("3 argument", ex.Message);
        Assert.Contains("1, 2", ex.Message);
    }

    [Fact]
    public void Interface_overloads_with_different_counts_resolve_by_count() {
        var name = $"{typeof(IOverloadedContract).FullName}|Get";
        Assert.Contains("Get(int)", Table.Resolve(name, 1).Declaration);
        Assert.Contains("Get(int, int)", Table.Resolve(name, 2).Declaration);
    }

    [Fact]
    public void A_name_declared_on_the_contract_hides_every_inherited_one() {
        // IHidingContract declares Do(int, int); the inherited IHiddenBase.Do(int) is hidden
        // entirely: registering a derived contract cannot change what one of its own members means.
        var name = $"{typeof(IHidingContract).FullName}|Do";
        Assert.True(Table.IsRegistered(name, 2));
        Assert.False(Table.IsRegistered(name, 1));
    }

    [Fact]
    public void Two_base_interfaces_with_the_same_name_but_different_counts_both_resolve() {
        var name = $"{typeof(IDistinctCombined).FullName}|Ping";
        Assert.Contains("Ping()", Table.Resolve(name, 0).Declaration);
        Assert.Contains("Ping(int)", Table.Resolve(name, 1).Declaration);
    }

    [Fact]
    public void Server_slot_rules_apply_to_interface_methods_too() {
        var name = $"{typeof(ITokenContract).FullName}|Sleep";
        Assert.True(Table.IsRegistered(name, 1));
        Assert.False(Table.IsRegistered(name, 2));
    }

    // ---- Clashes the wire cannot resolve fail the build -------------------------------------

    [Fact]
    public void Overloads_with_the_same_argument_count_fail_the_build() {
        var result = GeneratorHarness.Compile(GeneratorHarness.Usings + @"
public class Hub1 : HARRR { public Hub1(IServiceProvider sp) : base(sp) { } }
public class M : ServerMethods<Hub1> {
    public string GetById(string id) => id;
    public string GetById(Guid id) => id.ToString();
}");

        var error = Assert.Single(result.Errors, d => d.Id == "SARRR002");
        Assert.Contains("M.GetById", error.GetMessage());
    }

    [Fact]
    public void Overloads_whose_ranges_overlap_through_defaults_fail_the_build() {
        // A one-argument message would be genuinely ambiguous between Bar(int) and
        // Bar(int, int = 5) — C# only resolves that at the call site, which the wire does not have.
        var result = GeneratorHarness.Compile(GeneratorHarness.Usings + @"
public class Hub1 : HARRR { public Hub1(IServiceProvider sp) : base(sp) { } }
public class M : ServerMethods<Hub1> {
    public string Bar(int id) => ""one"";
    public string Bar(int id, int count = 5) => ""two"";
}");

        Assert.Contains(result.Errors, d => d.Id == "SARRR002");
    }

    [Fact]
    public void Interface_overloads_with_the_same_count_fail_the_build() {
        var result = GeneratorHarness.Compile(GeneratorHarness.Usings + @"
public interface IColliding { Task Get(string id); Task Get(Guid id); }
public class Hub1 : HARRR { public Hub1(IServiceProvider sp) : base(sp) { } }
public class M : ServerMethods<Hub1>, IColliding {
    public Task Get(string id) => Task.CompletedTask;
    public Task Get(Guid id) => Task.CompletedTask;
}");

        Assert.Contains(result.Errors, d => d.Id == "SARRR002" && d.GetMessage().Contains("IColliding|Get"));
    }

    [Fact]
    public void Two_base_interfaces_with_the_same_member_fail_the_build() {
        // Neither member is declared on the contract itself, so neither may hide the other; an
        // incoming 'Ping' with zero arguments would be genuinely ambiguous.
        var result = GeneratorHarness.Compile(GeneratorHarness.Usings + @"
public interface ILeft { Task Ping(); }
public interface IRight { Task Ping(); }
public interface ICombined : ILeft, IRight { }
public class Hub1 : HARRR { public Hub1(IServiceProvider sp) : base(sp) { } }
public class M : ServerMethods<Hub1>, ICombined { public Task Ping() => Task.CompletedTask; }");

        Assert.Contains(result.Errors, d => d.Id == "SARRR002" && d.GetMessage().Contains("ICombined|Ping"));
    }

    [Fact]
    public void A_clash_between_assemblies_fails_when_the_table_is_built() {
        // Within one compilation the generator catches it; two assemblies only meet at startup.
        var table = new ServerMethodTable();
        table.Add(Descriptor("Clash", "A.Clash(int)", 1, 1));

        var ex = Assert.Throws<InvalidOperationException>(() => table.Add(Descriptor("Clash", "B.Clash(string)", 1, 1)));
        Assert.Contains("argument count", ex.Message);
    }

    private static ServerMethodDescriptor Descriptor(string name, string declaration, int min, int max) =>
        new(name, declaration, min, max, new Cocoar.SignalARRR.Server.ExtensionMethods.SignalARRRAuthorizationPlan(false, Array.Empty<Microsoft.AspNetCore.Authorization.IAuthorizeData>()),
            static (_, _) => Task.FromResult<object?>(null));
}

// ---- Probes: one hub, compiled with the generator -------------------------------------------

public interface IOverloadedContract {
    Task Get(int id);
    Task Get(int id, int count);
}

public interface IHiddenBase {
    Task Do(int a);
}

public interface IHidingContract : IHiddenBase {
    Task Do(int a, int b);
}

public interface IDistinctLeft {
    Task Ping();
}

public interface IDistinctRight {
    Task Ping(int delay);
}

public interface IDistinctCombined : IDistinctLeft, IDistinctRight {
}

public interface ITokenContract {
    Task Sleep(int seconds, CancellationToken cancellationToken);
}

public class OverloadProbeHub : HARRR {
    public OverloadProbeHub(IServiceProvider serviceProvider) : base(serviceProvider) { }
}

public class OverloadProbeMethods : ServerMethods<OverloadProbeHub>, IOverloadedContract, IHidingContract, IDistinctCombined, ITokenContract {
    public Task Wait(string value, CancellationToken cancellationToken) => Task.CompletedTask;

    public string WithService(string value, [FromServices] IServiceProvider services) => value;

    public string WithDefaults(int id, int count = 5, string mode = "all") => mode;

    public string Fetch(int id) => "one";
    public string Fetch(int id, int count) => "two";

    public Task Get(int id) => Task.CompletedTask;
    public Task Get(int id, int count) => Task.CompletedTask;

    public Task Do(int a) => Task.CompletedTask;
    public Task Do(int a, int b) => Task.CompletedTask;

    public Task Ping() => Task.CompletedTask;
    public Task Ping(int delay) => Task.CompletedTask;

    public Task Sleep(int seconds, CancellationToken cancellationToken) => Task.CompletedTask;
}
