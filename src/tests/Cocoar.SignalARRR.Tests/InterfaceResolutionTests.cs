using System;
using System.Linq;
using System.Threading.Tasks;
using Cocoar.SignalARRR.Common.Exceptions;
using Cocoar.SignalARRR.Common.Helper;
using Cocoar.SignalARRR.Server;
using Xunit;

namespace Cocoar.SignalARRR.Tests;

/// <summary>
/// Covers how an incoming <c>Interface|Method</c> name is resolved.
/// </summary>
/// <remarks>
/// This lookup runs <em>before</em> the authorization check, so it is reachable by any client that
/// can open a connection. It resolves against the generated table — what the application exposed —
/// never against whatever types happen to be loaded in the process: resolving names through the
/// loaded assemblies once let a loop of random names cost a full multi-assembly scan and a
/// permanent cache entry per message.
/// </remarks>
public class InterfaceResolutionTests {

    private static ServerMethodTable Table => ServerTables.For<InterfaceProbeHub>();

    private static string Name<T>(string method) => $"{typeof(T).FullName}|{method}";

    [Fact]
    public void A_registered_interface_resolves_by_its_full_name() {
        Assert.Contains(nameof(IResolutionProbe.Ping), Table.Resolve(Name<IResolutionProbe>(nameof(IResolutionProbe.Ping)), 0).Declaration);
    }

    [Fact]
    public void A_registered_interface_also_resolves_by_its_assembly_qualified_name() {
        var descriptor = Table.Resolve($"{typeof(IResolutionProbe).AssemblyQualifiedName}|{nameof(IResolutionProbe.Ping)}", 0);

        Assert.Equal(Name<IResolutionProbe>(nameof(IResolutionProbe.Ping)), descriptor.Name);
    }

    [Fact]
    public void An_unregistered_type_is_rejected_even_though_it_exists() {
        // The type is perfectly resolvable — it just was not registered.
        var ex = Assert.Throws<MethodResolutionException>(() => Table.Resolve($"{typeof(string).FullName}|{nameof(string.Trim)}", 0));

        Assert.Contains("not registered", ex.Message);
    }

    [Fact]
    public void An_unknown_name_is_rejected() {
        Assert.Throws<MethodResolutionException>(() => Table.Resolve("Totally.Unknown.IThing|DoWork", 0));
    }

    [Fact]
    public void A_name_without_a_separator_is_not_an_interface_call() {
        var ex = Assert.Throws<MethodResolutionException>(() => Table.Resolve("NoSeparatorHere", 0));

        Assert.Contains("not found", ex.Message);
    }

    [Fact]
    public void A_method_that_is_not_on_the_interface_is_rejected() {
        Assert.Throws<MethodResolutionException>(() => Table.Resolve(Name<IResolutionProbe>("NoSuchMethod"), 0));
    }

    /// <summary>
    /// A member the contract inherits is callable, not just the ones it declares itself.
    /// </summary>
    /// <remarks>
    /// The proxy for <c>IDerived : IBase</c> implements <c>IBase</c>'s members and puts them on the
    /// wire as <c>Ns.IDerived|BaseMethod</c>, so that name has to be in the table.
    /// </remarks>
    [Fact]
    public void An_inherited_contract_member_resolves() {
        Assert.Contains(nameof(IBaseProbe.BaseMethod), Table.Resolve(Name<IDerivedProbe>(nameof(IBaseProbe.BaseMethod)), 0).Declaration);
    }

    /// <summary>The inherited member is called on the implementation, so its attributes count.</summary>
    [Fact]
    public void An_inherited_member_resolves_to_the_implementation() {
        Assert.StartsWith(typeof(InterfaceProbeMethods).FullName!, Table.Resolve(Name<IDerivedProbe>(nameof(IBaseProbe.BaseMethod)), 0).Declaration);
    }

    [Fact]
    public void A_declared_member_still_resolves_when_the_contract_also_inherits() {
        Assert.Contains(nameof(IDerivedProbe.DerivedMethod), Table.Resolve(Name<IDerivedProbe>(nameof(IDerivedProbe.DerivedMethod)), 0).Declaration);
    }

    /// <summary>
    /// <c>[MessageName]</c> on a contract member decides the name on the wire (N-5).
    /// </summary>
    /// <remarks>
    /// Until 5.0 the C# identifier <em>was</em> the protocol, so renaming a method silently broke
    /// every TypeScript and Swift client — they write the name out as a string and no compiler
    /// checks it. The declared name replaces the identifier rather than aliasing it.
    /// </remarks>
    [Fact]
    public void MessageName_on_a_contract_member_decides_the_wire_name() {
        Assert.Contains(nameof(IMessageNamedProbe.Ping), Table.Resolve(Name<IMessageNamedProbe>("Renamed"), 0).Declaration);
    }

    [Fact]
    public void The_csharp_name_no_longer_resolves_once_a_wire_name_is_declared() {
        Assert.Throws<MethodResolutionException>(() => Table.Resolve(Name<IMessageNamedProbe>(nameof(IMessageNamedProbe.Ping)), 0));
    }

