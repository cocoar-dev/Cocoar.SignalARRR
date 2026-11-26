using System.Collections.Generic;
using System.Linq;
using System.Text;
using Cocoar.SignalARRR.SourceGenerator.Helpers;
using Microsoft.CodeAnalysis;

namespace Cocoar.SignalARRR.SourceGenerator.Server;

/// <summary>
/// Builds the server's method tables from the hubs and <c>ServerMethods&lt;THub&gt;</c> classes of
/// one compilation and emits them as code: one descriptor per callable method, with a statically
/// typed call and the authorization plan resolved at compile time.
/// </summary>
/// <remarks>
/// Every rule here mirrors what the reflection registry did at startup, so that the wire names,
/// the accepted argument counts and the authorization outcome stay exactly what they were:
/// <list type="bullet">
/// <item>Hub methods: the public instance methods the hub declares itself (not those of a base hub),
/// named by <c>[MessageName]</c> or the C# name.</item>
/// <item><c>ServerMethods</c> methods: the public instance methods of the class and its own base
/// classes, named <c>Class.Method</c> (<c>[MessageName]</c> on either part).</item>
/// <item>Interfaces the class declares directly: their methods and those of their base interfaces
/// (a member the interface declares itself hides inherited ones of the same wire name), named
/// <c>Interface|Method</c>, called on the implementation.</item>
/// <item><c>CancellationToken</c> and <c>[FromServices]</c> parameters take no argument slot; trailing
/// parameters with defaults may be omitted.</item>
/// <item>Authorization: <c>[AllowAnonymous]</c> anywhere wins; otherwise the most specific level that
/// carries data decides — the method with its interface declarations, then the class chain, then
/// the hub.</item>
/// </list>
/// Two changes on purpose: an override of a <c>Hub</c>/<c>object</c> member on a hub
/// (<c>OnConnectedAsync</c>, <c>ToString</c>) is no longer callable by clients, and two methods
/// reachable under one name with overlapping argument counts fail the build instead of the startup.
/// </remarks>
internal sealed class ServerTableBuilder {

