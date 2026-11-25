---
description: "Handle server-to-client calls on the .NET client: define the contract, implement it, RegisterInterface before connecting, return values, why On() is for raw SignalR only, what happens when a handler throws"
---

# Server-to-Client Handlers

The server can call methods on the client and optionally await a response. Register handlers on the client to respond to these calls.

## Typed handlers via interfaces

Define a shared contract interface, implement it on the client, and register it. The source generator emits a dispatcher for every `[SignalARRRContract]` interface, which turns an incoming call into a direct call on your implementation — no reflection, so it works under NativeAOT.

### 1. Define the contract (shared library)

```csharp
[SignalARRRContract]
public interface IChatClient
{
    void ReceiveMessage(string user, string message);
    Task<string> GetClientName();
}
```

### 2. Implement on the client

```csharp
public class ChatClientHandler : IChatClient
{
    public void ReceiveMessage(string user, string message)
    {
        Console.WriteLine($"{user}: {message}");
    }

    public Task<string> GetClientName()
    {
        return Task.FromResult(Environment.MachineName);
    }
}
```

### 3. Register before connecting

```csharp
var connection = HARRRConnection.Create(builder => { ... });

// Register with an instance
connection.RegisterInterface<IChatClient>(new ChatClientHandler());

// Or with a factory (for dependency injection)
connection.RegisterInterface<IChatClient>(sp => new ChatClientHandler(sp.GetRequiredService<ILogger>()));

// Or name the implementation: taken from DI if registered there, else created with ActivatorUtilities
connection.RegisterInterface<IChatClient, ChatClientHandler>();

await connection.StartAsync();
```

### 4. Server calls the client

```csharp
// In a ServerMethods class or anywhere with access to ClientContext
var client = clientContext.GetTypedMethods<IChatClient>();
client.ReceiveMessage("System", "Welcome!");           // fire-and-forget
var name = await client.GetClientName();               // awaits response
```

::: warning Register before StartAsync
`RegisterInterface` must be called **before** `StartAsync()`. The server may invoke client methods immediately after connection.
:::

### What a contract must be

`RegisterInterface` throws at once, with the reason, for an interface the client cannot receive:

- **No `[SignalARRRContract]`** — without it there is no generated dispatcher. This applies to the .NET Framework client too.
- **Two members reachable under the same name with an overlapping argument count** — the wire carries no parameter types, so they cannot be told apart. Overloads with different argument counts, trailing default parameters and inherited members are fine; see [Proxy Generation](/guide/advanced/proxy-generation#generated-code).
- **A generic method** — see below.

### Generic methods cannot be received

A server-to-client call carries a generic type argument only as a type name, and only reflection can turn a name into a call — which is what NativeAOT rules out. So since 6.0 a contract with a generic method cannot be registered on a client, and the server refuses to send a call to one (`NotSupportedException`, on every path: per-client proxy, broadcast, backplane). A .NET client that receives generic arguments from an older server answers `method_not_found`. The TypeScript, Swift and Kotlin clients never evaluated them.

Declare a concrete method per type instead. Generic methods on the **server**, called by a client, are unaffected — see [Typed Methods](/guide/dotnet-client/typed-methods#generic-method-support).

::: danger Removed in 5.0: `OnServerRequest`
Earlier versions documented `connection.OnServerRequest("MethodName", handler)` for registering
handlers by bare method name. It never worked: the handlers went into a registry that nothing read,
so a server call either failed with `Method 'X' not found!` or, on the fire-and-forget path, was
swallowed into a client-side log line and did nothing at all. It is removed from both .NET clients
in 5.0.

There is no ad-hoc replacement — `RegisterInterface` with a contract interface, as shown above, is
the way to receive server-to-client calls, and always was.
:::

## `On()` is for raw SignalR calls, not contracts

`HARRRConnectionExtensions` provides `On<T>()` overloads for up to 4 parameters. These register
against **raw SignalR method names** — they pass straight through to `HubConnection.On`:

```csharp
// Fires for Clients.Client(id).SendAsync("StatusChanged", "online")
// — i.e. the plain SignalR API on the hub, bypassing SignalARRR
connection.On<string>("StatusChanged", status =>
{
    Console.WriteLine(status);
});
```

::: warning `On()` does not receive contract calls
A call made through a typed proxy — `clientContext.GetTypedMethods<IChatClient>().ReceiveMessage(...)` —
does **not** arrive as a SignalR message named `ReceiveMessage`. It arrives wrapped in an
`InvokeServerRequest` or `InvokeServerMessage` envelope, with the contract name
(`MyApp.IChatClient|ReceiveMessage`) *inside* the envelope. So `connection.On("ReceiveMessage", ...)`
never fires for it, and does so silently.

Use `RegisterInterface` for anything the server sends through a contract, and `On()` only for
messages the hub sends with plain `SendAsync`.
:::

## What happens when a handler throws

**The return type decides whether the server finds out.** This is the one thing to know before
choosing a signature for a contract member.

| Contract member | Sent as | A throwing handler |
|---|---|---|
| `Task<T>`, `T` | `InvokeServerRequest` | reaches the server as a `HubException` carrying the client's message |
| `void`, `Task` | `InvokeServerMessage` | is logged on the client and **nowhere else** |

For the fire-and-forget case this is not a gap that could be closed by propagating harder: the
server's `SendAsync` completes as soon as the message reaches the transport, long before the client
runs the method. By the time the handler fails there is no caller left to tell.

::: warning A failing push is invisible to the server
All three clients log the failure — .NET through `ILogger`, TypeScript to `console.error`, Swift
through its logger — and all three use the same wording, `Failed to handle server message '<name>'`.
That is the only record. For a browser or mobile client that means the evidence sits on someone
else's machine.

**If the server needs to know that the call succeeded, give the contract member a return value.**
Even `Task<bool>` turns a silent failure into a `HubException` at the call site.
:::

```csharp
[SignalARRRContract]
public interface IChatClient
{
    // Fire-and-forget: fast, and the server cannot tell whether it worked
    void ReceiveMessage(string user, string message);

    // Awaited: a throwing handler surfaces at the caller
    Task<bool> ApplySettings(Settings settings);
}
```

The same split applies to broadcasts, with one addition: `SendAsync` discards return values even for
members that declare one, because there is no single caller to return them to. Use `InvokeAllAsync`
when you need results — or errors — from a set of clients.

## How it works

When the server calls a client method:

1. Server sends `InvokeServerRequest` (expects reply) or `InvokeServerMessage` (fire-and-forget)
2. Client receives the message and the contract's generated dispatcher calls the registered handler
3. If `InvokeServerRequest`, the handler's return value is sent back automatically via SignalR's native client results (no separate reply message needed)

The `ChallengeAuthentication` message is handled automatically — the client's token factory is called and the token is sent back without developer intervention.

## Next steps

- [Server Methods](/guide/server/server-methods) — how the server calls client methods
- [Cancellation Propagation](/guide/advanced/cancellation) — server-initiated cancellation
- [TypeScript Client](/guide/typescript-client/setup) — same pattern in TypeScript