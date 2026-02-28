using System;
using System.Collections.Generic;

namespace Cocoar.SignalARRR.ProxyGenerator {
    public class ProxyCreator {

        private static readonly Dictionary<Type, Func<ProxyCreatorHelper, object>> _factories = new();
        private static Func<Type, ProxyCreatorHelper, object>? _fallbackFactory;

        public static void RegisterFactory<T>(Func<ProxyCreatorHelper, T> factory) where T : class
            => _factories[typeof(T)] = helper => factory(helper);

        public static void RegisterFallbackFactory(Func<Type, ProxyCreatorHelper, object> fallback)
            => _fallbackFactory = fallback;

        public static bool HasFactory<T>() where T : class
            => _factories.ContainsKey(typeof(T));

        public static T CreateInstanceFromInterface<T>(ProxyCreatorHelper classCreatorHelper) where T : class {

            if (_factories.TryGetValue(typeof(T), out var factory))
                return (T)factory(classCreatorHelper);

            if (_fallbackFactory is not null)
                return (T)_fallbackFactory(typeof(T), classCreatorHelper);

            throw new InvalidOperationException(
                $"No proxy factory for '{typeof(T).FullName}'. Use [SignalARRRContract] or reference Cocoar.SignalARRR.DynamicProxy.");
        }
    }
}