    private static readonly SymbolDisplayFormat TypeFormat = SymbolDisplayFormat.FullyQualifiedFormat
        .AddMiscellaneousOptions(SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

    private readonly Compilation _compilation;
    private readonly INamedTypeSymbol _harrr;
    private readonly INamedTypeSymbol? _serverMethods;
    private readonly INamedTypeSymbol? _serverMethodsOfT;
    private readonly INamedTypeSymbol? _hub;
    private readonly INamedTypeSymbol? _authorizeData;
    private readonly INamedTypeSymbol? _allowAnonymous;
    private readonly INamedTypeSymbol? _fromServices;
    private readonly List<Diagnostic> _diagnostics = new();
    private readonly Dictionary<string, string> _plans = new();
    private readonly StringBuilder _planFields = new();

    private ServerTableBuilder(Compilation compilation, INamedTypeSymbol harrr) {
        _compilation = compilation;
        _harrr = harrr;
        _serverMethods = compilation.GetTypeByMetadataName("Cocoar.SignalARRR.Server.ServerMethods");
        _serverMethodsOfT = compilation.GetTypeByMetadataName("Cocoar.SignalARRR.Server.ServerMethods`1");
        _hub = compilation.GetTypeByMetadataName("Microsoft.AspNetCore.SignalR.Hub");
        _authorizeData = compilation.GetTypeByMetadataName("Microsoft.AspNetCore.Authorization.IAuthorizeData");
        _allowAnonymous = compilation.GetTypeByMetadataName("Microsoft.AspNetCore.Authorization.IAllowAnonymous");
        _fromServices = compilation.GetTypeByMetadataName("Microsoft.AspNetCore.Mvc.FromServicesAttribute");
    }

    public IReadOnlyList<Diagnostic> Diagnostics => _diagnostics;

    /// <summary>The generated source, or <c>null</c> when the compilation has no hub and no ServerMethods class.</summary>
    public static (string? Source, IReadOnlyList<Diagnostic> Diagnostics) Build(Compilation compilation) {
        var harrr = compilation.GetTypeByMetadataName("Cocoar.SignalARRR.Server.HARRR");
        if (harrr == null) return (null, System.Array.Empty<Diagnostic>());

        var builder = new ServerTableBuilder(compilation, harrr);
        return (builder.Emit(), builder.Diagnostics);
    }

    private sealed class Entry {
        public string Name = "";
        public string? AlternateNameExpression;
        public string Declaration = "";
        public IMethodSymbol Method = null!;           // the implementation
        public INamedTypeSymbol ReflectedType = null!; // the hub or ServerMethods class
        public INamedTypeSymbol? CallThrough;          // the interface the call goes through, if any
        public IMethodSymbol? InterfaceMember;         // its member: an explicit implementation is only callable by that name
        public int Min;
        public int Max;
        public Location? Location;
    }

    private string? Emit() {
        var hubs = new List<INamedTypeSymbol>();
        var serverMethods = new List<(INamedTypeSymbol Type, INamedTypeSymbol Hub)>();

        foreach (var type in AllTypes(_compilation.Assembly.GlobalNamespace)) {
            if (type.TypeKind != TypeKind.Class || type.IsAbstract || type.IsGenericType || !IsAccessible(type)) continue;

            if (InheritsFrom(type, _harrr)) {
                hubs.Add(type);
            } else if (HubOfServerMethods(type) is { } hub) {
                serverMethods.Add((type, hub));
            }
        }

        if (hubs.Count == 0 && serverMethods.Count == 0) return null;

        var registrations = new StringBuilder();
        var entriesByHub = new Dictionary<INamedTypeSymbol, List<Entry>>(SymbolEqualityComparer.Default);

        foreach (var hub in hubs) {
            var entries = new List<Entry>();
            foreach (var method in hub.GetMembers().OfType<IMethodSymbol>()) {
                if (!IsCallable(method) || OverridesInfrastructure(method)) continue;
                entries.Add(CreateEntry(Checked(MethodMessageName(method), method) ?? method.Name, method, hub, callThrough: null));
            }
            entries.AddRange(InterfaceEntries(hub));
            Track(entriesByHub, hub, entries);
            registrations.AppendLine($"        global::Cocoar.SignalARRR.Server.ServerDispatchRegistry.RegisterHub<{Fq(hub)}>({EmitDescriptors(entries)});");
        }

        foreach (var (type, hub) in serverMethods) {
            var root = Checked(MessageName(type), type) ?? type.Name;
            var entries = new List<Entry>();
            foreach (var method in ServerMethodsMethods(type)) {
                entries.Add(CreateEntry($"{root}.{Checked(MethodMessageName(method), method) ?? method.Name}", method, type, callThrough: null));
            }
            entries.AddRange(InterfaceEntries(type));
            Track(entriesByHub, hub, entries);
            registrations.AppendLine($"        global::Cocoar.SignalARRR.Server.ServerDispatchRegistry.RegisterServerMethods<{Fq(type)}>(typeof({Fq(hub)}), {EmitDescriptors(entries)});");
        }

        foreach (var pair in entriesByHub) {
            ReportAmbiguities(pair.Value);
        }

        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("// The methods clients may call on the hubs of this assembly, generated by SignalARRR.");
        sb.AppendLine("#nullable enable");
        sb.AppendLine("#pragma warning disable CS1998 // Synchronous methods are wrapped in async lambdas.");
        sb.AppendLine("#pragma warning disable CS0618 // Obsolete members stay callable, as they were through reflection.");
        sb.AppendLine();
        sb.AppendLine("namespace SignalARRR.Generated");
        sb.AppendLine("{");
        sb.AppendLine("    internal static class SignalARRRServerDispatch");
        sb.AppendLine("    {");
        sb.Append(_planFields);
        sb.AppendLine();
        sb.AppendLine("#pragma warning disable CA2255 // The 'ModuleInitializer' attribute should not be used in libraries");
        sb.AppendLine("        [global::System.Runtime.CompilerServices.ModuleInitializer]");
        sb.AppendLine("        internal static void Initialize()");
        sb.AppendLine("#pragma warning restore CA2255");
        sb.AppendLine("        {");
        sb.Append(registrations);
        sb.AppendLine("        }");
        sb.AppendLine("    }");
        sb.AppendLine("}");
        return sb.ToString();
    }

    private static void Track(Dictionary<INamedTypeSymbol, List<Entry>> byHub, INamedTypeSymbol hub, List<Entry> entries) {
        if (!byHub.TryGetValue(hub, out var list)) {
            list = new List<Entry>();
            byHub[hub] = list;
        }
        list.AddRange(entries);
    }

    // ---------------------------------------------------------------- discovery

    private static IEnumerable<INamedTypeSymbol> AllTypes(INamespaceSymbol ns) {
        foreach (var type in ns.GetTypeMembers()) {
            foreach (var t in WithNested(type)) yield return t;
        }
        foreach (var child in ns.GetNamespaceMembers()) {
            foreach (var t in AllTypes(child)) yield return t;
        }
    }

    private static IEnumerable<INamedTypeSymbol> WithNested(INamedTypeSymbol type) {
        yield return type;
        foreach (var nested in type.GetTypeMembers()) {
            foreach (var t in WithNested(nested)) yield return t;
        }
    }

    private bool IsAccessible(INamedTypeSymbol type) => _compilation.IsSymbolAccessibleWithin(type, _compilation.Assembly);

    private static bool InheritsFrom(INamedTypeSymbol type, INamedTypeSymbol baseType) {
        for (var current = type.BaseType; current != null; current = current.BaseType) {
            if (SymbolEqualityComparer.Default.Equals(current, baseType)) return true;
        }
        return false;
    }

    private INamedTypeSymbol? HubOfServerMethods(INamedTypeSymbol type) {
        if (_serverMethodsOfT == null) return null;
        for (var current = type.BaseType; current != null; current = current.BaseType) {
            if (SymbolEqualityComparer.Default.Equals(current.OriginalDefinition, _serverMethodsOfT)) {
                return current.TypeArguments[0] is INamedTypeSymbol hub && InheritsFrom(hub, _harrr) ? hub : null;
            }
        }
        return null;
    }

    private bool IsInfrastructure(INamedTypeSymbol? type) =>
        type == null
        || type.SpecialType == SpecialType.System_Object
        || SymbolEqualityComparer.Default.Equals(type, _harrr)
        || SymbolEqualityComparer.Default.Equals(type, _hub)
        || SymbolEqualityComparer.Default.Equals(type, _serverMethods)
        || SymbolEqualityComparer.Default.Equals(type.OriginalDefinition, _serverMethodsOfT);

    private static bool IsCallable(IMethodSymbol method) =>
        method.MethodKind == MethodKind.Ordinary
        && method.DeclaredAccessibility == Accessibility.Public
        && !method.IsStatic
        && method.Parameters.All(p => p.RefKind == RefKind.None);

    private bool OverridesInfrastructure(IMethodSymbol method) {
        for (var overridden = method.OverriddenMethod; overridden != null; overridden = overridden.OverriddenMethod) {
            if (IsInfrastructure(overridden.ContainingType)) return true;
        }
        return false;
    }

    /// <summary>The class's public methods and those of its own base classes, most derived first.</summary>
    private IEnumerable<IMethodSymbol> ServerMethodsMethods(INamedTypeSymbol type) {
        var overridden = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default);
        var seenSignatures = new HashSet<string>();
        for (var current = type; current != null && !IsInfrastructure(current); current = current.BaseType) {
            foreach (var method in current.GetMembers().OfType<IMethodSymbol>()) {
                if (!IsCallable(method) || overridden.Contains(method) || OverridesInfrastructure(method)) continue;
                for (var o = method.OverriddenMethod; o != null; o = o.OverriddenMethod) overridden.Add(o);
                // A method hidden with 'new' in a derived class is not reachable a second time.
                if (!seenSignatures.Add(Signature(method))) continue;
                yield return method;
            }
        }
    }

