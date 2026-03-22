# Feature Status

Current implementation status across all platforms. See [Client Comparison](/reference/client-comparison) for a detailed side-by-side comparison with code examples.

| Feature | .NET Server | .NET Client | TypeScript | Swift |
|---------|:-----------:|:-----------:|:----------:|:-----:|
| Invoke (call & wait) | Done | Done | Done | Done |
| Send (fire & forget) | Done | Done | Done | Done |
| Server→Client item streaming | Done | Done | Done | Done |
| Client→Server item streaming | Done | Done | Done | Done |
| CancellationToken propagation | Done | Done | Done | Done |
| Authorization + token refresh | Done | Done | Done | Done |
| Compile-time proxies | Done | Done (Source Generator) | N/A | Done (@HubProxy Macro) |
| DynamicProxy fallback | Done | Done | N/A | N/A |
| File transfer (HTTP stream refs) | Done | Done | Done | Done |
| Structured error types | Done | Done | Done | — |
| MessagePack protocol | Done | Done | Done | — |
| Observable/ChannelReader returns | Done | Done | — | N/A |

## Swift Client: Open Items

The Swift client has core feature parity but is missing some features added during the current development cycle. These need macOS for testing.

| Item | What needs to happen |
|------|---------------------|
| Structured error types | Add `HARRRError` model + parsing to Swift client |
| MessagePack protocol | Check if `signalr-client-swift` preview supports MessagePack, add if available |
| Integration tests | Add tests for: complex types, multi-ServerMethods, [MessageName], server→client invoke, error handling — matching the .NET and TypeScript test suites |
| Client→Server Stream argument | Verify `buildClientRequest()` Data upload works E2E |

## Future Considerations

| Item | Priority |
|------|----------|
| TypeScript Observable/ChannelReader wrappers | Low — wire protocol already works via `stream()` |
| Extensible reference type registry | Low — build when a second use case beyond `Stream` emerges |
