# TypeScript Client Setup

The `@cocoar/signalarrr` npm package provides a TypeScript/JavaScript client for SignalARRR with support for `invoke`, `send`, `stream`, bidirectional streaming, and server-to-client method handling.

::: info Feature Parity
The TypeScript client has full feature parity with the .NET client: core RPC, cancellation, bidirectional streaming, and HTTP stream references.
:::

## Installation

```bash
npm install @cocoar/signalarrr @microsoft/signalr
```

The package ships as ESM and CJS with full TypeScript declarations.

## Create a connection

Use the static `create()` factory with a builder callback:

```ts
import { HARRRConnection } from '@cocoar/signalarrr';
import * as signalR from '@microsoft/signalr';

const connection = HARRRConnection.create(builder => {
    builder.withUrl('https://localhost:5001/apphub');
    builder.withAutomaticReconnect();
});
```

Or wrap an existing `HubConnection`:

```ts
const hubConnection = new signalR.HubConnectionBuilder()
    .withUrl('https://localhost:5001/apphub')
    .build();

const connection = HARRRConnection.create(hubConnection);
```

## Start and stop

```ts
await connection.start();

// ... use the connection ...

await connection.stop();
```

## Invoke (call with return value)

`invoke<T>()` calls a server method and awaits the result:

```ts
const history = await connection.invoke<string[]>('ChatMethods.GetHistory');
const user = await connection.invoke<User>('UserMethods.GetUser', userId);
```

## Send (fire-and-forget)

`send()` calls a server method without waiting for a return value:

```ts
await connection.send('ChatMethods.SendMessage', 'Alice', 'Hello!');
```

## Stream

`stream<T>()` opens a server-to-client stream:

```ts
connection.stream<string>('ChatMethods.StreamMessages').subscribe({
    next: msg => console.log(msg),
    error: err => console.error(err),
    complete: () => console.log('Stream ended'),
});
```

## Error handling

When a server method throws an exception, `invoke()` rejects with a structured error containing the exception type and message:

```ts
try {
    await connection.invoke('SomeMethod');
} catch (err: any) {
    console.log(err.type);    // "System.ArgumentException"
    console.log(err.message); // "Invalid value provided"
}
```

For more control, use `parseHARRRError()` from the package:

```ts
import { parseHARRRError } from '@cocoar/signalarrr';

try {
    await connection.invoke('SomeMethod');
} catch (err) {
    const error = parseHARRRError(err);
    console.log(error.Type, error.Message);
}
```

## MessagePack protocol

For better performance with many clients, use MessagePack instead of JSON:

```bash
npm install @microsoft/signalr-protocol-msgpack
```

```ts
import { MessagePackHubProtocol } from '@microsoft/signalr-protocol-msgpack';

const connection = HARRRConnection.create(builder => {
    builder.withUrl('https://localhost:5001/apphub');
    builder.withHubProtocol(new MessagePackHubProtocol());
});
```

The server must also have MessagePack enabled (`.AddMessagePackProtocol()`). Both JSON and MessagePack clients can connect to the same hub simultaneously.

## Authentication

Provide a token factory through SignalR's connection options:

```ts
const connection = HARRRConnection.create(builder => {
    builder.withUrl('https://localhost:5001/apphub', {
        accessTokenFactory: () => getAuthToken(),
    });
});
```

Token challenges are handled automatically — when the server detects an expired token, it sends a `ChallengeAuthentication` message, and the client calls `accessTokenFactory()` to get a fresh token.

## Connection events

```ts
connection.onClose(error => {
    console.log('Connection closed', error);
});

connection.onReconnecting(error => {
    console.log('Reconnecting...', error);
});

connection.onReconnected(connectionId => {
    console.log('Reconnected as', connectionId);
});
```

## Connection properties

| Property | Type | Description |
|----------|------|-------------|
| `connectionId` | `string \| null` | Current connection ID |
| `state` | `HubConnectionState` | `Disconnected`, `Connecting`, `Connected`, `Reconnecting` |
| `baseUrl` | `string` | Hub URL (get/set) |
| `serverTimeoutInMilliseconds` | `number` | Server timeout |
| `keepAliveIntervalInMilliseconds` | `number` | Keepalive interval |

## Access the raw HubConnection

```ts
const hubConnection = connection.asSignalRHubConnection();
```

## Method naming

The TypeScript client uses string method names. The pattern is `ClassName.MethodName`:

```ts
// Calls ChatMethods.SendMessage on the server
await connection.send('ChatMethods.SendMessage', 'Alice', 'Hello!');

// Calls UserMethods.GetUser on the server
const user = await connection.invoke<User>('UserMethods.GetUser', userId);
```

## Next steps

- [Server Method Handlers](/guide/typescript-client/server-methods) — handle server-to-client calls
- [Streaming](/guide/streaming/server-to-client) — stream data from the server
- [Getting Started](/guide/getting-started) — full setup walkthrough