    private static string Signature(IMethodSymbol method) =>
        method.Name + "`" + method.TypeParameters.Length + "(" + string.Join(",", method.Parameters.Select(p => p.Type.ToDisplayString())) + ")";

    /// <summary>The interfaces a class declares itself, not those of its base class or those another declared interface brings.</summary>
    private static IEnumerable<INamedTypeSymbol> DirectInterfaces(INamedTypeSymbol type) {
        var inherited = type.BaseType?.AllInterfaces ?? ImmutableArrayEmpty;
        foreach (var candidate in type.Interfaces) {
            if (inherited.Contains(candidate, SymbolEqualityComparer.Default)) continue;
            if (type.Interfaces.Any(other => !SymbolEqualityComparer.Default.Equals(other, candidate)
                                             && other.AllInterfaces.Contains(candidate, SymbolEqualityComparer.Default))) continue;
            yield return candidate;
        }
    }

    private static readonly System.Collections.Immutable.ImmutableArray<INamedTypeSymbol> ImmutableArrayEmpty =
        System.Collections.Immutable.ImmutableArray<INamedTypeSymbol>.Empty;

    private IEnumerable<Entry> InterfaceEntries(INamedTypeSymbol type) {
        foreach (var iface in DirectInterfaces(type)) {
            var interfaceMessageName = Checked(MessageName(iface), iface);
            var interfaceName = interfaceMessageName ?? iface.ToDisplayString();

            var own = iface.GetMembers().OfType<IMethodSymbol>().Where(m => m.MethodKind == MethodKind.Ordinary).ToList();
            var ownNames = new HashSet<string>(own.Select(m => MessageName(m) ?? m.Name));
            var members = own.Concat(iface.AllInterfaces
                .SelectMany(b => b.GetMembers().OfType<IMethodSymbol>().Where(m => m.MethodKind == MethodKind.Ordinary))
                .Where(m => !ownNames.Contains(MessageName(m) ?? m.Name)));

            foreach (var member in members) {
                if (member.Parameters.Any(p => p.RefKind != RefKind.None)) continue;
                if (type.FindImplementationForInterfaceMember(member) is not IMethodSymbol implementation) continue;

                var methodName = Checked(MessageName(member), member) ?? member.Name;
                var entry = CreateEntry($"{interfaceName}|{methodName}", implementation, type, member.ContainingType, member);
                if (interfaceMessageName == null) {
                    entry.AlternateNameExpression = $"typeof({Fq(iface)}).AssemblyQualifiedName + \"|{Escape(methodName)}\"";
                }
                yield return entry;
            }
        }
    }

