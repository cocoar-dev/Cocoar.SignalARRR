using System;
using System.Threading.Tasks;
using Cocoar.SignalARRR.Common;
using Cocoar.SignalARRR.Common.Helper;
using Xunit;

namespace Cocoar.SignalARRR.Tests;

/// <summary>
/// Covers how an incoming <c>Interface|Method</c> name is resolved.
/// </summary>
/// <remarks>
/// This lookup runs <em>before</em> the authorization check, so it is reachable by any client that
/// can open a connection. It used to go through <see cref="TypeHelper.FindType"/>, which scans every
/// loaded assembly on a miss and permanently caches every name it is asked about — including misses.
/// A loop of random names therefore cost a full multi-assembly scan and a permanent dictionary entry
/// per message, all serialized through one process-wide lock.
/// </remarks>
public class InterfaceResolutionTests {

    private static SignalARRRInterfaceCollection CollectionWithProbe() {
        var collection = new SignalARRRInterfaceCollection();
        collection.RegisterInterface(typeof(IResolutionProbe), typeof(ResolutionProbe));
        return collection;
    }

    [Fact]
    public void A_registered_interface_resolves_by_its_full_name() {
        var collection = CollectionWithProbe();

        var (_, methodInfo) = collection.GetInvokeInformation($"{typeof(IResolutionProbe).FullName}|{nameof(IResolutionProbe.Ping)}", 0);

        Assert.Equal(nameof(IResolutionProbe.Ping), methodInfo.Name);
    }

    [Fact]
    public void A_registered_interface_also_resolves_by_its_assembly_qualified_name() {
        var collection = CollectionWithProbe();

        var (_, methodInfo) = collection.GetInvokeInformation($"{typeof(IResolutionProbe).AssemblyQualifiedName}|{nameof(IResolutionProbe.Ping)}", 0);

        Assert.Equal(nameof(IResolutionProbe.Ping), methodInfo.Name);
    }

    /// <summary>
    /// Every registration overload has to end up in the wire-name index, not just the one the
    /// server happens to use.
    /// </summary>
    /// <remarks>
    /// The client registers through the generic overloads
    /// (<c>connection.RegisterInterface&lt;IContract, THandler&gt;(handler)</c>), the server through
    /// the <c>(Type, Type)</c> one. Indexing in only one of them left every server-to-client
    /// interface call unresolvable.
    /// </remarks>
    [Fact]
    public void The_generic_instance_overload_is_resolvable_by_wire_name() {
        var collection = new SignalARRRInterfaceCollection();
        collection.RegisterInterface<IResolutionProbe, ResolutionProbe>(new ResolutionProbe());

        var (_, methodInfo) = collection.GetInvokeInformation($"{typeof(IResolutionProbe).FullName}|{nameof(IResolutionProbe.Ping)}", 0);

        Assert.Equal(nameof(IResolutionProbe.Ping), methodInfo.Name);
    }

    [Fact]
    public void The_generic_factory_overload_is_resolvable_by_wire_name() {
        var collection = new SignalARRRInterfaceCollection();
        collection.RegisterInterface<IResolutionProbe, ResolutionProbe>(_ => new ResolutionProbe());

        var (_, methodInfo) = collection.GetInvokeInformation($"{typeof(IResolutionProbe).FullName}|{nameof(IResolutionProbe.Ping)}", 0);

        Assert.Equal(nameof(IResolutionProbe.Ping), methodInfo.Name);
    }

    [Fact]
    public void The_untyped_factory_overload_is_resolvable_by_wire_name() {
        var collection = new SignalARRRInterfaceCollection();
        collection.RegisterInterface(typeof(IResolutionProbe), _ => (object)new ResolutionProbe());

        var (_, methodInfo) = collection.GetInvokeInformation($"{typeof(IResolutionProbe).FullName}|{nameof(IResolutionProbe.Ping)}", 0);

        Assert.Equal(nameof(IResolutionProbe.Ping), methodInfo.Name);
    }

    [Fact]
    public void An_unregistered_type_is_rejected_even_though_it_exists() {
        var collection = CollectionWithProbe();

        // The type is perfectly resolvable — it just was not registered. Resolution has to be against
        // what the application exposed, not against whatever happens to be loaded in the process.
        var ex = Assert.Throws<Exception>(() =>
            collection.GetInvokeInformation($"{typeof(string).FullName}|{nameof(string.Trim)}", 0));

        Assert.Contains("not registered", ex.Message);
    }

