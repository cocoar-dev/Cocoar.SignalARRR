---
description: Implementation status of every feature across the .NET server and the .NET, TypeScript and Swift clients
---

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
| Structured error types | Done | Done | Done | Done |
| MessagePack protocol | Done | Done | Done | Done |
| Observable/ChannelReader returns | Done | Done | — | N/A |
| Auto-reconnect | Done | Done | Done | Done |
| Multiple transports (WS/SSE/LP) | Done | Done | Done | Done |
| Logging | Done | Done | Done | Done (os_log) |
