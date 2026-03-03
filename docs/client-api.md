# Client API Reference

## HARRRConnection

**Namespace:** `Cocoar.SignalARRR.Client`

The main client-side entry point. Wraps `HubConnection` with typed RPC support.

### Creating a connection

```csharp
// Option A: From HubConnectionBuilder (most common)
var connection = HARRRConnection.Create(builder => {
    builder.WithUrl("https://localhost:5001/myhub");
    builder.WithAutomaticReconnect();
    builder.AddJsonProtocol(options => {
        options.PayloadSerializerOptions.PropertyNamingPolicy = null;
    });
});

// Option B: From existing HubConnection
var hubConnection = new HubConnectionBuilder()
    .WithUrl("https://localhost:5001/myhub")
    .Build();
var connection = HARRRConnection.Create(hubConnection);
```

### Connection lifecycle

```csharp
await connection.StartAsync();
await connection.StopAsync();
await connection.DisposeAsync();
```

### Properties

```csharp
string? ConnectionId              // Current connection ID (null if disconnected)
HubConnectionState State          // Disconnected, Connecting, Connected, Reconnecting
TimeSpan ServerTimeout            // Timeout before considering server disconnected
TimeSpan KeepAliveInterval        // Interval for keep-alive pings
TimeSpan HandshakeTimeout         // Timeout for initial handshake
```

### Events

```csharp
connection.Closed += async (exception) => {
    Console.WriteLine($"Disconnected: {exception?.Message}");
};

connection.Reconnecting += async (exception) => {
    Console.WriteLine("Reconnecting...");
};

connection.Reconnected += async (connectionId) => {
    Console.WriteLine($"Reconnected: {connectionId}");
};
```

### Accessing the underlying HubConnection

```csharp
HubConnection hub = connection.AsSignalRHubConnection();
```

---

## Typed Server Proxies

### GetTypedMethods\<T\>()

Create a typed proxy for calling server methods through a shared interface:

```csharp
var chat = connection.GetTypedMethods<IChatHub>();

// All calls are now strongly typed
await chat.SendMessage("Alice", "Hello!");
List<string> history = await chat.GetHistory();
```

The proxy maps interface methods to SignalARRR RPC calls:

| Interface return type | Wire behavior |
|---|---|
| `void` | Synchronous send (fire-and-forget) |
| `T` (sync) | Synchronous invoke with return |
| `Task` | Async send (fire-and-forget) |
| `Task<T>` | Async invoke with return |
| `IAsyncEnumerable<T>` | Server-to-client stream |
| `ChannelReader<T>` | Server-to-client stream (channel) |
| `IObservable<T>` | Server-to-client stream (Rx) |

### Direct invocation (without typed proxy)

```csharp
// Invoke with return value
var result = await connection.InvokeCoreAsync<List<string>>(
    "GetHistory", Array.Empty<object>());

// Invoke without return value
await connection.InvokeCoreAsync(
    "SendMessage", new object[] { "Alice", "Hello!" });

// Fire-and-forget
await connection.SendCoreAsync(
    "LogEvent", new object[] { "user_joined" });

// Stream
await foreach (var msg in connection.StreamAsyncCore<string>(
    "StreamMessages", Array.Empty<object>(), cancellationToken)) {
    Console.WriteLine(msg);
}
```

---

## Registering Client-Side Method Handlers

When the server calls methods on the client (server-to-client RPC), the client
needs registered handlers. Use `connection.MessageHandler.RegisterInterface()`.

### Type-based registration

```csharp
// Framework creates a new instance per invocation
connection.MessageHandler.RegisterInterface<IChatClient, ChatClientImpl>();
```

### Instance-based registration

```csharp
// Use a shared singleton instance
var impl = new ChatClientImpl();
connection.MessageHandler.RegisterInterface<IChatClient, ChatClientImpl>(impl);
```

### Factory-based registration

```csharp
// Use a factory for DI support
connection.MessageHandler.RegisterInterface<IChatClient, ChatClientImpl>(
    sp => new ChatClientImpl(sp.GetRequiredService<ILogger>()));
```

### Non-generic registration

```csharp
// Register by Type objects (useful for plugin scenarios)
connection.MessageHandler.RegisterInterface(
    typeof(IChatClient), typeof(ChatClientImpl));
```

### Implementation example

```csharp
[SignalARRRContract]
public interface IChatClient {
    void ReceiveMessage(string user, string message);
    Task<string> GetClientName();
    IAsyncEnumerable<int> StreamNumbers(int count);
}

public class ChatClientImpl : IChatClient {
    public void ReceiveMessage(string user, string message) {
        Console.WriteLine($"{user}: {message}");
    }

    public Task<string> GetClientName() {
        return Task.FromResult(Environment.MachineName);
    }

    public async IAsyncEnumerable<int> StreamNumbers(int count) {
        for (int i = 0; i < count; i++) {
            yield return i;
            await Task.Delay(100);
        }
    }
}
```

