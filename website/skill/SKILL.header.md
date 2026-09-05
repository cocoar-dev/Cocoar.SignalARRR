---
name: signalarrr
description: >
  Typed bidirectional RPC over ASP.NET Core SignalR with Cocoar.SignalARRR. Use when working
  with HARRR hubs, ServerMethods<T>, [SignalARRRContract] interfaces, HARRRConnection on .NET,
  the @cocoar/signalarrr npm client or the CocoarSignalARRR Swift package, server-to-client calls,
  ClientManager and IClientQuery, item streaming (IAsyncEnumerable, IObservable, ChannelReader),
  HTTP stream references, cancellation propagation, SignalARRR authorization, the Redis or
  PostgreSQL backplane, or cluster subjects.
metadata:
  author: Bernhard Windisch
  version: "5.1"
  source: https://docs.cocoar.dev/signalarrr/
---

# Cocoar.SignalARRR

SignalARRR extends ASP.NET Core SignalR with typed bidirectional RPC: server and client call
each other's methods through shared interfaces, with compile-time proxies, item streaming, HTTP
file transfer, cancellation propagation, ASP.NET Core authorization, and an optional multi-node
backplane. This skill is the documentation, page by page, under `references/`. The index at the
bottom says which page answers what.

## Packages

| Package | Purpose |
|---|---|
| `Cocoar.SignalARRR.Server` | Server: `HARRR` hub base class, `ServerMethods<T>`, authorization, `ClientManager`, streaming, cluster subjects |
| `Cocoar.SignalARRR.Server.Backplane.Redis` | Multi-node scale-out over Redis, Valkey or Garnet (`AddSignalARRRRedisBackplane`) |
| `Cocoar.SignalARRR.Server.Backplane.Postgres` | Multi-node scale-out over the PostgreSQL primary the app already has (`AddSignalARRRPostgresBackplane`), with catch-up after a subscription drop |
| `Cocoar.SignalARRR.Client` | .NET client: `HARRRConnection`, typed proxies, server-to-client handlers |
| `Cocoar.SignalARRR.Client.FullFramework` | .NET Framework 4.6.2+ client: typed proxies via `DispatchProxy`, no item streaming |
| `Cocoar.SignalARRR.Contracts` | `[SignalARRRContract]` attribute plus the source generator; reference from shared interface projects |
| `Cocoar.SignalARRR.DynamicProxy` | Opt-in runtime proxy fallback via `DispatchProxy`, for plugin scenarios |
| `Cocoar.SignalARRR.Common`, `.ProxyGenerator`, `.SourceGenerator` | Referenced transitively; not added by hand |
| `@cocoar/signalarrr` (npm) | TypeScript/JavaScript client: `HARRRConnection`, `invoke`, `send`, `stream`, `onServerMethod` |
| `CocoarSignalARRR` (Swift Package) | Swift client for iOS, macOS, tvOS, watchOS with the `@HubProxy` macro |

## Things an assistant gets wrong without the docs

- **`send` is fire-and-forget, `invoke` awaits a result, `stream` returns items.** On the .NET
  client the return type of the contract method decides: `void`/`Task` → send, `Task<T>` → invoke,
  `IAsyncEnumerable<T>`/`IObservable<T>`/`ChannelReader<T>` → stream. On TypeScript and Swift the
  caller chooses the call explicitly.
- **Every interface a `ServerMethods<T>` class implements is a public RPC surface**, whether or
  not it carries `[SignalARRRContract]`. Hold collaborators as fields; do not implement their
  interfaces on the class.
- **`WithHub<T>()` returns an `IClientQuery`, not a sequence.** Filter with `WithGroup`, `WithUser`
  and `WithAttribute`, which span the cluster; `WithLocalFilter(predicate)` and `LocalClients()`
  are node-local by name.
- **Server-to-client calls use SignalR client results natively.** Register handlers with
  `RegisterInterface` (.NET), `onServerMethod` (TypeScript) or the interface handler (Swift)
  before connecting; `On()` on the raw `HubConnection` is not the contract path.
- **A `System.IO.Stream` parameter travels over HTTP, not the WebSocket.** Download endpoints are
  one-time; uploads go through `RequestUploadSlot` and `POST /hub/upload/{id}`; slots per
  connection are capped.
- **Token clients configure the message credential.** Without it the server's
  `ChallengeAuthentication` gets no answer and the connection is denied at the first authorized
  call; transport-level auth (certificates, Negotiate, cookies) works without it.
- **Unexpected exceptions do not carry their detail to the caller** — a fixed sentence plus a
  correlation id. Throw `HARRRException(code, message)` for errors the caller is meant to act on.
- **A backplane is a package and one registration call.** Redis and Postgres provide the same
  cluster behaviour; the Postgres one replays messages missed during a subscription drop. Server
  streams fed by an in-process observable are node-local unless the source is an
  `IClusterSubject<T>` — one per event type, registered by name.
