---
description: "The Roslyn source generator behind typed proxies: setup, [SignalARRRContract], generated code and naming, return type classification, multi-assembly support, the DynamicProxy fallback, the ProxyCreator API"
---

# Proxy Generation

SignalARRR uses a Roslyn source generator to produce typed proxy classes at compile time. This enables zero-reflection RPC calls and is AOT-compatible.

## How it works

1. Mark an interface with `[SignalARRRContract]`
2. The source generator finds it during build
3. A proxy class is generated that implements the interface
4. A module initializer registers the proxy in `ProxyCreator`
5. `GetTypedMethods<T>()` returns the generated proxy

## Setup

Reference `Cocoar.SignalARRR.Contracts` in your shared interface project:

```xml
<PackageReference Include="Cocoar.SignalARRR.Contracts" Version="5.*" />
```

This package brings:
- The `[SignalARRRContract]` attribute
- The `ProxyCreator` and `ProxyCreatorHelper` base classes

The Roslyn source generator is not part of it. It ships as `Cocoar.SignalARRR.SourceGenerator`, a dependency of `Cocoar.SignalARRR.Server` and `Cocoar.SignalARRR.Client`, so it runs in your server and client projects, where the proxies are used. The shared interface project contains only its interfaces. A project that references both Server and Client still runs the generator once, because NuGet resolves the shared package once.

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

For `IChatHub`, the generator produces:

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

**Registration** (`SignalARRRProxyRegistration.g.cs`):
```csharp
internal static class SignalARRRProxyRegistration
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        ProxyCreator.RegisterFactory<IChatHub>(
            helper => new ChatHubProxy(helper));
    }
}
```

The module initializer runs when the assembly loads, making the proxy available immediately.

## Proxy naming

The generator strips the leading `I` from interface names:

| Interface | Proxy class |
|-----------|-------------|
| `IChatHub` | `ChatHubProxy` |
| `IAdminService` | `AdminServiceProxy` |
| `IMyContract` | `MyContractProxy` |

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

Interfaces that are only known at runtime, for example from plugins loaded later, are not covered; for those, use the DynamicProxy fallback below.

## DynamicProxy fallback

For scenarios where compile-time generation isn't possible (e.g., plugin systems loading interfaces at runtime), add the `Cocoar.SignalARRR.DynamicProxy` package:

```xml
<PackageReference Include="Cocoar.SignalARRR.DynamicProxy" Version="5.*" />
```

This registers a fallback factory in `ProxyCreator` that uses `DispatchProxy` for runtime proxy creation.

::: warning
`DynamicProxy` requires `System.Reflection.Emit` and is **not AOT-compatible**. Use the source generator for AOT scenarios.
:::

## ProxyCreator API

| Method | Description |
|--------|-------------|
| `RegisterFactory<T>(factory)` | Register a compiled proxy factory |
| `RegisterFallbackFactory(factory)` | Register a runtime fallback (e.g., DispatchProxy) |
| `HasFactory<T>()` | Check if a proxy factory exists for `T` |
| `CreateInstanceFromInterface<T>(helper)` | Create a proxy instance |

## Next steps

- [Typed Methods](/guide/dotnet-client/typed-methods) — use generated proxies on the client
- [Getting Started](/guide/getting-started) — full setup walkthrough
- [Packages](/reference/packages) — which packages to reference
