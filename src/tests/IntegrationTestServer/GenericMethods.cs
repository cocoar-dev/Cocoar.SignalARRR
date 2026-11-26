using Cocoar.SignalARRR.Common.Attributes;
using Cocoar.SignalARRR.Server;
using Cocoar.SignalARRR.Tests.SharedModels;
using TestShared;

namespace IntegrationTestServer {

    /// <summary>Generic client-to-server methods with declared type arguments.</summary>
    public class GenericMethods : ServerMethods<TestHub>, IGenericServerMethods {

        [GenericArguments(typeof(int), typeof(string), typeof(ComplexTestClass))]
        public T Echo<T>(T value) => value;

        // Several type parameters: one attribute per allowed instantiation, in parameter order.
        [GenericArguments(typeof(int), typeof(string))]
        [GenericArguments(typeof(string), typeof(int))]
        public string Pair<TFirst, TSecond>(TFirst first, TSecond second) =>
            $"{typeof(TFirst).Name}:{first}|{typeof(TSecond).Name}:{second}";
    }
}
