<!-- Generated from website/guide/migration/from-v5.md by website/scripts/sync-skill.mjs. Do not edit; edit the docs page. -->

# Migration from v5.x

v6 makes the .NET client and the ASP.NET Core server run under NativeAOT. To get there, both
stopped using reflection: proxies, the dispatchers that receive server calls on a client and the
table of methods a hub accepts are now all generated at compile time. Everything that only worked
through reflection is gone, and that is what this page covers.

The wire protocol is unchanged. A 6.0 client talks to a 5.x server and the other way round — the
one exception is a generic server-to-client method, below. You can update server and clients in
any order, and 5.x and 6.0 nodes share a backplane during a rolling update.

If your contracts carry `[SignalARRRContract]`, you register handlers with
`RegisterInterface<TInterface, TClass>(...)` and your server has no generic methods, the update is
probably just the version number.

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
nothing changes. Generic methods in the **client-to-server** direction remain, but declare their
type arguments — [next section](#generic-server-methods-need-genericarguments).

### Generic server methods need `[GenericArguments]`

A client names the type argument of a generic call as a string. 5.x resolved any loaded type by that
name at runtime; 6.0 calls only the instantiations a server method declares, each one generated at
compile time:

```csharp
using Cocoar.SignalARRR.Common.Attributes;

[GenericArguments(typeof(Order), typeof(Invoice))]   // Load<Order> and Load<Invoice>
public Task<T> Load<T>(Guid id) { ... }

// several type parameters: one attribute per allowed instantiation, in parameter order
[GenericArguments(typeof(int), typeof(string))]
[GenericArguments(typeof(string), typeof(int))]
public string Pair<A, B>(A a, B b) { ... }
```

- A generic server method without the attribute, or an attribute with the wrong number of types,
  fails the build with `SARRR003`.
- A call with a type argument the method does not declare is refused with an error naming
  `[GenericArguments]`. List every type your clients send.
- Nothing changes on the clients: the .NET proxy sends `typeof(T).FullName`, and Kotlin's
  `@GenericArguments` must name one of the declared types — see
  [Wire Protocol](../../reference/wire-protocol.md#clientrequestmessage) for how names match.

Generic methods whose parameters use the type parameter — `Task Save<T>(T item)` — never worked in
5.x: the server bound the method before closing it over its type argument. They work now.

### Server methods: new build errors

The source generator now checks hubs and `ServerMethods` classes at compile time. Two mistakes that
used to fail at startup or on the first call now fail the build:

| Error | Cause | Before 6.0 |
|---|---|---|
| `SARRR002` | Two methods reachable under the same wire name with overlapping argument counts — also through omitted default parameters, or two base interfaces declaring the same member | startup error |
| `SARRR004` | A `[MessageName]` that is empty or contains `\|` | error on the call |

The wire carries no parameter types, so methods that share a name must differ in argument count;
rename one of them.

### Hubs and `ServerMethods` come from the source generator

`AddSignalARRR` no longer scans assemblies with reflection; it reads the table the source generator
built for every hub and `ServerMethods<THub>` class. The generator comes with
`Cocoar.SignalARRR.Server`, so a project that references the package has nothing to do, and
`AddServerMethodsFrom(assembly)` still selects the assemblies. An assembly added there that was
built without the generator fails at startup, naming it. There is no reflection fallback.

One difference in what is callable: public overrides of `Hub` and `object` members on a hub — an
`override` of `OnConnectedAsync`, `ToString` — were reachable by clients and are not any more.

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

### Running under NativeAOT

*New, not a migration — but the reason for the rest.* The .NET client and the server (net9.0 and
net10.0) run under NativeAOT. An AOT app adds a `JsonSerializerContext` with the types of its
arguments and results; the client's `StartAsync` and the server's startup list any it misses.
MessagePack is not available under AOT. See [NativeAOT](../advanced/proxy-generation.md#nativeaot).

JIT apps need nothing: where reflection-based JSON is enabled, it stays the last resolver in the
chain, and MessagePack keeps working.

## Only if you touched infrastructure types

These were never meant to be application-facing. If you did not reference them, there is nothing to
do.

| Type or member | Change |
|---|---|
| `SignalARRRInterfaceCollection`, `ISignalARRRInterfaceCollection`, `SignalARRRMethodsCollection`, `ISignalARRRMethodsCollection`, `ClientInterfaceMethodsCache`, `WireSlotPolicy`, `TypeHelper` | removed — the server dispatches through the generated table |
| `MethodInfoExtensions.GetAuthorizationPlan`, `GetAuthorizeData` | removed — `SignalARRRAuthorizationPlan` stays and is built at compile time |
| `ClientContext.TryAuthenticate` | takes a `SignalARRRAuthorizationPlan` instead of a `MethodInfo` |
| `WireTypeName` | moved from `Cocoar.SignalARRR.Common` to `Cocoar.SignalARRR.Server`, same namespace; `Resolve` removed, `From` stays |
| `MessagePackProtocolSerializer`, `ClientMethodsCache` | removed |
| `JsonProtocolSerializer` | takes the hub's `JsonSerializerOptions` in its constructor |
| `HubProtocolType` (client) | removed — nothing read it |
| `ProxyCreator.RegisterFallbackFactory` | removed with the DynamicProxy package |
| `Cocoar.SignalARRR.Common` | no longer depends on `MessagePack` |
| `Cocoar.SignalARRR.Client`, `.Client.FullFramework`, `.Server` | no longer depend on `Cocoar.Reflectensions` |

## Next steps

- [Changelog](https://docs.cocoar.dev/signalarrr/changelog.html) — the full list
- [Proxy Generation](../advanced/proxy-generation.md) — proxies, dispatchers, the server's method table and NativeAOT
- [Server Methods](../server/server-methods.md) — naming, overloads and `[GenericArguments]`
- [Server-to-Client Handlers](../dotnet-client/server-to-client.md) — the registration overloads
