using System.Reflection;
using System.Runtime.CompilerServices;
using Cocoar.SignalARRR.ProxyGenerator;
using Cocoar.SignalARRR.Tests.SharedModels;
using TestShared;
using Xunit;

namespace Cocoar.SignalARRR.Tests;

public class SourceGeneratorProxyTests {
    private readonly ITestOutputHelper _output;

    public SourceGeneratorProxyTests(ITestOutputHelper output) {
        _output = output;
    }

    [Fact]
    public void ModuleInitializer_IsPresent_InTestSharedAssembly() {
        var asm = typeof(ITestHub).Assembly;
        _output.WriteLine($"Assembly: {asm.FullName}");
        _output.WriteLine($"Location: {asm.Location}");

        var regType = asm.GetType("TestShared.SignalARRR.SignalARRRProxyRegistration");
        Assert.NotNull(regType);
        _output.WriteLine($"Registration type found: {regType!.FullName}");

        var initMethod = regType.GetMethod("Initialize",
            BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
        Assert.NotNull(initMethod);
        _output.WriteLine($"Initialize method found: {initMethod!.Name}");

        var attr = initMethod.GetCustomAttribute<ModuleInitializerAttribute>();
        _output.WriteLine($"[ModuleInitializer] attribute: {(attr != null ? "present" : "MISSING")}");
        Assert.NotNull(attr);
    }

    [Fact]
    public void ModuleInitializer_RegistersFactories_WhenCalledExplicitly() {
        // Call the generated initializer explicitly via reflection
        var asm = typeof(ITestHub).Assembly;
        var regType = asm.GetType("TestShared.SignalARRR.SignalARRRProxyRegistration");
        Assert.NotNull(regType);

        var initMethod = regType!.GetMethod("Initialize",
            BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
        Assert.NotNull(initMethod);
        initMethod!.Invoke(null, null);

        // Now verify factories are registered
        Assert.True(ProxyCreator.HasFactory<ITestHub>());
        Assert.True(ProxyCreator.HasFactory<ITestClientMethods>());
        Assert.True(ProxyCreator.HasFactory<ISharedMethods>());
        Assert.True(ProxyCreator.HasFactory<IStringMethods>());
        Assert.True(ProxyCreator.HasFactory<IGeneric>());
    }

    [Fact]
    public void ModuleInitializer_RegistersFactories_ForSharedModels() {
        var asm = typeof(ITestServerMethods).Assembly;
        var regType = asm.GetType("Cocoar.SignalARRR.Tests.SharedModels.SignalARRR.SignalARRRProxyRegistration");
        Assert.NotNull(regType);

        var initMethod = regType!.GetMethod("Initialize",
            BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
        Assert.NotNull(initMethod);
        initMethod!.Invoke(null, null);

        Assert.True(ProxyCreator.HasFactory<ITestServerMethods>());
    }
}
