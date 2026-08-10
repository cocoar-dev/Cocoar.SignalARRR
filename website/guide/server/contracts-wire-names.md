# Contract Wire Names

A call through a contract interface travels under a name made of two halves, separated by `|`:

```
MyApp.Contracts.IChatClient|ReceiveMessage
└────── interface ────────┘ └── method ──┘
```

By default those halves are the C# full name of the interface and the C# name of the method. That
default is convenient and, if you only ever talk .NET to .NET, harmless: both sides compile against
the same contract assembly, so a rename moves both ends at once.

It stops being harmless the moment a TypeScript or Swift client exists.

## Why you would declare them

Those clients write the name out as a string. Nothing checks it — not a compiler, not a test, not a
log line. So with default names:

- Renaming a C# method breaks every deployed non-.NET client, **silently, at runtime**.
- Moving the interface to another namespace does the same, and so does renaming the interface.
- Conversely, if the contract ships as a NuGet package, you cannot rename anything without it being
  a breaking change for .NET consumers either.

Declaring the names decouples the two. The C# identifiers become internal API you can rename freely;
the wire names become the contract you keep stable.

## How

`[MessageName]` on the interface sets the first half, on a member the second:

```csharp
[SignalARRRContract]
[MessageName("chat.client")]
public interface IChatClient {

    [MessageName("received")]
    void ReceiveMessage(string user, string message);

    // No attribute: keeps its C# name, so this one is "chat.client|GetClientName"
    Task<string> GetClientName();
}
```

```ts
connection.onServerMethod('chat.client|received', (user, message) => { /* … */ });
```

Both halves are independent, and both are optional — a contract with no attributes behaves exactly as
it did before.

## Rules

- **A declared name replaces the C# name, it does not add an alias.** Once `IChatClient` is
  `chat.client`, its full name no longer resolves. Aliasing would keep renames working and defeat the
  point.
- **The name must not contain `|`.** The receiving side splits on the first one to tell the interface
  from the method. A separator inside either half is a registration error, not a surprise later.
- **Two members cannot share a wire name and argument count.** That is the same collision rule as for
  C# names, now reachable by renaming two members onto each other. It fails at registration.
- **Hiding follows the wire name.** A member declared on the registered interface hides an inherited
  one with the same *wire* name — which, with renames in play, need not be the one it shares a C#
  name with.

## Versioning

There is no version mechanic, deliberately. A wire name is a name; if you want versions, put them in
it:

```csharp
[MessageName("getById.v2")]
Task<Item> GetById(string id);
```

What `v2` means, and whether `v1` still exists alongside it as a second member, is your application's
decision — the library does not parse, compare or resolve it.

## Where this does not apply

Client-to-server calls on hub methods and `ServerMethods` classes have always honoured
`[MessageName]`, on both the class and the method, and form their names as `Root.Method`. Nothing
about that changed.

## Next steps

- [Server-to-Client Handlers](/guide/dotnet-client/server-to-client) — the .NET side of a contract
- [TypeScript: Server Method Handlers](/guide/typescript-client/server-methods) — registering by wire name
- [Swift: Typed Proxies](/guide/swift-client/typed-proxies) — same, in Swift