    // ---------------------------------------------------------------- entries

    private Entry CreateEntry(string name, IMethodSymbol implementation, INamedTypeSymbol reflectedType, INamedTypeSymbol? callThrough, IMethodSymbol? interfaceMember = null) {
        var slots = 0;
        var required = 0;
        foreach (var parameter in implementation.Parameters) {
            if (KindOf(parameter) is ParameterKind.CancellationToken or ParameterKind.FromServices) continue;
            slots++;
            if (!parameter.HasExplicitDefaultValue) required = slots;
        }

        return new Entry {
            Name = name,
            Declaration = $"{reflectedType.ToDisplayString()}.{implementation.Name}({string.Join(", ", implementation.Parameters.Select(p => p.Type.ToDisplayString()))})",
            Method = implementation,
            ReflectedType = reflectedType,
            CallThrough = callThrough,
            InterfaceMember = interfaceMember,
            Min = required,
            Max = slots,
            Location = implementation.Locations.FirstOrDefault(l => l.IsInSource) ?? interfaceMember?.Locations.FirstOrDefault(),
        };
    }

    private void ReportAmbiguities(List<Entry> entries) {
        foreach (var group in entries.GroupBy(e => e.Name)) {
            var list = group.ToList();
            for (var i = 1; i < list.Count; i++) {
                for (var j = 0; j < i; j++) {
                    if (list[j].Min <= list[i].Max && list[i].Min <= list[j].Max) {
                        _diagnostics.Add(Diagnostic.Create(SourceGenerator.Diagnostics.AmbiguousServerMethod, list[i].Location,
                            list[j].Declaration, list[i].Declaration, group.Key));
                    }
                }
            }
        }
    }

