# Migration from v4.x

v5 is the result of a full code review of the server and the .NET client. Most of it is fixes you
get for free. This page covers only what you have to change, grouped by whether it is likely to
affect you at all.

If your application uses the typed contract API and nothing else, the first section is probably the
whole job.

## Affects almost everyone

### `WithHub<T>()` returns `IClientQuery`

It used to return `IEnumerable<ClientContext>`, which made `.Where(...)` compile. That was a trap:
LINQ dropped the query's cluster scope, so a broadcast that looked cluster-wide silently reached
only the local node. The old docs even recommended it.

```csharp
// Before — compiles, and under a backplane reaches one node
await _clients.WithHub<AppHub>().Where(c => c.User.IsInRole("Admin"))
    .SendAsync<IAlertClient>(c => c.SecurityAlert(details));

// After — the same node-local behaviour, but the name says so
await _clients.WithHub<AppHub>().WithLocalFilter(c => c.User.IsInRole("Admin"))
    .SendAsync<IAlertClient>(c => c.SecurityAlert(details));

// After — what you probably meant: every admin on every node
await _clients.WithHub<AppHub>().WithAttribute("role", "admin")
    .SendAsync<IAlertClient>(c => c.SecurityAlert(details));
```

A predicate cannot be evaluated on another node — it is a delegate over local objects — so
`WithLocalFilter` is honest about its reach. Group, user and attribute filters are the ones the
connection registry can answer cluster-wide.

Enumerating a query also needs a name now, because it was never showing you remote connections:

```csharp
foreach (var c in _clients.WithHub<AppHub>()) { … }              // Before
foreach (var c in _clients.WithHub<AppHub>().LocalClients()) { … } // After
```

`SendAsync`, `InvokeAllAsync` and `InvokeOneAsync` are instance methods on `IClientQuery` now, so
they are no longer visible on arbitrary `IEnumerable<ClientContext>` values, and the fluent API no
longer needs `using Cocoar.SignalARRR.Server.ExtensionMethods;`.

See [Client Manager](/guide/server/client-manager).

### The entry points moved to the conventional namespaces

Registration and endpoint mapping now live where the rest of ASP.NET Core puts them. If your `using`
directives were already the conventional ones, nothing changes; otherwise the compiler will point at
each site.

## Only if you use that feature

### Redis backplane: add a package

The backplane moved out of `Cocoar.SignalARRR.Server` so that single-node applications stop carrying
`StackExchange.Redis`.

```xml
<PackageReference Include="Cocoar.SignalARRR.Server.Backplane.Redis" Version="5.*" />
```

`AddSignalARRRRedisBackplane`, its options builder and their namespaces are unchanged — the package
reference is the entire migration. See [Backplane & Clustering](/guide/server/backplane).

### `OnServerRequest` is gone

It registered handlers into a table nothing read, so a server-to-client call by bare method name
always failed — with "Method 'X' not found!", or silently on the fire-and-forget path. There is no
ad-hoc replacement; `RegisterInterface` with a contract interface is the working path, and was.

```csharp
// Before — never worked
connection.OnServerRequest("ReceiveMessage", (string user, string message) => { … });

// After
connection.RegisterInterface<IChatClient, ChatClientHandler>();
```

Removed from the .NET Framework client too, where it had the same defect.

### `ClientRequestMessage.WithAuthorization(Func<Task<string>>)` is gone

The blocking overload behind a UI deadlock: it resolved the token provider with
`GetAwaiter().GetResult()`, which deadlocks on any single-threaded `SynchronizationContext` — WPF,
WinForms, MAUI. Use `WithAuthorizationAsync`, or `WithAuthorization(string)` for a token already at
hand (that one is untouched).

### `ClientManager.GetHARRRClients<T>()` is gone

`[Obsolete]` since 4.x with a message naming this release. `WithHub<T>()` replaces it and reaches the
whole cluster rather than the local node. For the predicate overload see `WithLocalFilter` above.

### The MVC result clones on `HttpContext` are gone

`Ok`, `NotFound`, `BadRequest`, `File` in 24 overloads and about 70 more — a reimplementation of
`ControllerBase` that landed on every `HttpContext` in any file importing
`Cocoar.SignalARRR.Server.ExtensionMethods`. `Microsoft.AspNetCore.Http.Results` and `ControllerBase`
are the framework's own answers.