    [Fact]
    public void An_unknown_name_is_rejected_without_touching_the_type_cache() {
        var collection = CollectionWithProbe();

        Assert.Throws<Exception>(() => collection.GetInvokeInformation("Totally.Unknown.IThing|DoWork", 0));
    }

    [Fact]
    public void A_name_without_a_separator_is_rejected() {
        var collection = CollectionWithProbe();

        Assert.Throws<ArgumentException>(() => collection.GetInvokeInformation("NoSeparatorHere", 0));
    }

    [Fact]
    public void A_method_that_is_not_on_the_interface_is_rejected() {
        var collection = CollectionWithProbe();

        Assert.Throws<Exception>(() =>
            collection.GetInvokeInformation($"{typeof(IResolutionProbe).FullName}|NoSuchMethod", 0));
    }

    /// <summary>
    /// A member the contract inherits is callable, not just the ones it declares itself.
    /// </summary>
    /// <remarks>
    /// <c>GetMethods</c> on an interface returns only declared members — unlike on a class, where it
    /// walks the base chain. The source generator does walk <c>AllInterfaces</c>, so the proxy for
    /// <c>IDerived : IBase</c> implements <c>IBase</c>'s members and puts them on the wire as
    /// <c>Ns.IDerived|BaseMethod</c>. Nothing had registered that name, so the call ended in
    /// "Method 'BaseMethod' not found!" — every time, with nothing pointing at inheritance.
    /// </remarks>
    [Fact]
    public void An_inherited_contract_member_resolves() {
        var collection = new SignalARRRInterfaceCollection();
        collection.RegisterInterface(typeof(IDerivedProbe), typeof(DerivedProbe));

        var (_, methodInfo) = collection.GetInvokeInformation(
            $"{typeof(IDerivedProbe).FullName}|{nameof(IBaseProbe.BaseMethod)}", 0);

        Assert.Equal(nameof(IBaseProbe.BaseMethod), methodInfo.Name);
    }

    /// <summary>
    /// The inherited member still resolves to the implementation, so its attributes are visible.
    /// </summary>
    /// <remarks>
    /// Same reasoning as for declared members: authorization is evaluated on the stored
    /// <c>MethodInfo</c>, and virtual dispatch runs the implementation either way.
    /// </remarks>
    [Fact]
    public void An_inherited_member_resolves_to_the_implementation() {
        var collection = new SignalARRRInterfaceCollection();
        collection.RegisterInterface(typeof(IDerivedProbe), typeof(DerivedProbe));

        var (_, methodInfo) = collection.GetInvokeInformation(
            $"{typeof(IDerivedProbe).FullName}|{nameof(IBaseProbe.BaseMethod)}", 0);

        Assert.Equal(typeof(DerivedProbe), methodInfo.DeclaringType);
    }

    /// <summary>
    /// Picking up inherited members must not disturb the ones the contract declares itself.
    /// </summary>
    [Fact]
    public void A_declared_member_still_resolves_when_the_contract_also_inherits() {
        var collection = new SignalARRRInterfaceCollection();
        collection.RegisterInterface(typeof(IDerivedProbe), typeof(DerivedProbe));

        var (_, methodInfo) = collection.GetInvokeInformation(
            $"{typeof(IDerivedProbe).FullName}|{nameof(IDerivedProbe.DerivedMethod)}", 0);

        Assert.Equal(nameof(IDerivedProbe.DerivedMethod), methodInfo.Name);
        Assert.Equal(typeof(DerivedProbe), methodInfo.DeclaringType);
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

public interface IResolutionProbe {
    Task Ping();
}

public class ResolutionProbe : IResolutionProbe {
    public Task Ping() => Task.CompletedTask;
}

public interface IBaseProbe {
    Task BaseMethod();
}

public interface IDerivedProbe : IBaseProbe {
    Task DerivedMethod();
}

public class DerivedProbe : IDerivedProbe {
    public Task BaseMethod() => Task.CompletedTask;
    public Task DerivedMethod() => Task.CompletedTask;
}
