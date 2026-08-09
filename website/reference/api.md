# API Overview

This page lists the public API surface of SignalARRR across all packages.

## Server API (`Cocoar.SignalARRR.Server`)

### Registration

```csharp
// IServiceCollection extension
services.AddSignalARRR(options => options
    .AddServerMethodsFrom(assembly));

services.AddSignalARRRRedisBackplane(options => options
    .WithConnectionString("localhost:6379,abortConnect=false")
    .WithChannelPrefix("my-app")
    .WithNodeId("node-1"));

// IEndpointRouteBuilder extension
app.MapSignalARRRHub<THub>(path);
app.MapSignalARRRHub<THub>(path, configureOptions);
```

### HARRR (Hub base class)

| Member | Type | Description |
|--------|------|-------------|
| `ServiceProvider` | `IServiceProvider` | DI container |
| `Logger` | `ILogger?` | Logger instance |
| `ClientContext` | `ClientContext` | Current client context |
| `OnConnectedAsync()` | `Task` | Client connected (registers in ClientManager) |
| `OnDisconnectedAsync(Exception?)` | `Task` | Client disconnected (unregisters) |

**Hub methods** (wire protocol — called by clients internally):

| Method | Description |
|--------|-------------|
| `InvokeMessage(ClientRequestMessage)` | Fire-and-forget |
| `InvokeMessageResult(ClientRequestMessage)` | Returns result |
| `SendMessage(ClientRequestMessage)` | Fire-and-forget (async void) |
| `StreamMessage(ClientRequestMessage, CancellationToken)` | Server-to-client stream |
| `StreamItemToServer(Guid, object)` | Client-to-server stream item |
| `StreamCompleteToServer(Guid, string?)` | Client-to-server stream completion |

### ServerMethods / ServerMethods&lt;T&gt;

| Property | Type | Description |
|----------|------|-------------|
| `ClientContext` | `ClientContext` | Current client context |
| `Context` | `HubCallerContext` | SignalR caller context |
| `Clients` | `IHubCallerClients` | Client connections |
| `Groups` | `IGroupManager` | Group management |
| `Logger` | `ILogger` | Logger |

### ClientContext

| Property | Type | Description |
|----------|------|-------------|
| `Id` | `string` | Connection ID |
| `HARRRType` | `Type` | Hub type |
| `RemoteIp` | `IPAddress?` | Client IP |
| `User` | `ClaimsPrincipal` | Authenticated user |
| `UserValidUntil` | `DateTime` | Token expiration |
| `ConnectedAt` | `DateTime` | Connection time |
| `ReconnectedAt` | `List<DateTime>` | Reconnection history |
| `Attributes` | `ClientAttributes` | Custom key-value storage |
| `ConnectedTo` | `Uri` | Hub URL |

| Method | Description |
|--------|-------------|
| `GetTypedMethods<T>()` | Get typed proxy to call this client |
| `TryAuthenticate(MethodInfo)` | Validate token, challenge if expired |

### ClientManager

| Method | Description |
|--------|-------------|
| `WithHub<THub>()` | Select hub — primary entry point for queries |
| `GetClientById(string)` | Get client by connection ID |
| `GetAllClients()` | All connected clients (across all hubs) |
| `GetAllClients(predicate)` | Filter clients |
| `AddToGroupAsync(connectionId, groupName)` | Add client to SignalR group (tracked in `ClientContext.Groups`) |
| `RemoveFromGroupAsync(connectionId, groupName)` | Remove client from group |
| `GetConnectionsAsync<THub>()` | Connection snapshots for the selected hub |
| `GetConnectionsByUserAsync<THub>(string)` | Connection snapshots for one user |
| `GetConnectionsInGroupAsync<THub>(string)` | Connection snapshots for one group |
| `GetConnectionsByAttributeAsync<THub>(string, string?)` | Connection snapshots matching an attribute |
| `GetOnlineUsersAsync<THub>()` | User presence snapshots for the hub |
| `IsUserOnlineAsync<THub>(string)` | Check whether a user is online |

### SignalARRRRedisBackplaneOptionsBuilder

| Method | Description |
|--------|-------------|
| `WithConnectionString(string)` | Configure the Redis-compatible backend connection |
| `WithChannelPrefix(string)` | Prefix all backplane keys/channels for app isolation |
| `WithNodeId(string)` | Set a stable logical node identifier |
| `WithInvokeTimeout(TimeSpan)` | Timeout for cross-node invoke aggregation |
| `WithHeartbeatInterval(TimeSpan)` | Heartbeat interval for dead-node detection |
| `WithNodeTimeout(TimeSpan)` | Time after which a node is considered stale |