### `HARRRConnectionExtensions`: at most four arguments

The file was decompiled Microsoft code and has been rewritten. Handler registration went from
`On<T1…T8>` to `On<T1…T4>`, and calls from 0–10 arguments to 0–4. Past four, use the `object[]` core
methods on `HARRRConnection` — or a typed contract via `GetTypedMethods<T>()`, which the removed
overloads had buried in the completion list.

Also gone: the `InvokeCoreAsync` and `StreamAsChannelCoreAsync` *extension* methods. Identically
shaped instance methods always won overload resolution, so nothing ever called them.

### `ClientAttributes` no longer derives from `Dictionary`

It inherited `Dictionary<string, StringValues>` and hid the indexer with one returning `string?`, so
the same object answered the same key two ways — `null` through `ClientAttributes`,
`KeyNotFoundException` through the dictionary it *was*. It is now a sealed
`IReadOnlyDictionary<string, StringValues>` with one indexer that keeps the interface's contract.

The old `string?` lookup lives on as `GetString(key)` — use it wherever you relied on
`attributes[key]` being `null` for an absent attribute. `Has(key)`, `Has(key, value)`, enumeration
and case-insensitive matching are unchanged. `Add`, `Remove` and `Clear` are gone: this is a read
model of the connection request.

### `HARRRException(string, string)` changed meaning

The second parameter is now the error **code**, not a second message — errors travel with a small,
stable set of codes, and an application code you pass travels verbatim. Relevant only if you
construct these yourself.

## Only if you have TypeScript or Swift clients

### Remote reference arguments carry a `__type` marker

An argument that is a handle rather than a value — a cancellation token, a stream — now travels as
`{ "__type": "cancellationToken", "Id": … }` or `{ "__type": "stream", "Uri": … }`. Receivers used to
guess from the shape and guessed wrong on ordinary data: the TypeScript client treated *any* object
with a string `Id` as a cancellation token, so a payload like `{ Id: "user-42", Name: "Ada" }` was
swapped for a token and never reached the handler.

Update `@cocoar/signalarrr` and the Swift package together with the server.

### Handler names are contract wire names — check yours

Not a v5 change, but worth verifying while you are here, because the guides had it wrong: the name
you register must be the **full contract name**, `interface|method`, and it is matched exactly. A
miss is dropped without a word.

```ts
connection.onServerMethod('MyApp.Contracts.IChatClient|ReceiveMessage', handler);  // fires
connection.onServerMethod('ReceiveMessage', handler);                              // never fires
```

New in v5: you can declare those names explicitly with `[MessageName]` so a C# rename stops breaking
deployed clients. See [Contract Wire Names](/guide/server/contracts-wire-names).

## Only if you touched infrastructure types

These were never meant to be application-facing and are now `internal`, or changed shape. If you did
not reference them, there is nothing to do.

| Type or member | Change |
|---|---|
| `MessageHandler`, `StreamReferenceResolver`, `HubConnectionExtensions` (client) | `internal` |
| `SignalARRRAuthentication`, `MethodArgumentPreparer`, `EndpointExtensions`, the file-transfer handlers (server) | `internal` |
| `HARRRConnection`'s constructor | `internal` — `HARRRConnection.Create(...)` is and was the way to build one |
| `HARRRContext` (client) | renamed `ClientConnectionContext`, and `internal` |
| `MethodArgumentPreperator` | renamed `MethodArgumentPreparer` |
| `ClientCollectionResult<T>` | renamed `ClientResult<T>` — it holds one result |
| `ObservableExtensions` (server) | renamed `SignalARRRObservableExtensions`, which collided with `System.ObservableExtensions` |
| `MethodNames` | `static class`, all names `const` — they had public setters, so any consumer could rewrite the wire protocol process-wide |
| `ServerStreamManager`, `ServerPushStreamManager.WaitForUpload` | signature changes |
| `MethodInfoExtensions.GetAuthorizeData()` | signature change |
| `ISignalARRRMethodsCollection.GetMethodInformations`, `ISignalARRRInterfaceCollection.GetInvokeInformation` | signature changes |

## Next steps

- [Changelog](/changelog) — the full list, including everything fixed that needs no action from you
- [Client Manager](/guide/server/client-manager) — the reshaped query API
- [Contract Wire Names](/guide/server/contracts-wire-names) — new in v5