    private string EmitDescriptors(List<Entry> entries) {
        var sb = new StringBuilder();
        sb.AppendLine("new global::Cocoar.SignalARRR.Server.ServerMethodDescriptor[]");
        sb.AppendLine("        {");
        foreach (var entry in entries) {
            var descriptor = EmitDescriptor(entry);
            if (descriptor != null) sb.AppendLine(descriptor + ",");
        }
        sb.Append("        }");
        return sb.ToString();
    }

    private string? EmitDescriptor(Entry entry) {
        var plan = PlanField(entry);
        var head = $"            new global::Cocoar.SignalARRR.Server.ServerMethodDescriptor({Literal(entry.Name)}, {Literal(entry.Declaration)}, {entry.Min}, {entry.Max}, {plan}";
        var method = entry.Method;

        List<List<ITypeSymbol>>? instantiations = null;
        if (method.TypeParameters.Length > 0) {
            instantiations = Instantiations(method, entry.Location);
            if (instantiations == null) return null;
        }

        var wireTypes = instantiations == null
            ? WireTypes(method)
            : instantiations.SelectMany(types => WireTypes(method.Construct(types.ToArray()))).Distinct().ToList();
        var initializers = new List<string>();
        if (entry.AlternateNameExpression != null) initializers.Add($"AlternateName = {entry.AlternateNameExpression}");
        if (wireTypes.Count > 0) initializers.Add($"WireTypes = new global::System.Type[] {{ {string.Join(", ", wireTypes.Select(t => $"typeof({t})"))} }}");
        var tail = initializers.Count == 0 ? ")" : $") {{ {string.Join(", ", initializers)} }}";

        if (instantiations == null) {
            return $"{head}, {Invoker(entry, method, typeArguments: null)}{tail}";
        }

        var sb = new StringBuilder();
        sb.AppendLine($"{head}, {method.TypeParameters.Length}, static names =>");
        sb.AppendLine("            {");
        foreach (var types in instantiations) {
            var constructed = method.Construct(types.ToArray());
            var typeofs = string.Join(", ", types.Select(t => $"typeof({t.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)})"));
            sb.AppendLine($"                if (global::Cocoar.SignalARRR.Server.ServerGenericArguments.Match(names, {typeofs})) return {Invoker(entry, constructed, types)};");
        }
        sb.AppendLine("                return null;");
        sb.Append($"            }}{tail}");
        return sb.ToString();
    }

    /// <summary>The instantiations declared with [GenericArguments]; reports SARRR003 and returns null when they do not fit.</summary>
    private List<List<ITypeSymbol>>? Instantiations(IMethodSymbol method, Location? location) {
        var attributes = method.GetAttributes()
            .Where(a => a.AttributeClass?.ToDisplayString() == "Cocoar.SignalARRR.Common.Attributes.GenericArgumentsAttribute")
            .ToList();
        var arity = method.TypeParameters.Length;
        var result = new List<List<ITypeSymbol>>();

        foreach (var attribute in attributes) {
            var types = attribute.ConstructorArguments.SelectMany(a => a.Kind == TypedConstantKind.Array ? (IEnumerable<TypedConstant>)a.Values : new[] { a })
                .Select(v => v.Value as ITypeSymbol)
                .ToList();
            if (types.Any(t => t == null)) continue;

            if (arity == 1) {
                result.AddRange(types.Select(t => new List<ITypeSymbol> { t! }));
            } else if (types.Count == arity) {
                result.Add(types!);
            } else {
                _diagnostics.Add(Diagnostic.Create(SourceGenerator.Diagnostics.InvalidGenericArguments, location,
                    method.ToDisplayString(), $"each [GenericArguments] of a method with {arity} type parameters lists exactly {arity} types, one instantiation"));
                return null;
            }
        }

        if (result.Count == 0) {
            _diagnostics.Add(Diagnostic.Create(SourceGenerator.Diagnostics.InvalidGenericArguments, location,
                method.ToDisplayString(), "declare the type arguments it accepts with [GenericArguments(typeof(...))]"));
            return null;
        }

        return result;
    }

