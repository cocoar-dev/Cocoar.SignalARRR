---
description: "The Roslyn source generator behind typed proxies and server dispatch: setup, [SignalARRRContract], generated proxies and dispatchers, the server's method table, naming, return type classification, multi-assembly support, NativeAOT for client and server, the ProxyCreator API"
---

# Proxy Generation

SignalARRR uses a Roslyn source generator to produce, at compile time, a typed proxy for every call you make through a contract and a dispatcher for every call you receive through one. On the server it also produces the [table of methods](#server-method-table) clients may call on each hub. None of this uses reflection, which is what lets the .NET client and the server run under NativeAOT.

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

## Server method table

In a project that references `Cocoar.SignalARRR.Server`, the generator also covers every hub (a class deriving from `HARRR`) and every `ServerMethods<THub>` class. For each method a client may call it emits the wire name, the argument counts it accepts, a statically typed call and the `[Authorize]`/`[AllowAnonymous]` plan, resolved at compile time (`SignalARRRServerDispatch.g.cs`). A module initializer registers it; `AddSignalARRR` builds each hub's table from it for the assemblies selected with `AddServerMethodsFrom`, and the hub calls the generated delegate instead of `MethodInfo.Invoke`.

The table follows the rules described in [Server Methods](/guide/server/server-methods#method-naming) and [Authorization](/guide/server/authorization). What the wire cannot tell apart fails the build:

| Error | Cause |
|---|---|
| `SARRR002` | Two methods reachable under the same wire name with overlapping argument counts, also through omitted default parameters or two base interfaces declaring the same member |
| `SARRR003` | A generic method without [`[GenericArguments]`](/guide/server/server-methods#generic-methods), or an attribute with the wrong number of types |
| `SARRR004` | A `[MessageName]` that is empty or contains `\|` |

An assembly passed to `AddServerMethodsFrom` that was built without the generator has no table and fails at startup, naming it. There is no reflection fallback.

## NativeAOT

The .NET client (`Cocoar.SignalARRR.Client`) and the server (`Cocoar.SignalARRR.Server`, with the Redis and Postgres backplanes) run under NativeAOT and trimming. Proxies, dispatchers and the server's method table are generated, so the one thing an AOT app adds is JSON metadata for its own argument and result types: a `JsonSerializerContext`, inserted at the front of the hub protocol's resolver chain.

```csharp
[JsonSerializable(typeof(LiveNudge))]
[JsonSerializable(typeof(List<string>))]
partial class AppJson : JsonSerializerContext { }

// Client
var connection = HARRRConnection.Create(b => b
    .WithUrl(url)
    .AddJsonProtocol(o => o.PayloadSerializerOptions.TypeInfoResolverChain.Insert(0, AppJson.Default)));

// Server
builder.Services.AddSignalR()
    .AddJsonProtocol(o => o.PayloadSerializerOptions.TypeInfoResolverChain.Insert(0, AppJson.Default));
builder.Services.AddSignalARRR(b => b.AddServerMethodsFrom(typeof(ChatHub).Assembly));
```

SignalARRR appends the metadata for its own types (`SignalARRRJsonContext`) behind the app's itself. No `TrimmerRootAssembly` or other workaround is needed.

- **A missing type fails at start, not mid-call.** When reflection-based JSON is off — as under AOT — the client's `StartAsync` checks that every type the registered contracts send or receive has a JSON contract, and the server checks every argument and result type of its methods at startup; both throw listing all that are missing.
- **JIT apps change nothing.** Where reflection-based JSON is enabled, the reflection resolver stays last in the chain and serves every type no context covers.
- **MessagePack is not supported under AOT.** The MessagePack hub protocol (MessagePack 2.x, and SignalR's server side of it) generates code at runtime; use JSON in an AOT app. In JIT apps MessagePack keeps working.
- **Generic server methods declare their type arguments.** A generic call can only reach an instantiation generated at compile time — see [`[GenericArguments]`](/guide/server/server-methods#generic-methods).
- **Backplane and cluster subjects.** The backplane's own envelopes come with SignalARRR; the event type of a cluster subject goes into the app's `JsonSerializerContext` — see [Backplane & Clustering](/guide/server/backplane#nativeaot).
- **AOT and JIT mix freely.** The wire protocol is the same, so an AOT client talks to any server and an AOT server to any client.

Under AOT, SignalR invokes the hub's entry points through reflection and wraps exceptions in `TargetInvocationException`; SignalARRR registers a hub filter that unwraps them, so structured error codes such as `unauthorized` still reach clients.

The repository's CI publishes a smoke-test client and server with `PublishAot` (`src/tests/Cocoar.SignalARRR.AotSmoke`, `src/tests/Cocoar.SignalARRR.AotSmokeServer`, run by `scripts/run-aot-smoke.sh`), runs the native client against the native server and against the IntegrationTestServer, and fails on any ILC warning from a SignalARRR assembly.

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