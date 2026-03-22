# Server Method Handlers

The server can call methods on the TypeScript client. Use `onServerMethod()` to register handlers that respond to these calls.

## Register a handler

```ts
connection.onServerMethod('ReceiveMessage', (user: string, message: string) => {
    console.log(`${user}: ${message}`);
});
```

The handler is called when the server invokes `InvokeServerRequest` or `InvokeServerMessage` with the matching method name.

## Return values

If the server expects a return value (`InvokeServerRequest`), return it from the handler:

```ts
connection.onServerMethod('GetClientName', () => {
    return navigator.userAgent;
});

connection.onServerMethod('GetClientTime', () => {
    return new Date().toISOString();
});
```

The return value is sent back to the server automatically via SignalR's native client results feature.

## Async handlers

Handlers can be async:

```ts
connection.onServerMethod('FetchData', async (url: string) => {
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
    .onServerMethod('ReceiveMessage', (user, msg) => console.log(`${user}: ${msg}`))
    .onServerMethod('GetClientName', () => navigator.userAgent)
    .onServerMethod('Ping', () => 'pong');

await connection.start();
```

## Cancellation support

When the server passes a `CancellationToken` to a client method, SignalARRR converts it to an `AbortSignal` in the TypeScript handler:

```ts
connection.onServerMethod('LongRunningTask', async (data: string, signal: AbortSignal) => {
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
