# Server Method Handlers

The server can call methods on the TypeScript client. Use `onServerMethod()` to register handlers that respond to these calls.

## The name you register is the name on the wire

::: danger Register the full contract name, not the method name
The name is matched **exactly**, and a handler that does not match is not an error — the call is
dropped and nothing is logged. This is the single most common way for a TypeScript client to
"silently do nothing".
:::

When the server calls a client through a typed contract —
`clientContext.GetTypedMethods<IChatClient>().ReceiveMessage(...)` — the name that arrives is
`interface|method`, not the bare method name:

```ts
// Correct: the contract's wire name
connection.onServerMethod('MyApp.Contracts.IChatClient|ReceiveMessage', (user: string, message: string) => {
    console.log(`${user}: ${message}`);
});

// Wrong: never fires for a contract call, and fails silently
connection.onServerMethod('ReceiveMessage', () => { /* … */ });
```

By default the interface part is the C# interface's full name and the method part is the C# method
name. Since 5.0 both can be declared explicitly on the contract, which is what you want as soon as a
TypeScript or Swift client exists — see [Contract wire names](/guide/server/contracts-wire-names):

```csharp
[SignalARRRContract]
[MessageName("chat.client")]
public interface IChatClient {
    [MessageName("received")]
    void ReceiveMessage(string user, string message);
}
```

```ts
connection.onServerMethod('chat.client|received', (user: string, message: string) => { /* … */ });
```

A bare name without `|` only matches a call the hub made with plain SignalR
(`Clients.Client(id).SendAsync("Ping", …)`), not a contract call.

## Return values

If the server expects a return value (`InvokeServerRequest`), return it from the handler:

```ts
connection.onServerMethod('MyApp.Contracts.IChatClient|GetClientName', () => {
    return navigator.userAgent;
});

connection.onServerMethod('MyApp.Contracts.IChatClient|GetClientTime', () => {
    return new Date().toISOString();
});
```

The return value is sent back to the server automatically via SignalR's native client results feature.

## Async handlers

Handlers can be async:

```ts
connection.onServerMethod('MyApp.Contracts.IChatClient|FetchData', async (url: string) => {
    const response = await fetch(url);
    return await response.json();
});
```

## Chaining

`onServerMethod()` returns `this`, so you can chain multiple registrations:

```ts
const connection = HARRRConnection.create(builder => {
    builder.withUrl('https://localhost:5001/apphub');
});

connection
    .onServerMethod('chat.client|received', (user, msg) => console.log(`${user}: ${msg}`))
    .onServerMethod('chat.client|name', () => navigator.userAgent)
    .onServerMethod('chat.client|ping', () => 'pong');

await connection.start();
```

## Cancellation support

When the server passes a `CancellationToken` to a client method, SignalARRR converts it to an `AbortSignal` in the TypeScript handler:

```ts
connection.onServerMethod('MyApp.Contracts.IWorkerClient|LongRunningTask', async (data: string, signal: AbortSignal) => {
    for (let i = 0; i < 100; i++) {
        if (signal.aborted) {
            throw new Error('Operation cancelled');
        }
        await processChunk(data, i);
    }
    return 'done';
});
```

The server can cancel the operation by calling `CancelTokenFromServer`. See [Cancellation Propagation](/guide/advanced/cancellation) for details.

## How it works

The client registers handlers for four internal SignalR methods:

| Internal Method | Behavior |
|----------------|----------|
| `InvokeServerRequest` | Calls handler, returns result via native SignalR client results |
| `InvokeServerMessage` | Calls handler (fire-and-forget, no reply) |
| `ChallengeAuthentication` | Automatic — calls token factory, sends token back |
| `CancelTokenFromServer` | Triggers `AbortController.abort()` for the matching cancellation ID |

Responses are transported automatically by SignalR's native client results feature -- no separate reply message is needed.

## Next steps

- [Cancellation Propagation](/guide/advanced/cancellation) — server-initiated cancellation with AbortSignal
- [Setup & Usage](/guide/typescript-client/setup) — TypeScript client basics
- [Server Methods](/guide/server/server-methods) — how the server calls client methods
