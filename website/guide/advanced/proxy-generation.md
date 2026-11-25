---
description: "The Roslyn source generator behind typed proxies: setup, [SignalARRRContract], generated proxies and dispatchers, naming, return type classification, multi-assembly support, NativeAOT, the ProxyCreator API"
---

# Proxy Generation

SignalARRR uses a Roslyn source generator to produce, at compile time, a typed proxy for every call you make through a contract and a dispatcher for every call you receive through one. Neither side uses reflection, which is what lets the .NET client run under NativeAOT.

## How it works

1. Mark an interface with `[SignalARRRContract]`
2. The source generator finds it during build
3. A proxy class is generated that implements the interface, and a dispatcher that calls an implementation of it
4. A module initializer registers the proxy in `ProxyCreator` and the dispatcher in `DispatcherRegistry`
5. `GetTypedMethods<T>()` returns the generated proxy; `RegisterInterface<T>(...)` receives calls through the generated dispatcher

## Setup

Reference `Cocoar.SignalARRR.Contracts` in your shared interface project:

```xml
<PackageReference Include="Cocoar.SignalARRR.Contracts" Version="6.*" />
```

This package brings:
- The `[SignalARRRContract]` attribute
- The `ProxyCreator` and `ProxyCreatorHelper` base classes

The Roslyn source generator is not part of it. It ships as `Cocoar.SignalARRR.SourceGenerator`, a dependency of `Cocoar.SignalARRR.Server`, `Cocoar.SignalARRR.Client` and `Cocoar.SignalARRR.Client.FullFramework`, so it runs in your server and client projects, where the proxies are used. The shared interface project contains only its interfaces. A project that references both Server and Client still runs the generator once, because NuGet resolves the shared package once.

## Mark interfaces

```csharp
[SignalARRRContract]
public interface IChatHub
{
    Task SendMessage(string user, string message);
    Task<List<string>> GetHistory();
    IAsyncEnumerable<string> StreamMessages(CancellationToken ct);
}
```

## Generated code

For `IChatHub`, the generator produces a proxy, a dispatcher and their registration.

**Proxy class** (`IChatHub.SignalARRRProxy.g.cs`):
```csharp
internal sealed class ChatHubProxy : IChatHub
{
    private readonly ProxyCreatorHelper _helper;
    private const string Prefix = "MyNamespace.IChatHub";

    public ChatHubProxy(ProxyCreatorHelper helper) => _helper = helper;

    public Task SendMessage(string user, string message) =>
        _helper.SendAsync(Prefix + "|SendMessage",
            new object[] { user, message }, Array.Empty<string>());

    public Task<List<string>> GetHistory() =>
        _helper.InvokeAsync<List<string>>(Prefix + "|GetHistory",
            Array.Empty<object>(), Array.Empty<string>());

    public IAsyncEnumerable<string> StreamMessages(CancellationToken ct) =>
        _helper.StreamAsync<string>(Prefix + "|StreamMessages",
            Array.Empty<object>(), Array.Empty<string>());
}
```

**Dispatcher** (`IChatHub.SignalARRRDispatcher.g.cs`), the receiving side — shortened:
```csharp
internal sealed class ChatHubDispatcher : ContractDispatcher<IChatHub>
{
    public override async Task<object?> InvokeAsync(IChatHub target, string method, ArgumentReader args)
    {
        switch (method)
        {
            case "SendMessage":
                if (args.Count == 2)
                {
                    await target.SendMessage(args.Read<string>(0), args.Read<string>(1));
                    return null;
                }
                throw ArgumentCountMismatch(method, args.Count, "2");
            case "GetHistory":
                if (args.Count == 0) return await target.GetHistory();
                throw ArgumentCountMismatch(method, args.Count, "0");
            // ...
        }
        throw MethodNotFound(method);
    }
}
```

A server-to-client call is a switch on the method name and the argument count, statically typed reads of the arguments and a direct call — no `MethodInfo.Invoke`. It keeps the rules the wire has always had:

