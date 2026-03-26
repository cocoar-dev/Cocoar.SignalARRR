# SignalARRR vs. gRPC vs. REST

Choosing a communication framework is one of the first architectural decisions in a distributed system. This page compares SignalARRR with the two most common alternatives — gRPC and REST APIs — and explains when each shines and where SignalARRR has an edge.

## The short version

| | SignalARRR | gRPC | REST (HTTP APIs) |
|---|---|---|---|
| **Transport** | WebSockets (+ SSE, Long Polling fallback) | HTTP/2 (end-to-end required) | HTTP/1.1 or HTTP/2 |
| **Direction** | Full duplex — server and client call each other | Client → Server (+ server streaming) | Client → Server only |
| **Real-time push** | Built-in | Server streaming only | Polling or separate WebSocket |
| **Proxy/Firewall** | Works through any proxy (auto-fallback) | HTTP/2 E2E — breaks on many corporate proxies | Works everywhere |
| **Browser support** | Full (WebSocket native) | Requires grpc-web proxy | Full |
| **Type safety** | Shared interfaces + source generator | .proto files + codegen | OpenAPI/Swagger (optional) |
| **Streaming** | Bidirectional item streaming + HTTP file transfer | Bidirectional streaming | Chunked transfer (manual) |
| **Connection** | Persistent | Per-call or persistent (HTTP/2) | Per-call |
| **Auth mid-connection** | Token challenge/refresh built-in | Per-call metadata | Per-call headers |
| **Mobile (iOS/Swift)** | Native client with `@HubProxy` macro | grpc-swift | URLSession |
| **JavaScript/TypeScript** | `@cocoar/signalarrr` npm package | grpc-web (limited) | fetch/axios |
| **.NET Framework** | Full client (netstandard2.0) | Deprecated `Grpc.Core` only | HttpClient |
| **.NET support** | net8.0 / net9.0 / net10.0 + .NET Framework 4.6.2+ | .NET Core 3.0+ only (modern client) | Any |

## When to use what

### Use REST when...

- You have a simple request/response API with no real-time requirements
- Your consumers are third parties who expect a standard HTTP API
- You need maximum cacheability (CDN, HTTP caches)
- You're building a public API that must be accessible from any language without client libraries

REST is the right choice when you don't need real-time communication and want the broadest possible compatibility.

### Use gRPC when...

- You have high-throughput service-to-service communication (microservices backend)
- Both ends are server-side (no browser, no mobile)
- You need strict contract-first development with `.proto` files
- Maximum serialization performance matters more than developer ergonomics

gRPC excels at backend-to-backend communication where both sides are under your control and you can use HTTP/2 natively.

### Use SignalARRR when...

- Server and client need to **call each other** — not just request/response
- You need **real-time push** from server to client (notifications, live updates, dashboards)
- Your clients include **browsers**, **mobile apps**, and/or **.NET desktop apps**
- You want **typed bidirectional RPC** without maintaining separate IDL files
- You need **file transfer** alongside RPC (HTTP stream references)
- You're building **enterprise line-of-business applications** with mixed client platforms
- You need **authorization with token refresh** on long-lived connections

## Why SignalARRR is the better choice for most enterprise apps

### 1. True bidirectional RPC

gRPC's "bidirectional streaming" lets both sides send messages on a stream — but it's not typed RPC. You define `stream` methods with a single message type, and both sides must interpret that stream manually. There's no concept of "server calls client method X and gets a typed response."

SignalARRR gives you actual **typed method invocation in both directions**:

```csharp
// Server calls client — typed, with return value
var client = ClientContext.GetTypedMethods<IDeviceClient>();
DeviceStatus status = await client.GetStatus();

// Client calls server — same shared interface
var hub = connection.GetTypedMethods<IDeviceHub>();
await hub.ReportMetrics(metrics);
```

With gRPC, you'd need to implement your own correlation layer on top of bidirectional streaming to achieve this. SignalARRR does it out of the box.

### 2. Works through any network infrastructure

gRPC requires **HTTP/2 end-to-end**. That sounds simple, but in enterprise environments it's often a showstopper:

- **Corporate proxies** frequently downgrade or block HTTP/2
- **Reverse proxies** (older nginx, HAProxy, IIS ARR) may not support HTTP/2 upstream or break bidirectional streaming
- **Azure Application Gateway Classic**, many WAFs, and CDNs don't pass HTTP/2 streams through correctly
- **grpc-web** is the workaround for browsers, but it requires a **separate proxy** (Envoy or ASP.NET Core middleware) that translates between HTTP/1.1 and HTTP/2 — an extra deployment, failure point, and security surface

In practice, getting gRPC to work reliably through a typical enterprise network stack (client → corporate proxy → load balancer → WAF → reverse proxy → server) is a significant DevOps challenge.

SignalARRR uses **WebSockets** as primary transport — supported by virtually every modern proxy, load balancer, and firewall. And if even WebSockets are blocked (rare, but it happens in locked-down environments), SignalR automatically falls back to **Server-Sent Events** or **Long Polling** — which work through literally any HTTP infrastructure:

```
WebSocket  →  blocked?  →  SSE  →  blocked?  →  Long Polling
   (best)                (good)                (always works)
```

No configuration, no proxy setup, no DevOps tickets. It just works.

### 3. Shared interfaces instead of .proto files

gRPC requires you to maintain `.proto` files and run a separate code generation step. The generated code is verbose and framework-specific.

