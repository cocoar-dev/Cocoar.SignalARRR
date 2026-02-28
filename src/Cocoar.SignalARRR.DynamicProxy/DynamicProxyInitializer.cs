using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Cocoar.SignalARRR.ProxyGenerator;

namespace Cocoar.SignalARRR.DynamicProxy;

internal static class DynamicProxyInitializer {

#pragma warning disable CA2255 // The 'ModuleInitializer' attribute should not be used in libraries
    [ModuleInitializer]
    [RequiresDynamicCode("DynamicProxy uses DispatchProxy which requires runtime code generation.")]
    internal static void Initialize() {
        ProxyCreator.RegisterFallbackFactory(
            (interfaceType, helper) => SignalARRRDispatchProxy.CreateForType(interfaceType, helper));
    }
#pragma warning restore CA2255
}
