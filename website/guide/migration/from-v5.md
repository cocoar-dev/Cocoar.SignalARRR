---
description: "Upgrade from 5.x to 6.0: the .NET client under NativeAOT, [SignalARRRContract] required everywhere, the removed DynamicProxy package, the new RegisterInterface overloads, no generic server-to-client methods, the .NET Framework client on the source generator"
---

# Migration from v5.x

v6 makes the .NET client run under NativeAOT. To get there, both .NET clients stopped using
reflection: proxies and the dispatchers that receive server calls are now all generated at compile
time. Everything that only worked through reflection is gone, and that is what this page covers.

The wire protocol is unchanged. A 6.0 client talks to a 5.x server and the other way round — the
one exception is a generic server-to-client method, below. You can update server and clients in
any order.

If your contracts carry `[SignalARRRContract]` and you register handlers with
`RegisterInterface<TInterface, TClass>(...)`, the update is probably just the version number.

## Affects almost everyone

### Every contract needs `[SignalARRRContract]`

The attribute was already required for `GetTypedMethods<T>()` on modern .NET, unless you used the
DynamicProxy fallback. It is now also required for:

- **Receiving.** `RegisterInterface` calls the generated dispatcher of the interface. An interface
  without the attribute has none, and `RegisterInterface` throws at once, naming it.
- **.NET Framework.** See [below](#net-framework-client-sdk-style-c-10-and-the-attribute).
- **Interfaces you only knew at runtime.** `Cocoar.SignalARRR.DynamicProxy` is removed, and with it
  `ProxyCreator.RegisterFallbackFactory`. An interface needs the attribute and must be visible to
  the compiler of a project that references a SignalARRR server or client package.

```xml
<!-- Remove -->
<PackageReference Include="Cocoar.SignalARRR.DynamicProxy" Version="5.*" />
```

```csharp
[SignalARRRContract]   // add where it was missing
public interface IChatClient { ... }
```

The server itself does not need the attribute to accept calls — interfaces implemented by a
`ServerMethods` class are registered as before.

## Only if you use that feature

### `RegisterInterface` with a `Type` is gone

The overloads `RegisterInterface(Type, Type)`, `RegisterInterface(Type, object)` and
`RegisterInterface(Type, Func<IServiceProvider, object>)` are removed: a runtime `Type` cannot
reach a generated dispatcher. Use the generic overloads, two of which are new:

```csharp
connection.RegisterInterface<IPushClient>(handler);                    // new: instance
connection.RegisterInterface<IPushClient>(sp => new PushClient(...));  // new: factory
connection.RegisterInterface<IPushClient, PushClient>();               // from DI, else ActivatorUtilities
connection.RegisterInterface<IPushClient, PushClient>(handler);        // unchanged
```

The same set exists on the .NET Framework client.

### Generic server-to-client methods are not supported

A generic method on a contract the **server calls on a client** — `Task Notify<T>(T payload)` on
`IPushClient` — cannot be received any more: its type argument travels as a name, and only
reflection can turn a name into a call. In 6.0:

- `RegisterInterface` throws for such a contract, naming the method.
- The server refuses to send the call with a `NotSupportedException`, through a per-client proxy, a
  broadcast, the backplane or a captured call alike.
- A 6.0 .NET client that receives a call with generic arguments from a 5.x server answers
  `method_not_found`.

Replace the generic method with one concrete method per type:

```csharp
// Before
Task Notify<T>(T payload);

// After
Task NotifyOrder(Order payload);
Task NotifyInvoice(Invoice payload);
```

The TypeScript, Swift and Kotlin clients never evaluated generic arguments on receive, so for them
nothing changes. Generic methods in the **client-to-server** direction are unaffected — the server
resolves their type arguments as before.

### .NET Framework client: SDK-style, C# 10 and the attribute

`Cocoar.SignalARRR.Client.FullFramework` used `DispatchProxy` for proxies and reflection to receive
calls. It now uses the same generated proxies and dispatchers as the .NET client, so its project
has to run the source generator:

- **SDK-style project.** The generator arrives as an analyzer through the package. Convert the
  project if it is not SDK-style yet.
- **C# 10 or later.** .NET Framework projects default to C# 7.3; the generated code needs 10.
  Without it the build fails with `SARRR001`.
- **`[SignalARRRContract]` on every contract.** `Cocoar.SignalARRR.Contracts` now targets
  netstandard2.0, so a .NET Framework project can reference the shared contract project directly.

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net48</TargetFramework>
    <LangVersion>10</LangVersion>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Cocoar.SignalARRR.Client.FullFramework" Version="6.*" />
  </ItemGroup>
</Project>
```

`ModuleInitializerAttribute`, which the generated registration needs, is added by the generator on
targets that lack it.

### Running the .NET client under NativeAOT

*New, not a migration — but the reason for the rest.* An AOT app adds a `JsonSerializerContext`
with the types of its arguments and results; `StartAsync` lists any it misses. MessagePack is not
available under AOT. See [NativeAOT](/guide/advanced/proxy-generation#nativeaot).

JIT apps need nothing: where reflection-based JSON is enabled, it stays the last resolver in the
chain, and MessagePack keeps working.

## Only if you touched infrastructure types

These were never meant to be application-facing. If you did not reference them, there is nothing to
do.

| Type or member | Change |
|---|---|
| `SignalARRRInterfaceCollection`, `ISignalARRRInterfaceCollection`, `SignalARRRMethodsCollection`, `ISignalARRRMethodsCollection`, `ClientInterfaceMethodsCache`, `WireSlotPolicy`, `TypeHelper`, `WireTypeName` | moved from `Cocoar.SignalARRR.Common` to `Cocoar.SignalARRR.Server`, same namespaces |
| `MessagePackProtocolSerializer`, `ClientMethodsCache` | removed |
| `JsonProtocolSerializer` | takes the hub's `JsonSerializerOptions` in its constructor |
| `HubProtocolType` (client) | removed — nothing read it |
| `ProxyCreator.RegisterFallbackFactory` | removed with the DynamicProxy package |
| `Cocoar.SignalARRR.Common` | no longer depends on `MessagePack` |
| `Cocoar.SignalARRR.Client`, `.Client.FullFramework` | no longer depend on `Cocoar.Reflectensions` |

## Next steps

- [Changelog](/changelog) — the full list
- [Proxy Generation](/guide/advanced/proxy-generation) — proxies, dispatchers and NativeAOT
- [Server-to-Client Handlers](/guide/dotnet-client/server-to-client) — the registration overloads