- Trailing parameters with a default value may be omitted by the caller, and overloads are told apart by argument count. Two members reachable under the same name with an overlapping argument count cannot be told apart, because the wire carries no parameter types.
- Members inherited from base interfaces are receivable; a member the contract declares itself hides inherited members of the same wire name. `[MessageName]` renames as on the sending side.
- Return types `void`, `Task`, `Task<T>`, a synchronous value, `IAsyncEnumerable<T>`, `ChannelReader<T>` and `IObservable<T>`; `CancellationToken` and `Stream` parameters.
- Generic methods cannot be received: their type argument arrives only as a name, and only reflection can turn that into a call. See [Server-to-Client Handlers](/guide/dotnet-client/server-to-client#generic-methods-cannot-be-received).

A contract that breaks one of these rules still compiles — it may only be called, not received — and registering it on a client throws with the reason.

**Registration** (`SignalARRRProxyRegistration.g.cs`):
```csharp
internal static class SignalARRRProxyRegistration
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        ProxyCreator.RegisterFactory<IChatHub>(
            helper => new ChatHubProxy(helper));
        DispatcherRegistry.Register<IChatHub>(new ChatHubDispatcher());
    }
}
```

The module initializer runs when the assembly loads, making proxy and dispatcher available immediately. On netstandard2.0 and .NET Framework, which lack `ModuleInitializerAttribute`, the generator adds it as an internal polyfill.

The generated code needs C# 10 or later. A project compiling with an older language version — the default for .NET Framework projects — gets error `SARRR001`; set `<LangVersion>10</LangVersion>` or `latest`.

## Proxy naming

The generator strips the leading `I` from interface names:

| Interface | Proxy class | Dispatcher class |
|-----------|-------------|------------------|
| `IChatHub` | `ChatHubProxy` | `ChatHubDispatcher` |
| `IAdminService` | `AdminServiceProxy` | `AdminServiceDispatcher` |
| `IMyContract` | `MyContractProxy` | `MyContractDispatcher` |

## Return type classification

The generator classifies return types to determine the correct proxy method:

| Return type | Proxy call | Protocol |
|-------------|------------|----------|
| `void` | `Send()` | `SendMessage` |
| `Task` | `SendAsync()` | `SendMessage` |
| `T` (sync) | `Invoke<T>()` | `InvokeMessageResult` |
| `Task<T>` | `InvokeAsync<T>()` | `InvokeMessageResult` |
| `IAsyncEnumerable<T>` | `StreamAsync<T>()` | `StreamMessage` |
| `IObservable<T>` | `StreamAsync<T>()` → `ToObservable()` | `StreamMessage` |
| `ChannelReader<T>` | `StreamAsync<T>()` → `ToChannelReader()` | `StreamMessage` |

## Multi-assembly support

The generator covers `[SignalARRRContract]` interfaces declared in the project itself and in every assembly it references at compile time. A server or client project that references a shared contracts library generates the proxies for that library's interfaces and registers them in its own module initializer:

```
SharedContracts.dll  →  IChatHub, IAdminHub          (interfaces only)
MyServer.dll         →  ChatHubProxy, AdminHubProxy  (generated and registered here)
```

Interfaces that are only known at runtime, for example from plugins loaded later, are not covered: an interface needs `[SignalARRRContract]` and must be visible to the compiler of a project that runs the generator. There is no runtime fallback — `GetTypedMethods<T>()` and `RegisterInterface<T>(...)` throw for an interface without generated code.

## NativeAOT

The .NET client (`Cocoar.SignalARRR.Client`) runs under NativeAOT and trimming. Proxies and dispatchers are generated, so the one thing an AOT app adds is JSON metadata for its own argument and result types: a `JsonSerializerContext`, inserted at the front of the hub protocol's resolver chain.

```csharp
[JsonSerializable(typeof(LiveNudge))]
[JsonSerializable(typeof(List<string>))]
partial class AppJson : JsonSerializerContext { }

var connection = HARRRConnection.Create(b => b
    .WithUrl(url)
    .AddJsonProtocol(o => o.PayloadSerializerOptions.TypeInfoResolverChain.Insert(0, AppJson.Default)));
```

SignalARRR appends the metadata for its own envelope types (`SignalARRRJsonContext`) at the end of the chain itself. No `TrimmerRootAssembly` or other workaround is needed.

- **A missing type fails at start, not mid-call.** When reflection-based JSON is off — as under AOT — `StartAsync` checks that every type the registered contracts send or receive has a JSON contract, and throws listing all that do not.
- **JIT apps change nothing.** Where reflection-based JSON is enabled, the reflection resolver stays last in the chain and serves every type no context covers.
- **MessagePack is not supported under AOT.** The MessagePack hub protocol (MessagePack 2.x) generates code at runtime; use JSON in an AOT app. In JIT apps MessagePack keeps working.
- **The server is not AOT-capable yet.** `Cocoar.SignalARRR.Server` still dispatches through reflection. The wire protocol is the same, so an AOT client talks to any server.

The repository's CI publishes a smoke-test client with `PublishAot` (`src/tests/Cocoar.SignalARRR.AotSmoke`, run by `scripts/run-aot-smoke.sh`) and fails on any ILC warning from a SignalARRR assembly.

## ProxyCreator API

| Method | Description |
|--------|-------------|
| `RegisterFactory<T>(factory)` | Register a compiled proxy factory |
| `HasFactory<T>()` | Check if a proxy factory exists for `T` |
| `CreateInstanceFromInterface<T>(helper)` | Create a proxy instance |

## Next steps

- [Typed Methods](/guide/dotnet-client/typed-methods) — use generated proxies on the client
- [Server-to-Client Handlers](/guide/dotnet-client/server-to-client) — receive calls through generated dispatchers
- [Getting Started](/guide/getting-started) — full setup walkthrough
- [Packages](/reference/packages) — which packages to reference