SignalARRR uses **plain C# interfaces** as the contract. The Roslyn source generator produces proxies at build time — no separate tooling, no generated files to commit, no build pipeline step:

```csharp
[SignalARRRContract]
public interface IOrderHub {
    Task<Order> GetOrder(Guid id);
    Task PlaceOrder(OrderRequest request);
    IAsyncEnumerable<OrderUpdate> StreamUpdates(CancellationToken ct);
}
```

This interface is shared between server and all .NET clients. TypeScript and Swift clients use the same method names and types.

### 4. Server can push — and the client can respond

REST has no push mechanism. gRPC has server streaming, but the client can't send responses back on that stream.

SignalARRR supports **server-to-client RPC with return values** — the server calls a method on the client and awaits the response:

```csharp
// From a background service, controller, or anywhere with ClientManager
var client = clientManager.GetTypedMethods<IDeviceClient>(connectionId);
bool accepted = await client.ConfirmUpdate(updatePackage);
if (accepted) {
    await client.InstallUpdate(updatePackage);
}
```

This is a game-changer for enterprise scenarios: configuration pushes, remote management, client-driven workflows.

### 5. File transfer built into RPC

With gRPC, file transfer means chunking bytes into stream messages and reassembling on the other side. With REST, you build upload/download endpoints.

SignalARRR routes `System.IO.Stream` parameters through HTTP automatically — the RPC call looks normal:

```csharp
// Server method accepts a file as a parameter
public async Task<ImportResult> ImportData(string format, Stream data) {
    // data is a regular Stream — read it normally
}

// Client calls it — SignalARRR handles the HTTP upload transparently
await hub.ImportData("csv", fileStream);
```

### 6. Enterprise-ready auth on persistent connections

REST sends credentials per request. gRPC sends metadata per call. Both are simple.

But persistent connections (WebSockets) are different — the initial token can expire while the connection is still alive. SignalARRR solves this with an **automatic challenge/refresh flow**:

1. Client connects with a token
2. Token expires after 30 minutes
3. Next server call detects expiry → challenges client for fresh token
4. Client provides new token → call continues
5. No disconnection, no retry, no manual refresh logic

### 7. .NET Framework support — bridge legacy and modern

gRPC's modern .NET client (`Grpc.Net.Client`) requires **.NET Core 3.0+**. The old `Grpc.Core` package supports .NET Framework but is **deprecated since 2023** and no longer maintained. If you have .NET Framework 4.8 clients, gRPC is effectively a dead end.

SignalARRR provides a **dedicated .NET Framework client** (`Cocoar.SignalARRR.Client.FullFramework`) targeting netstandard2.0 with near-full feature parity: typed proxies, streaming, server-to-client RPC, cancellation, file transfer, and MessagePack.

This enables a pattern that's invaluable in enterprise environments: a **.NET 10 server** with modern frontend technology, connected to **legacy .NET Framework 4.8 clients** that load platform-specific DLLs (SCCM, SCSM, COM interop, legacy SDKs) — all communicating through the same typed RPC interface.

Neither gRPC nor REST can offer this: typed bidirectional RPC between modern and legacy .NET runtimes.

### 8. Four client platforms with feature parity

| | .NET | .NET Framework | TypeScript | Swift |
|---|---|---|---|---|
| Typed proxies | Source generator | DispatchProxy | Method name strings | `@HubProxy` macro |
| Streaming | `IAsyncEnumerable<T>` | `IAsyncEnumerable<T>` (polyfill) | `subscribe()` | `AsyncThrowingStream` |
| Server-to-client RPC | Full typed | Full typed | `onServerMethod()` | `onServerMethod()` |
| File transfer | `Stream` parameters | `Stream` parameters | HTTP stream refs | HTTP stream refs |
| MessagePack | Optional | Optional | Optional | Built-in (native impl) |
| Cancellation | `CancellationToken` | `CancellationToken` | `AbortSignal` | Task cancellation |

## When gRPC still wins

Be honest about trade-offs:

- **Raw throughput**: gRPC with Protobuf is faster for high-volume service-to-service communication (binary serialization, HTTP/2 multiplexing). For 10,000 req/s between backend services, gRPC is the right tool.
- **Language-agnostic contracts**: `.proto` files generate clients in 10+ languages. SignalARRR's typed experience is strongest in .NET, TypeScript, and Swift.
- **Ecosystem maturity**: gRPC has wider adoption in the microservices/cloud-native space, with more tooling for observability, load balancing, and service mesh integration.

## When REST still wins

- **Simplicity**: For a simple CRUD API, REST is less machinery.
- **Cacheability**: HTTP caching, CDNs, and reverse proxies work out of the box.
- **Third-party consumers**: External developers expect REST, not WebSockets.

## The verdict

For **enterprise line-of-business applications** where you control both server and clients, need real-time push, bidirectional communication, and want type safety across platforms — **SignalARRR is the strongest choice**. It gives you the developer experience of a typed RPC framework with the real-time capabilities that gRPC and REST simply don't have.

For backend-to-backend microservices at scale, use gRPC. For public APIs, use REST. For everything in between — especially when your UI needs live data and your server needs to talk back — use SignalARRR.

## Next steps

- [Getting Started](/guide/getting-started) — set up your first SignalARRR hub
- [Why SignalARRR?](/guide/why-signalarrr) — detailed comparison with raw SignalR
- [Client Comparison](/reference/client-comparison) — feature matrix across .NET, TypeScript, Swift
