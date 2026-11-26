using System;
using System.Collections.Generic;

namespace Cocoar.SignalARRR.Common.Attributes {
    /// <summary>
    /// Declares the type arguments a generic server method may be called with.
    /// </summary>
    /// <remarks>
    /// A client names the type argument of a generic call as a string, and the server has to turn
    /// that name into a call. Resolving it at runtime needs reflection and code generation, which
    /// NativeAOT does not have, and it let a caller pick any loaded type. The source generator
    /// instead emits one statically typed call per declared instantiation, and the server accepts
    /// exactly those.
    /// <para>
    /// For a method with one type parameter, every type listed is one allowed instantiation:
    /// <c>[GenericArguments(typeof(Order), typeof(Invoice))]</c> allows <c>Load&lt;Order&gt;</c>
    /// and <c>Load&lt;Invoice&gt;</c>. For a method with several type parameters, each attribute
    /// lists one instantiation in parameter order, and the attribute may be repeated.
    /// </para>
    /// <para>
    /// A type is matched by its <see cref="Type.FullName"/>, which is what the .NET client sends, and
    /// a name without a dot also as <c>System.&lt;Name&gt;</c> regardless of case, which some Kotlin
    /// contracts send (<c>@GenericArguments("String")</c>).
    /// </para>
    /// </remarks>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
    public sealed class GenericArgumentsAttribute : Attribute {

        public GenericArgumentsAttribute(params Type[] types) {
            Types = types ?? Array.Empty<Type>();
        }

        /// <summary>The declared types, see the remarks for how they map to type parameters.</summary>
        public IReadOnlyList<Type> Types { get; }
    }
}
