<!-- Generated from website/guide/typescript-client/setup.md by website/scripts/sync-skill.mjs. Do not edit; edit the docs page. -->

# TypeScript Client Setup

The `@cocoar/signalarrr` npm package provides a TypeScript/JavaScript client for SignalARRR with support for `invoke`, `send`, `stream`, bidirectional streaming, and server-to-client method handling.

> **Info: Feature Parity**
>
> The TypeScript client has full feature parity with the .NET client: core RPC, cancellation, bidirectional streaming, and HTTP stream references.

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

A connection has two credentials. Usually they are the same token, so one option sets both:

```ts
const connection = HARRRConnection.create('https://localhost:5001/apphub', {
    credential: async () => await getAuthToken(),
});
```

To give them different credentials — a single-use connection ticket, say, which has no business being resent with every message — set them separately:

```ts
const connection = HARRRConnection.create('https://localhost:5001/apphub', {
    connectionCredential: async () => await getConnectionTicket(),
    messageCredential: async () => await getAuthToken(),
});
```

| Option | Authenticates | Travels as | Checked by | When it expires |
|---|---|---|---|---|
| `connectionCredential` | negotiate and the transport | `Authorization` header; in a browser, the `access_token` URL parameter for WebSocket and SSE (Node: for SSE) | `[Authorize]` on the hub class, `.RequireAuthorization()` on the mapping | fetched again on every connect and reconnect |
| `messageCredential` | every message, the answer to a challenge, file transfers | the `Authorization` field of the message; a header on file transfers | `[Authorize]` on a method or a `ServerMethods` class | fetched on every use, so a refreshed token is sent from the next call on |
| `credential` | both of the above | | | |

The options are named the same in every SignalARRR client, and nothing is coupled implicitly: `messageCredential` alone leaves the connection anonymous, `connectionCredential` alone sends no message credential. A `connectionCredential` or `messageCredential` next to `credential` takes its part over. Each may be synchronous, `async`, or a plain string. See [Authorization](../server/authorization.md#the-two-credentials) for the same table across all clients.

`HARRRConnection.create(url, options, configure?)` builds the SignalR connection itself and hands the connection credential to it as `accessTokenFactory`. SignalR's other connection options go into `options.httpConnectionOptions`; `configure` receives the builder after `withUrl`, for the protocol, automatic reconnect or logging:

```ts
const connection = HARRRConnection.create(
    'https://localhost:5001/apphub',
    {
        credential: async () => await getAuthToken(),
        httpConnectionOptions: { transport: signalR.HttpTransportType.WebSockets },
    },
    builder => builder.withAutomaticReconnect(),
);
```

With `create(builder => ...)` or an existing `HubConnection`, SignalR's own `accessTokenFactory` authenticates the connection, and only `messageCredential` applies — a connection credential there throws, as does `accessTokenFactory` in `httpConnectionOptions` next to `connectionCredential`.

Token challenges are handled automatically — while a stream is running the server may send a `ChallengeAuthentication` message, and the client calls the message credential to answer it.

A connection without a message credential is not cut off: once the server's auth cache lapses it falls back to the principal established at negotiate, the way plain SignalR would, and the expiry stated on that principal is still enforced. What you lose is the refresh — the server can no longer catch a revoked credential, and cannot ask you for a new one.

A failing factory surfaces where the call does: `invoke()` and `send()` reject, and `stream()` reports the error to the subscriber — the stream is opened only once the credential is in hand.

> **Tip: Former names**
>
> `authorization` is the message credential under its former name. It still works and is marked deprecated; replace it with `messageCredential`. Using it together with `messageCredential` or `credential` throws.

> **Warning: Changed in 5.0.0**
>
> The client used to take the message credential from SignalR's `accessTokenFactory` automatically, by reading private fields off the connection. It no longer does. If your tokens are short-lived and refreshed — the usual reason for having them — set a message credential, or the connection will run on the identity it started with until that identity's stated expiry.

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

- [Server Method Handlers](./server-methods.md) — handle server-to-client calls
- [Streaming](../streaming/server-to-client.md) — stream data from the server
- [Getting Started](../getting-started.md) — full setup walkthrough
