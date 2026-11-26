using Cocoar.SignalARRR.Contracts;

namespace Cocoar.SignalARRR.Tests.SharedModels {
    /// <summary>
    /// Generic client-to-server methods. The server declares which type arguments each one accepts
    /// with [GenericArguments]; the client's generated proxy names them by <c>FullName</c>.
    /// </summary>
    [SignalARRRContract]
    public interface IGenericServerMethods {

        T Echo<T>(T value);

        string Pair<TFirst, TSecond>(TFirst first, TSecond second);
    }
}