---

## Server Request Handlers

For ad-hoc server-to-client calls without interfaces:

```csharp
// Register a handler for a named method
connection.OnServerRequest<string>("Ping", (message) => {
    return $"Pong: {message}";
});

// With multiple parameters
connection.OnServerRequest<string, int>("GetItem", (category, index) => {
    return items[category][index];
});
```

---

## Authentication

Pass an access token provider when creating the connection:

```csharp
var connection = HARRRConnection.Create(builder => {
    builder.WithUrl("https://localhost:5001/myhub", options => {
        options.AccessTokenProvider = () => Task.FromResult(GetCurrentToken());
    });
});
```

SignalARRR automatically:
1. Sends the token with each RPC call
2. Responds to server authentication challenges
3. Supports token refresh when the server requests re-authentication

---

# TypeScript / JavaScript Client API

**Package:** `@cocoar/signalarrr`
**Requires:** `@microsoft/signalr` ^10

## HARRRConnection

### Creating a connection

```ts
import { HARRRConnection } from '@cocoar/signalarrr';

// Option A: configure via builder callback (most common)
const connection = HARRRConnection.create(builder => {
    builder.withUrl('https://localhost:5001/myhub');
    builder.withAutomaticReconnect();
});

// Option B: pass an existing HubConnection
import * as signalR from '@microsoft/signalr';
const hub = new signalR.HubConnectionBuilder()
    .withUrl('https://localhost:5001/myhub')
    .build();
const connection = HARRRConnection.create(hub);
```

### Lifecycle

```ts
await connection.start();
await connection.stop();
```

### Properties

```ts
connection.baseUrl                       // get/set
connection.connectionId                  // string | null
connection.state                         // HubConnectionState
connection.serverTimeoutInMilliseconds   // get/set
connection.keepAliveIntervalInMilliseconds // get/set
```

### Events

```ts
connection.onClose(err => console.log('closed', err));
connection.onReconnecting(err => console.log('reconnecting', err));
connection.onReconnected(id => console.log('reconnected', id));
```

### Access the underlying HubConnection

```ts
const hub: signalR.HubConnection = connection.asSignalRHubConnection();
```

---

## Calling server methods

### `invoke<T>()` — call and await a return value

```ts
const history = await connection.invoke<string[]>('ChatMethods.GetHistory');
const result  = await connection.invoke<number>('MathMethods.Add', 3, 4);
```

### `send()` — fire-and-forget

```ts
await connection.send('ChatMethods.SendMessage', 'Alice', 'Hello!');
```

### `stream<T>()` — server-to-client stream

```ts
const stream = connection.stream<string>('ChatMethods.StreamMessages');

stream.subscribe({
    next:     msg  => console.log(msg),
    error:    err  => console.error(err),
    complete: ()   => console.log('stream complete'),
});
```

---

## Handling server-to-client calls

Use `onServerMethod` to register handlers for methods the server calls on this client.
Returns `this` for chaining.

```ts
// Synchronous handler — return a value
connection.onServerMethod('GetClientName', () => navigator.userAgent);

// Async handler — return a Promise
connection.onServerMethod('FetchData', async (id: string) => {
    const res = await fetch(`/data/${id}`);
    return res.json();
});

// With AbortSignal (server passed a CancellationToken)
connection.onServerMethod('DoWork', async (payload: string, signal: AbortSignal) => {
    while (!signal.aborted) {
        await processChunk(payload, signal);
    }
});

// Chaining
connection
    .onServerMethod('Ping',        ()        => 'Pong')
    .onServerMethod('GetStatus',   ()        => ({ ok: true }))
    .onServerMethod('HandleEvent', (e: Event) => handleEvent(e));
```

### Handler types

| Server sends | Handler receives | Handler should return |
|---|---|---|
| `InvokeServerRequest` | args (+ optional `AbortSignal`) | value — sent back to server |
| `InvokeServerMessage` | args (+ optional `AbortSignal`) | anything — result discarded |
| `ChallengeAuthentication` | — | — (handled automatically) |
| `CancelTokenFromServer` | — | — (handled automatically) |

---

## Raw SignalR event handlers

For low-level use, `on` / `off` pass through directly to the underlying `HubConnection`:

```ts
connection.on('CustomEvent', (payload) => console.log(payload));
connection.off('CustomEvent');
```
