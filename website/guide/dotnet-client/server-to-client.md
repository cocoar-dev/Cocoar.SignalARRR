# Server-to-Client Handlers

The server can call methods on the client and optionally await a response. Register handlers on the client to respond to these calls.

## Typed handlers via interfaces

The cleanest approach is to implement a shared contract interface and register it:

```csharp
[SignalARRRContract]
public interface IChatClient
{
    void ReceiveMessage(string user, string message);
    Task<string> GetClientName();
}
```

The server calls these methods through `ClientContext.GetTypedMethods<IChatClient>()`, and the client handles them through registered handlers.

## Register ad-hoc handlers

Use `OnServerRequest()` to register handlers by method name:

```csharp
// Handle a void method (fire-and-forget from server)
connection.OnServerRequest("ReceiveMessage", (string user, string message) =>
{
    Console.WriteLine($"{user}: {message}");
    return null;
});

// Handle a method with a return value (server awaits response)
connection.OnServerRequest<string>("GetClientName", name =>
{
    return Environment.MachineName;
});
```

## Typed overloads

`OnServerRequest` supports up to 4 typed parameters:

```csharp
// 1 parameter
connection.OnServerRequest<string>("Echo", input => input.ToUpper());

// 2 parameters
connection.OnServerRequest<string, int>("Repeat", (text, count) =>
    string.Concat(Enumerable.Repeat(text, count)));

// 3 parameters
connection.OnServerRequest<string, string, bool>("Compare", (a, b, ignoreCase) =>
    string.Equals(a, b, ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal));
```

## Extension method overloads

The `HARRRConnectionExtensions` class provides `On<T>()` overloads for up to 16 parameters:

```csharp
connection.On<string, string>("ReceiveMessage", (user, message) =>
{
    Console.WriteLine($"{user}: {message}");
});
```

## How it works

When the server calls a client method:

1. Server sends `InvokeServerRequest` (expects reply) or `InvokeServerMessage` (fire-and-forget)
2. Client receives the message and dispatches to the registered handler
3. If `InvokeServerRequest`, the handler's return value is sent back automatically via SignalR's native client results (no separate reply message needed)

The `ChallengeAuthentication` message is handled automatically — the client's token factory is called and the token is sent back without developer intervention.

## Next steps

- [Server Methods](/guide/server/server-methods) — how the server calls client methods
- [Cancellation Propagation](/guide/advanced/cancellation) — server-initiated cancellation
- [TypeScript Client](/guide/typescript-client/setup) — same pattern in TypeScript
