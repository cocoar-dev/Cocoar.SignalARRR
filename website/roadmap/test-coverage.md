---
description: How the three client ecosystems are tested against one shared IntegrationTestServer on real Kestrel, and what each suite covers
---

# Test Coverage

All three client ecosystems (.NET, TypeScript, Swift) test against one shared `IntegrationTestServer` running real Kestrel. No mocking.

## Summary

| Platform | Framework | Tests |
|----------|-----------|------:|
| .NET Unit (SourceGenerator) | xUnit | 3 |
| .NET Integration (JSON) | xUnit | 50 |
| .NET Integration (MessagePack) | xUnit | 5 |
| TypeScript Integration (JSON) | vitest | 25 |
| TypeScript Integration (MessagePack) | vitest | 5 |
| Swift Unit | XCTest | 31 |
| Swift Macro | XCTest | 6 |
| Swift Integration | XCTest | 6 |

## What's Tested

- **Client → Server:** invoke (sync/async, multiple return types), send (fire-and-forget), echo, streaming (ChannelReader, IAsyncEnumerable)
- **Server → Client:** fire-and-forget, typed proxy calls, return values (string, list, guid), streaming (IAsyncEnumerable), cancellation
- **Complex Types:** DateTime, Guid, List, Dictionary, multiple parameter types
- **Multi-ServerMethods:** second class on same hub, `[MessageName]` attribute, hub method coexistence
- **Authorization:** authenticated calls (sync/async), unauthenticated rejection, token challenge/refresh, `[AllowAnonymous]` override, second ServerMethods class with hub-level auth
- **Error Handling:** structured error types (ArgumentException, InvalidOperationException), non-existent method
- **File Transfer:** RequestUploadSlot, HTTP upload, automatic Stream argument preparation, Stream return values
- **Advanced:** `[FromServices]` injection, ClientContext.Attributes from headers
- **MessagePack:** invoke, send, echo, guid, multi-param — same scenarios as JSON
- **Proxy Generation:** ModuleInitializer registration, all 7 return type categories, method name format, CancellationToken extraction, generated dispatchers (overloads by argument count, default parameters, inherited members, unreceivable contracts)

## Test Infrastructure

- **IntegrationTestServer** — standalone .NET server, dynamic port, shared by all clients
- **`scripts/test-server.sh`** — server lifecycle coordinator (acquire/release with ref counting)
- **`scripts/run-integration-tests.sh`** — runs all available client tests in sequence
- **`scripts/run-aot-smoke.sh`** — publishes `src/tests/Cocoar.SignalARRR.AotSmoke` and `src/tests/Cocoar.SignalARRR.AotSmokeServer` with NativeAOT and runs the native client — proxy calls, a stream, pushes, a server call with result, `ServerMethods`, interfaces, defaults, generics, `[FromServices]`, an upload, `[Authorize]`/`[AllowAnonymous]` — against the native server and against the IntegrationTestServer; fails on any ILC warning from `Cocoar.SignalARRR.*` (CI job `aot-smoke`)
- **.NET fixture** auto-starts server if no `SIGNALARRR_TEST_SERVER_URL` environment variable