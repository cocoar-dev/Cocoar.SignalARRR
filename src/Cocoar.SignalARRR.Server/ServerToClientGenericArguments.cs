using System;

namespace Cocoar.SignalARRR.Server {
    /// <summary>
    /// The one rule for generic methods in the server-to-client direction: there are none.
    /// </summary>
    /// <remarks>
    /// A call to a generic client method would carry its type argument as a name, and no SignalARRR
    /// client resolves one: the .NET clients receive through generated dispatchers since 6.0, which
    /// can only call what is known at compile time, and the Swift, Kotlin and TypeScript clients
    /// never evaluated <c>GenericArguments</c>. Sending it anyway would fail on the client, after
    /// the fact; failing here names the method on the side that wrote the call. Client-to-server
    /// generic methods are unaffected.
    /// </remarks>
    internal static class ServerToClientGenericArguments {

        public static void Reject(string methodName, string[]? genericArguments) {
            if (genericArguments == null || genericArguments.Length == 0) {
                return;
            }

            throw new NotSupportedException(
                $"'{methodName}' is a generic method, and server-to-client calls cannot carry generic arguments: " +
                "a client cannot resolve a type argument that arrives as a name. Declare the client method with concrete types.");
        }
    }
}