    /// <summary>The types that cross the wire for a method: its value parameters and its result or stream items.</summary>
    private List<string> WireTypes(IMethodSymbol method) {
        var types = new List<ITypeSymbol>();
        foreach (var parameter in method.Parameters) {
            if (KindOf(parameter) == ParameterKind.Value) types.Add(parameter.Type);
        }

        var returnType = method.ReturnType;
        if (returnType.SpecialType != SpecialType.System_Void) {
            var (awaited, inner) = UnwrapTask(returnType);
            var value = awaited ? inner : returnType;
            if (value != null) {
                types.Add(IsStream(value) ? ((INamedTypeSymbol)value).TypeArguments[0] : value);
            }
        }

        return types
            .Where(t => t.TypeKind != TypeKind.TypeParameter && t.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) != "global::System.IO.Stream")
            .Select(t => t.WithNullableAnnotation(NullableAnnotation.NotAnnotated).ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat))
            .Distinct()
            .ToList();
    }

    private enum ParameterKind { Value, CancellationToken, FromServices, Stream }

    private ParameterKind KindOf(IParameterSymbol parameter) {
        var type = parameter.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        if (type == "global::System.Threading.CancellationToken") return ParameterKind.CancellationToken;
        if (_fromServices != null && parameter.GetAttributes().Any(a => SymbolEqualityComparer.Default.Equals(a.AttributeClass, _fromServices))) return ParameterKind.FromServices;
        for (var t = parameter.Type as INamedTypeSymbol; t != null; t = t.BaseType) {
            if (t.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == "global::System.IO.Stream") return ParameterKind.Stream;
        }
        return ParameterKind.Value;
    }

    private string Invoker(Entry entry, IMethodSymbol method, List<ITypeSymbol>? typeArguments) {
        var arguments = new List<string>();
        var slot = 0;
        foreach (var parameter in method.Parameters) {
            var type = parameter.Type.ToDisplayString(TypeFormat);
            var name = Literal(parameter.Name);
            switch (KindOf(parameter)) {
                case ParameterKind.CancellationToken:
                    arguments.Add("call.CallCancellation");
                    break;
                case ParameterKind.FromServices:
                    arguments.Add($"call.Service<{parameter.Type.WithNullableAnnotation(NullableAnnotation.NotAnnotated).ToDisplayString(TypeFormat)}>()");
                    break;
                case ParameterKind.Stream:
                    var read = $"(({type})(await call.StreamArgument(args, {slot}, {name}).ConfigureAwait(false)))";
                    arguments.Add(parameter.HasExplicitDefaultValue ? $"(args.Count > {slot} ? {read} : null)" : read);
                    slot++;
                    break;
                default:
                    var value = $"call.Argument<{type}>(args, {slot}, {name})";
                    arguments.Add(parameter.HasExplicitDefaultValue
                        ? $"(args.Count > {slot} ? {value} : ({type})({SymbolExtensions.FormatDefaultValue(parameter)}))"
                        : value);
                    slot++;
                    break;
            }
        }

        var target = InheritsFrom(entry.ReflectedType, _harrr)
            ? $"call.Hub<{Fq(entry.ReflectedType)}>()"
            : $"call.Instance<{Fq(entry.ReflectedType)}>()";
        if (entry.CallThrough != null) {
            target = $"(({Fq(entry.CallThrough)}){target})";
        }

        var typeArgumentList = typeArguments == null ? "" : "<" + string.Join(", ", typeArguments.Select(t => t.ToDisplayString(TypeFormat))) + ">";
        var call = $"{target}.{entry.InterfaceMember?.Name ?? method.Name}{typeArgumentList}({string.Join(", ", arguments)})";

        return $"static async (call, args) => {{ {ReturnStatement(method.ReturnType, call)} }}";
    }

    private static string ReturnStatement(ITypeSymbol returnType, string call) {
        if (returnType.SpecialType == SpecialType.System_Void) return $"{call}; return null;";

        var (awaited, inner) = UnwrapTask(returnType);
        if (awaited && inner == null) return $"await {call}.ConfigureAwait(false); return null;";

        var valueType = inner ?? returnType;
        var value = awaited ? $"(await {call}.ConfigureAwait(false))" : call;
        return IsStream(valueType) ? $"return call.Stream({value});" : $"return {value};";
    }

    /// <summary>Whether the type is Task/ValueTask (awaited) and its result type, if it has one.</summary>
    private static (bool Awaited, ITypeSymbol? Inner) UnwrapTask(ITypeSymbol type) {
        var name = type.OriginalDefinition.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        switch (name) {
            case "global::System.Threading.Tasks.Task":
            case "global::System.Threading.Tasks.ValueTask":
                return (true, null);
            case "global::System.Threading.Tasks.Task<TResult>":
            case "global::System.Threading.Tasks.ValueTask<TResult>":
                return (true, ((INamedTypeSymbol)type).TypeArguments[0]);
            default:
                return (false, null);
        }
    }

    private static bool IsStream(ITypeSymbol type) {
        var name = type.OriginalDefinition.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        return name is "global::System.Collections.Generic.IAsyncEnumerable<T>"
            or "global::System.Threading.Channels.ChannelReader<T>"
            or "global::System.IObservable<T>";
    }

    // ---------------------------------------------------------------- authorization

    private string PlanField(Entry entry) {
        var (allowAnonymous, authorize) = AuthorizationPlan(entry.Method, entry.ReflectedType);
        var expression = $"new global::Cocoar.SignalARRR.Server.ExtensionMethods.SignalARRRAuthorizationPlan({(allowAnonymous ? "true" : "false")}, new global::Microsoft.AspNetCore.Authorization.IAuthorizeData[] {{ {string.Join(", ", authorize)} }})";
        if (!_plans.TryGetValue(expression, out var field)) {
            field = $"Plan{_plans.Count}";
            _plans[expression] = field;
            _planFields.AppendLine($"        private static readonly global::Cocoar.SignalARRR.Server.ExtensionMethods.SignalARRRAuthorizationPlan {field} = {expression};");
        }
        return field;
    }

    private (bool AllowAnonymous, List<string> Authorize) AuthorizationPlan(IMethodSymbol method, INamedTypeSymbol reflectedType) {
        var methodLevel = new List<AttributeData>();
        methodLevel.AddRange(MethodAttributes(method));
        foreach (var declaration in InterfaceDeclarations(method, reflectedType)) {
            methodLevel.AddRange(declaration.GetAttributes());
        }

        var typeLevel = new List<AttributeData>();
        for (var current = reflectedType; current != null && !IsInfrastructure(current); current = current.BaseType) {
            typeLevel.AddRange(current.GetAttributes());
        }

        var hubLevel = new List<AttributeData>();
        if (HubOfServerMethods(reflectedType) is { } hub) {
            for (var current = hub; current != null && !IsInfrastructure(current); current = current.BaseType) {
                hubLevel.AddRange(current.GetAttributes());
            }
        }

        var allowAnonymous = methodLevel.Concat(typeLevel).Concat(hubLevel).Any(a => Implements(a.AttributeClass, _allowAnonymous));

        foreach (var level in new[] { methodLevel, typeLevel, hubLevel }) {
            var data = level.Where(a => Implements(a.AttributeClass, _authorizeData)).Select(AttributeExpression).ToList();
            if (data.Count > 0) return (allowAnonymous, data);
        }

        return (allowAnonymous, new List<string>());
    }

    /// <summary>The method's own attributes, plus inheritable ones from the methods it overrides.</summary>
    private static IEnumerable<AttributeData> MethodAttributes(IMethodSymbol method) {
        foreach (var attribute in method.GetAttributes()) yield return attribute;
        for (var overridden = method.OverriddenMethod; overridden != null; overridden = overridden.OverriddenMethod) {
            foreach (var attribute in overridden.GetAttributes()) {
                if (IsInheritable(attribute.AttributeClass)) yield return attribute;
            }
        }
    }

    private static IEnumerable<IMethodSymbol> InterfaceDeclarations(IMethodSymbol method, INamedTypeSymbol reflectedType) {
        foreach (var iface in reflectedType.AllInterfaces) {
            foreach (var member in iface.GetMembers().OfType<IMethodSymbol>()) {
                var implementation = reflectedType.FindImplementationForInterfaceMember(member) as IMethodSymbol;
                if (implementation != null && (SymbolEqualityComparer.Default.Equals(implementation, method)
                                               || SymbolEqualityComparer.Default.Equals(implementation.OriginalDefinition, method.OriginalDefinition))) {
                    yield return member;
                }
            }
        }
    }

    private static bool Implements(INamedTypeSymbol? type, INamedTypeSymbol? @interface) =>
        type != null && @interface != null && type.AllInterfaces.Contains(@interface, SymbolEqualityComparer.Default);

    private static bool IsInheritable(INamedTypeSymbol? attributeClass) {
        for (var current = attributeClass; current != null; current = current.BaseType) {
            var usage = current.GetAttributes().FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == "System.AttributeUsageAttribute");
            if (usage == null) continue;
            var inherited = usage.NamedArguments.FirstOrDefault(n => n.Key == "Inherited");
            return inherited.Key == null || inherited.Value.Value is not false;
        }
        return true;
    }

    /// <summary>The attribute as a constructor call with the arguments it was declared with.</summary>
    private static string AttributeExpression(AttributeData attribute) {
        var arguments = string.Join(", ", attribute.ConstructorArguments.Select(Constant));
        var named = attribute.NamedArguments.Select(n => $"{n.Key} = {Constant(n.Value)}").ToList();
        var initializer = named.Count == 0 ? "" : " { " + string.Join(", ", named) + " }";
        return $"new {attribute.AttributeClass!.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}({arguments}){initializer}";
    }

    private static string Constant(TypedConstant constant) {
        if (constant.IsNull) return "null";
        switch (constant.Kind) {
            case TypedConstantKind.Array:
                return $"new {constant.Type!.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)} {{ {string.Join(", ", constant.Values.Select(Constant))} }}";
            case TypedConstantKind.Type:
                return $"typeof({((ITypeSymbol)constant.Value!).ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)})";
            case TypedConstantKind.Enum:
                return $"({constant.Type!.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)})({constant.Value})";
            default:
                return constant.Value switch {
                    string s => Literal(s),
                    bool b => b ? "true" : "false",
                    char c => $"'{(c == '\'' ? "\\'" : c == '\\' ? "\\\\" : c.ToString())}'",
                    float f => f.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + "f",
                    double d => d.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + "d",
                    long l => l + "L",
                    ulong ul => ul + "UL",
                    uint ui => ui + "U",
                    _ => $"({constant.Type!.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)})({System.Convert.ToString(constant.Value, System.Globalization.CultureInfo.InvariantCulture)})",
                };
        }
    }

    // ---------------------------------------------------------------- names

    /// <summary>
    /// A declared wire name, reported when it cannot work: the receiver splits on the first '|' to
    /// tell the interface from the method, so a separator inside either half addresses something else.
    /// </summary>
    private string? Checked(string? name, ISymbol symbol) {
        if (name == null) return null;
        if (string.IsNullOrWhiteSpace(name) || name.IndexOf('|') >= 0) {
            _diagnostics.Add(Diagnostic.Create(SourceGenerator.Diagnostics.InvalidWireName,
                symbol.Locations.FirstOrDefault(l => l.IsInSource), name, symbol.ToDisplayString()));
        }
        return name;
    }

    private static string? MessageName(ISymbol symbol) {
        foreach (var attribute in symbol.GetAttributes()) {
            if (attribute.AttributeClass?.ToDisplayString() == "Cocoar.SignalARRR.Common.Attributes.MessageNameAttribute"
                && attribute.ConstructorArguments.Length == 1 && attribute.ConstructorArguments[0].Value is string name) {
                return name;
            }
        }
        return null;
    }

    /// <summary>[MessageName] on a method, also from a method it overrides, as reflection's inherit lookup found it.</summary>
    private static string? MethodMessageName(IMethodSymbol method) {
        for (var current = method; current != null; current = current.OverriddenMethod) {
            if (MessageName(current) is { } name) return name;
        }
        return null;
    }

    private static string Fq(ITypeSymbol type) => type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

    private static string Literal(string value) => "\"" + Escape(value) + "\"";

    private static string Escape(string value) => value.Replace("\\", "\\\\").Replace("\"", "\\\"");
}