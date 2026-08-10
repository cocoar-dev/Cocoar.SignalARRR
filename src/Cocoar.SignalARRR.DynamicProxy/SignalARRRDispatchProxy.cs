using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Cocoar.SignalARRR.DynamicProxy;

using ProxyGenerator;

[RequiresDynamicCode("DispatchProxy uses runtime code generation.")]
internal class SignalARRRDispatchProxy : DispatchProxy {

    internal static readonly AsyncLocal<ProxyCreatorHelper?> CurrentHelper = new();

    private ProxyCreatorHelper _helper = null!;
    private Type _interfaceType = null!;

    internal void Initialize(ProxyCreatorHelper helper, Type interfaceType) {
        _helper = helper;
        _interfaceType = interfaceType;
    }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) {
        if (targetMethod is null)
            throw new ArgumentNullException(nameof(targetMethod));

        args ??= Array.Empty<object?>();

        // The registered interface, not targetMethod.DeclaringType: an inherited contract member
        // travels under the interface that was registered (F-7).
        var methodName = Cocoar.SignalARRR.Common.WireName.For(_interfaceType, targetMethod);
        var returnType = targetMethod.ReturnType;

        var genericArguments = targetMethod.IsGenericMethod
            ? targetMethod.GetGenericArguments().Select(a => a.FullName ?? string.Empty).Where(n => !string.IsNullOrEmpty(n)).ToArray()
            : Array.Empty<string>();

        var cancellationToken = args.OfType<CancellationToken>().FirstOrDefault();

        // void
        if (returnType == typeof(void)) {
            _helper.Send(methodName, args!, genericArguments, cancellationToken);
            return null;
        }

        // Task
        if (returnType == typeof(Task)) {
            return _helper.SendAsync(methodName, args!, genericArguments, cancellationToken);
        }

        // Task<T>
        if (returnType.IsGenericType && returnType.GetGenericTypeDefinition() == typeof(Task<>)) {
            var resultType = returnType.GetGenericArguments()[0];
            var invokeMethod = typeof(ProxyCreatorHelper).GetMethod(nameof(ProxyCreatorHelper.InvokeAsync))!.MakeGenericMethod(resultType);
            return invokeMethod.Invoke(_helper, new object[] { methodName, args!, genericArguments, cancellationToken });
        }

        // Streaming types — use element type, not interface type
        if (returnType.IsGenericType) {
            var genericDef = returnType.GetGenericTypeDefinition();
            var elementType = returnType.GetGenericArguments()[0];

            // IAsyncEnumerable<T>
            if (genericDef == typeof(IAsyncEnumerable<>)) {
                var streamMethod = typeof(ProxyCreatorHelper).GetMethod(nameof(ProxyCreatorHelper.StreamAsync))!.MakeGenericMethod(elementType);
                return streamMethod.Invoke(_helper, new object[] { methodName, args!, genericArguments, cancellationToken });
            }

            // ChannelReader<T>
            if (genericDef == typeof(ChannelReader<>)) {
                var streamMethod = typeof(ProxyCreatorHelper).GetMethod(nameof(ProxyCreatorHelper.StreamAsync))!.MakeGenericMethod(elementType);
                var stream = streamMethod.Invoke(_helper, new object[] { methodName, args!, genericArguments, cancellationToken })!;
                var toChannelReader = typeof(ProxyCreatorHelper).GetMethod(nameof(ProxyCreatorHelper.ToChannelReader))!.MakeGenericMethod(elementType);
                return toChannelReader.Invoke(_helper, new object[] { stream, cancellationToken });
            }

            // IObservable<T>
            if (genericDef == typeof(IObservable<>)) {
                var streamMethod = typeof(ProxyCreatorHelper).GetMethod(nameof(ProxyCreatorHelper.StreamAsync))!.MakeGenericMethod(elementType);
                var stream = streamMethod.Invoke(_helper, new object[] { methodName, args!, genericArguments, cancellationToken })!;
                var toObservable = typeof(ProxyCreatorHelper).GetMethod(nameof(ProxyCreatorHelper.ToObservable))!.MakeGenericMethod(elementType);
                return toObservable.Invoke(_helper, new object[] { stream });
            }
        }

        // Sync T
        {
            var invokeMethod = typeof(ProxyCreatorHelper).GetMethod(nameof(ProxyCreatorHelper.Invoke))!.MakeGenericMethod(returnType);
            return invokeMethod.Invoke(_helper, new object[] { methodName, args!, genericArguments, cancellationToken });
        }
    }

    [RequiresDynamicCode("DispatchProxy.Create uses runtime code generation.")]
    internal static object CreateForType(Type interfaceType, ProxyCreatorHelper helper) {
        var createMethod = typeof(DispatchProxy)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .First(m => m.Name == nameof(DispatchProxy.Create) && m.GetGenericArguments().Length == 2)
            .MakeGenericMethod(interfaceType, typeof(SignalARRRDispatchProxy));

        var proxy = createMethod.Invoke(null, null)!;
        ((SignalARRRDispatchProxy)proxy).Initialize(helper, interfaceType);
        return proxy;
    }
}
