using System;
using System.Linq;
using Cocoar.SignalARRR.Server;
using Xunit;

namespace Cocoar.SignalARRR.Tests;

/// <summary>
/// Generic server methods: the generator emits one call per type argument declared with
/// [GenericArguments], and the server accepts exactly those — a type that only arrives as a name
/// is never resolved at runtime.
/// </summary>
public class GenericServerMethodGenerationTests {

    private const string Source = GeneratorHarness.Usings + @"
public class GenericHub : HARRR { public GenericHub(IServiceProvider sp) : base(sp) { } }
public class GenericMethods : ServerMethods<GenericHub> {
    [GenericArguments(typeof(int), typeof(string))]
    public T Echo<T>(T value) => value;

    [GenericArguments(typeof(int), typeof(string))]
    public string Pair<A, B>(A a, B b) => """";
}";

    private static ServerMethodTable Table() {
        var result = GeneratorHarness.Compile(Source);
        Assert.Empty(result.Errors);
        return ServerTables.For(result.Assembly!.GetType("GenericHub")!, result.Assembly);
    }

    [Fact]
    public void A_declared_type_argument_resolves_by_full_name() {
        var echo = Table().Resolve("GenericMethods.Echo", 1);

        Assert.Equal(1, echo.GenericArity);
        Assert.NotNull(echo.ResolveGeneric!(new[] { "System.Int32" }));
        Assert.NotNull(echo.ResolveGeneric!(new[] { "System.String" }));
    }

    [Fact]
    public void A_short_system_name_resolves_regardless_of_case() {
        Assert.NotNull(Table().Resolve("GenericMethods.Echo", 1).ResolveGeneric!(new[] { "int32" }));
    }

    [Fact]
    public void An_undeclared_type_argument_does_not_resolve() {
        Assert.Null(Table().Resolve("GenericMethods.Echo", 1).ResolveGeneric!(new[] { "System.Guid" }));
    }

    [Fact]
    public void Several_type_parameters_take_one_instantiation_per_attribute() {
        var pair = Table().Resolve("GenericMethods.Pair", 2);

        Assert.Equal(2, pair.GenericArity);
        Assert.NotNull(pair.ResolveGeneric!(new[] { "System.Int32", "System.String" }));
        Assert.Null(pair.ResolveGeneric!(new[] { "System.String", "System.Int32" }));
    }

    [Fact]
    public void A_generic_method_without_declared_type_arguments_fails_the_build() {
        var result = GeneratorHarness.Compile(GeneratorHarness.Usings + @"
public class GenericHub : HARRR { public GenericHub(IServiceProvider sp) : base(sp) { } }
public class GenericMethods : ServerMethods<GenericHub> {
    public T Echo<T>(T value) => value;
}");

        var error = Assert.Single(result.Errors, d => d.Id == "SARRR003");
        Assert.Contains("[GenericArguments", error.GetMessage());
    }

    [Fact]
    public void An_instantiation_with_the_wrong_number_of_types_fails_the_build() {
        var result = GeneratorHarness.Compile(GeneratorHarness.Usings + @"
public class GenericHub : HARRR { public GenericHub(IServiceProvider sp) : base(sp) { } }
public class GenericMethods : ServerMethods<GenericHub> {
    [GenericArguments(typeof(int))]
    public string Pair<A, B>(A a, B b) => """";
}");

        Assert.Contains(result.Errors, d => d.Id == "SARRR003");
    }
}
