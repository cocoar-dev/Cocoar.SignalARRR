# Proxy Generation

SignalARRR uses a dual proxy strategy: compile-time source generation (preferred)
and optional runtime fallback via DispatchProxy.

---

## Source Generator (Compile-Time)

### Setup

Reference `Cocoar.SignalARRR.Contracts` in your shared interface project. This
single package includes:
- The `[SignalARRRContract]` attribute
- The Roslyn source generator (runs at compile time)
- The `ProxyGenerator` dependency (so generated code compiles)

```xml
<PackageReference Include="Cocoar.SignalARRR.Contracts" Version="4.0.0" />
```

### Mark interfaces

```csharp
using Cocoar.SignalARRR.Contracts;

[SignalARRRContract]
public interface IChatHub {
    Task SendMessage(string user, string message);
    Task<List<string>> GetHistory();
    IAsyncEnumerable<string> StreamMessages(CancellationToken ct);
}
```

### What gets generated

For each `[SignalARRRContract]` interface, the source generator produces:

1. **A proxy class** (e.g., `ChatHubProxy` from `IChatHub`) that implements the
   interface and delegates all calls to `ProxyCreatorHelper`
2. **A registration initializer** that registers the proxy factory in
   `ProxyCreator` via `[ModuleInitializer]`

The generated proxy class:
- Is `internal partial` in a `SignalARRR.Generated` namespace
- Strips the leading `I` from the interface name (e.g., `IChatHub` → `ChatHubProxy`)
- Routes each method to the appropriate dispatch: `Send`, `SendAsync`,
  `Invoke<T>`, `InvokeAsync<T>`, `StreamAsync<T>`, etc.
- Extracts `CancellationToken` parameters and passes them separately
- Handles generic methods by passing type arguments as strings

### Method naming convention

Generated proxies use the method name format:
```
FullInterfaceName|MethodName
```

For example, `MyApp.Shared.IChatHub|SendMessage`. This allows the server to
resolve which interface and method to invoke.

### Auto-registration

The `[ModuleInitializer]` runs when the assembly loads — no manual registration
needed. Just call:

```csharp
var proxy = connection.GetTypedMethods<IChatHub>();  // Uses generated proxy
```

---

## DynamicProxy (Runtime Fallback)

For scenarios where interfaces aren't known at compile time (plugin systems,
dynamic loading), use the DynamicProxy package:

```
dotnet add package Cocoar.SignalARRR.DynamicProxy
```

### How it works

- Uses `System.Reflection.DispatchProxy` (BCL, zero dependencies)
- Registers itself as a fallback factory via `[ModuleInitializer]`
- If no source-generated proxy exists for an interface, the fallback creates
  a `DispatchProxy` at runtime
- Marked with `[RequiresDynamicCode]` — requires JIT (not AOT-compatible)

### No code changes needed

Just add the package reference. `GetTypedMethods<T>()` automatically uses:
1. Source-generated proxy (if available) — preferred
2. DynamicProxy fallback (if package referenced) — runtime

### When to use DynamicProxy

| Scenario | Source Generator | DynamicProxy |
|---|---|---|
| Known interfaces at compile time | Yes | Not needed |
| Plugin interfaces loaded at runtime | No | Yes |
| AOT deployment | Yes | No |
| Development/prototyping | Yes | Optional convenience |

---

## Return type dispatch

The proxy routes method calls based on their return type:

```
void           → Send(methodName, args)
T (sync)       → Invoke<T>(methodName, args)
Task           → SendAsync(methodName, args)
Task<T>        → InvokeAsync<T>(methodName, args)
IAsyncEnumerable<T> → StreamAsync<T>(methodName, args, ct)
ChannelReader<T>    → StreamAsync<T>(...).ToChannelReader()
IObservable<T>      → StreamAsync<T>(...).ToObservable()
```

---

## ProxyCreator API

The `ProxyCreator` static class manages the proxy factory registry:

```csharp
// Check if a factory exists for an interface
bool hasProxy = ProxyCreator.HasFactory<IChatHub>();

// Create a proxy instance (used internally by GetTypedMethods<T>)
var proxy = ProxyCreator.CreateInstanceFromInterface<IChatHub>(helper);

// Register a custom factory (advanced)
ProxyCreator.RegisterFactory<IChatHub>(helper => new MyCustomProxy(helper));

// Register a fallback factory (used by DynamicProxy package)
ProxyCreator.RegisterFallbackFactory((type, helper) => CreateDynamic(type, helper));
```

---

## Project-reference caveat

When developing against source (not NuGet packages), Roslyn analyzers don't flow
transitively via `<ProjectReference>`. Projects that define `[SignalARRRContract]`
interfaces need an explicit analyzer reference to the SourceGenerator:

```xml
<!-- Only needed for project-reference development, not when using NuGet -->
<ProjectReference Include="..\Cocoar.SignalARRR.SourceGenerator\Cocoar.SignalARRR.SourceGenerator.csproj"
                  OutputItemType="Analyzer"
                  ReferenceOutputAssembly="false" />
```

The NuGet package handles this automatically — the analyzer is bundled in the
`analyzers/` folder.