### Presence models

| Type | Purpose |
|------|---------|
| `SignalARRRConnectionSnapshot` | Connection ID, node ID, user ID, groups, and attributes |
| `SignalARRRUserPresenceSnapshot` | User ID plus aggregated connection IDs and node IDs |

## Client API (`Cocoar.SignalARRR.Client`)

### HARRRConnection

| Static method | Description |
|--------------|-------------|
| `Create(Action<HubConnectionBuilder>, options?)` | Create from builder |
| `Create(HubConnection, options?)` | Wrap existing connection |

| Method | Description |
|--------|-------------|
| `GetTypedMethods<T>()` | Get typed proxy for a contract interface |
| `InvokeCoreAsync<T>(message, ct)` | Call server, await typed result |
| `SendCoreAsync(message, ct)` | Fire-and-forget |
| `StreamAsyncCore<T>(message, ct)` | Server-to-client stream |
| `RegisterInterface<TInterface, TClass>(...)` | Register a server-to-client contract handler (instance, factory or type) |
| `StartAsync(ct)` | Connect |
| `StopAsync(ct)` | Disconnect |
| `DisposeAsync()` | Dispose |
| `AsSignalRHubConnection()` | Access raw HubConnection |

| Property | Type | Description |
|----------|------|-------------|
| `ConnectionId` | `string?` | Connection ID |
| `State` | `HubConnectionState` | Connection state |
| `ServerTimeout` | `TimeSpan` | Server timeout |
| `KeepAliveInterval` | `TimeSpan` | Keepalive interval |
| `HandshakeTimeout` | `TimeSpan` | Handshake timeout |

| Event | Description |
|-------|-------------|
| `Closed` | Fires when connection closes |
| `Reconnecting` | Fires when reconnecting |
| `Reconnected` | Fires when reconnected |

## TypeScript API (`@cocoar/signalarrr`)

### HARRRConnection

```ts
class HARRRConnection {
    static create(builderOrConnection, options?): HARRRConnection

    invoke<T>(methodName: string, ...args: unknown[]): Promise<T>
    send(methodName: string, ...args: unknown[]): Promise<void>
    stream<T>(methodName: string, ...args: unknown[]): IStreamResult<T>

    onServerMethod(methodName: string, handler: (...args) => unknown): this

    start(): Promise<void>
    stop(): Promise<void>
    asSignalRHubConnection(): signalR.HubConnection

    onClose(callback: (error?) => void): void
    onReconnecting(callback: (error?) => void): void
    onReconnected(callback: (connectionId?) => void): void

    connectionId: string | null
    state: HubConnectionState
    baseUrl: string
    serverTimeoutInMilliseconds: number
    keepAliveIntervalInMilliseconds: number
}
```

### Exported types

```ts
export { HARRRConnection } from './harrr-connection.js'
export { HARRRConnectionOptions } from './harrr-connection-options.js'
export type { ClientRequestMessage } from './models/client-request-message.js'
export type { ServerRequestMessage } from './models/server-request-message.js'
export type { CancellationTokenReference } from './models/cancellation-token-reference.js'
```

## Common types (`Cocoar.SignalARRR.Common`)

### ClientRequestMessage

| Property | Type | Description |
|----------|------|-------------|
| `Method` | `string` | Method name (`ClassName.MethodName`) |
| `Arguments` | `object[]` | Method arguments |
| `Authorization` | `string?` | Bearer token |
| `GenericArguments` | `string[]` | Generic type arguments |

### ServerRequestMessage

| Property | Type | Description |
|----------|------|-------------|
| `Id` | `Guid` | Correlation ID for reply |
| `Method` | `string` | Method name |
| `Arguments` | `object[]` | Method arguments |
| `GenericArguments` | `string[]` | Generic type arguments |
| `CancellationGuid` | `Guid?` | Cancellation correlation ID |
| `StreamId` | `Guid?` | Stream correlation ID |

### Attributes

| Attribute | Description |
|-----------|-------------|
| `[SignalARRRContract]` | Marks an interface for proxy generation |
| `[MessageName(string)]` | Override the default method name |

## Next steps

- [Wire Protocol](/reference/wire-protocol) — message flow details
- [Packages](/reference/packages) — package selection guide
- [Getting Started](/guide/getting-started) — quick setup