    [Fact]
    public void MessageName_on_the_interface_itself_decides_the_interface_part() {
        Assert.Contains(nameof(IRenamedContractProbe.ReceiveMessage), Table.Resolve("chat.client|received", 1).Declaration);
    }

    [Fact]
    public void A_renamed_interface_no_longer_answers_to_its_full_name() {
        var ex = Assert.Throws<MethodResolutionException>(() => Table.Resolve(Name<IRenamedContractProbe>("received"), 1));
        Assert.Contains("not registered", ex.Message);
    }

    [Fact]
    public void MessageName_on_the_implementation_does_not_rename_the_interface_member() {
        // On the class it renames the class's own entry (InterfaceProbeMethods.Renamed); the
        // interface entry keeps the contract's name.
        Assert.True(Table.IsRegistered(Name<IPlainContractProbe>(nameof(IPlainContractProbe.Pong)), 0));
        Assert.True(Table.IsRegistered("InterfaceProbeMethods.Renamed", 0));
    }

    [Fact]
    public void A_wire_name_containing_the_separator_fails_the_build() {
        // The receiver splits on the first separator to tell interface from method, so a separator
        // inside either half would address something else entirely.
        var result = GeneratorHarness.Compile(GeneratorHarness.Usings + @"
public interface ISeparator { [MessageName(""bad|name"")] Task Ping(); }
public class Hub1 : HARRR { public Hub1(IServiceProvider sp) : base(sp) { } }
public class M : ServerMethods<Hub1>, ISeparator { public Task Ping() => Task.CompletedTask; }");

        Assert.Contains(result.Errors, d => d.Id == "SARRR004" && d.GetMessage().Contains("bad|name"));
    }

    [Fact]
    public void Two_members_renamed_onto_the_same_wire_name_fail_the_build() {
        var result = GeneratorHarness.Compile(GeneratorHarness.Usings + @"
public interface IColliding {
    [MessageName(""Collide"")] Task First();
    [MessageName(""Collide"")] Task Second();
}
public class Hub1 : HARRR { public Hub1(IServiceProvider sp) : base(sp) { } }
public class M : ServerMethods<Hub1>, IColliding {
    public Task First() => Task.CompletedTask;
    public Task Second() => Task.CompletedTask;
}");

        Assert.Contains(result.Errors, d => d.Id == "SARRR002" && d.GetMessage().Contains("Collide"));
    }
}

/// <summary>Covers the bounded, success-only resolution cache.</summary>
public class TypeHelperTests {

    [Fact]
    public void A_resolvable_type_is_found() {
        Assert.Equal(typeof(string), TypeHelper.FindType(typeof(string).FullName!));
    }

    [Fact]
    public void An_unresolvable_name_returns_null_and_stays_unresolvable_only_by_being_absent() {
        // Misses must not be cached: a type from an assembly loaded later has to become resolvable,
        // which the previous negative caching made permanently impossible.
        Assert.Null(TypeHelper.FindType("Definitely.Not.A.Real.Type"));
        Assert.Null(TypeHelper.FindType("Definitely.Not.A.Real.Type"));
    }

    [Fact]
    public void Resolution_is_case_sensitive_for_qualified_names() {
        // The previous case-insensitive fallback could return a *different* type than the caller
        // named — the wrong answer to give for a value that decides which type a generic method
        // operates on.
        Assert.Null(TypeHelper.FindType("system.string"));
    }

    [Fact]
    public void An_empty_name_maps_to_void() {
        Assert.Equal(typeof(void), TypeHelper.FindType(string.Empty));
    }
}

// ---- Probes ---------------------------------------------------------------------------------

public interface IResolutionProbe {
    Task Ping();
}

public interface IMessageNamedProbe {
    [Cocoar.SignalARRR.Common.Attributes.MessageName("Renamed")]
    Task Ping();
}

public interface IPlainContractProbe {
    Task Pong();
}

[Cocoar.SignalARRR.Common.Attributes.MessageName("chat.client")]
public interface IRenamedContractProbe {
    [Cocoar.SignalARRR.Common.Attributes.MessageName("received")]
    Task ReceiveMessage(string text);
}

public interface IBaseProbe {
    Task BaseMethod();
}

public interface IDerivedProbe : IBaseProbe {
    Task DerivedMethod();
}

public class InterfaceProbeHub : HARRR {
    public InterfaceProbeHub(IServiceProvider serviceProvider) : base(serviceProvider) { }
}

public class InterfaceProbeMethods : ServerMethods<InterfaceProbeHub>,
    IResolutionProbe, IMessageNamedProbe, IPlainContractProbe, IRenamedContractProbe, IDerivedProbe {

    Task IResolutionProbe.Ping() => Task.CompletedTask;
    Task IMessageNamedProbe.Ping() => Task.CompletedTask;

    [Cocoar.SignalARRR.Common.Attributes.MessageName("Renamed")]
    public Task Pong() => Task.CompletedTask;

    public Task ReceiveMessage(string text) => Task.CompletedTask;
    public Task BaseMethod() => Task.CompletedTask;
    public Task DerivedMethod() => Task.CompletedTask;
}
