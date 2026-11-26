using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading.Tasks;
using Cocoar.SignalARRR.ProxyGenerator;
using Cocoar.SignalARRR.Server.ExtensionMethods;

namespace Cocoar.SignalARRR.Server {

    /// <summary>Calls one server method with the arguments of one message.</summary>
    /// <returns>The result: <c>null</c> for <c>void</c>/<c>Task</c>, the value, or a stream from <see cref="ServerCall.Stream{T}(IAsyncEnumerable{T})"/>.</returns>
    public delegate Task<object?> ServerMethodInvoker(ServerCall call, ArgumentReader arguments);

    /// <summary>
    /// Picks the instantiation of a generic server method for the type arguments a client named,
    /// or returns <c>null</c> when they are not among the ones declared with <c>[GenericArguments]</c>.
    /// </summary>
    public delegate ServerMethodInvoker? ServerGenericResolver(IReadOnlyList<string> typeArguments);

    /// <summary>
    /// One method clients may call, as the source generator describes it: the name it is called by,
    /// how many arguments it accepts, who may call it, and a statically typed call.
    /// </summary>
    /// <remarks>
    /// Generated per hub, <see cref="ServerMethods{T}"/> class and implemented interface; this
    /// replaces the assembly scan and <c>MethodInfo.Invoke</c>, which NativeAOT cannot run.
    /// </remarks>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public sealed class ServerMethodDescriptor {

        public ServerMethodDescriptor(string name, string declaration, int minArguments, int maxArguments,
            SignalARRRAuthorizationPlan authorization, ServerMethodInvoker invoke) {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            Declaration = declaration;
            MinArguments = minArguments;
            MaxArguments = maxArguments;
            Authorization = authorization ?? throw new ArgumentNullException(nameof(authorization));
            Invoke = invoke ?? throw new ArgumentNullException(nameof(invoke));
        }

        public ServerMethodDescriptor(string name, string declaration, int minArguments, int maxArguments,
            SignalARRRAuthorizationPlan authorization, int genericArity, ServerGenericResolver resolveGeneric) {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            Declaration = declaration;
            MinArguments = minArguments;
            MaxArguments = maxArguments;
            Authorization = authorization ?? throw new ArgumentNullException(nameof(authorization));
            GenericArity = genericArity;
            ResolveGeneric = resolveGeneric ?? throw new ArgumentNullException(nameof(resolveGeneric));
        }

        /// <summary>The name a client calls it by: <c>Method</c>, <c>Class.Method</c> or <c>Interface|Method</c>.</summary>
        public string Name { get; }

        /// <summary>
        /// A second accepted name: <c>AssemblyQualifiedName|Method</c> for an interface without
        /// <c>[MessageName]</c>, which callers have always been able to send.
        /// </summary>
        public string? AlternateName { get; init; }

        /// <summary>
        /// The types of the method's arguments and results as they cross the wire. Checked at startup
        /// when reflection-based JSON is off, so a type missing from the app's JSON context fails then
        /// rather than at the first call that needs it.
        /// </summary>
        public IReadOnlyList<Type> WireTypes { get; init; } = Array.Empty<Type>();

        /// <summary>The C# declaration, for error messages.</summary>
        public string Declaration { get; }

        public int MinArguments { get; }

        public int MaxArguments { get; }

        public SignalARRRAuthorizationPlan Authorization { get; }

        /// <summary>The call of a non-generic method; <c>null</c> for a generic one.</summary>
        public ServerMethodInvoker? Invoke { get; }

        /// <summary>How many type parameters a generic method has; 0 otherwise.</summary>
        public int GenericArity { get; }

        public ServerGenericResolver? ResolveGeneric { get; }

        internal bool Accepts(int argumentCount) => argumentCount >= MinArguments && argumentCount <= MaxArguments;

        internal bool Overlaps(ServerMethodDescriptor other) => MinArguments <= other.MaxArguments && other.MinArguments <= MaxArguments;
    }
}
