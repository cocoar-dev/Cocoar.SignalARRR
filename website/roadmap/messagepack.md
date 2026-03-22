# MessagePack Protocol

**Status:** Implemented for .NET + TypeScript. Swift needs macOS testing.

## Overview

SignalARRR supports MessagePack alongside JSON. Both protocols run simultaneously — different clients can use different protocols on the same hub.

**Architecture:**
- `IProtocolSerializer` abstraction in `Cocoar.SignalARRR.Common.Serialization`
- `JsonProtocolSerializer` handles both `JsonElement` (JSON) and plain .NET objects (MessagePack) via its fallback
- All `JsonElement`/`JsonSerializer` direct usage removed from `MessageHandler` and `ServerStreamManager`

## Server Setup

```csharp
builder.Services.AddSignalR()
    .AddMessagePackProtocol();  // Add this line — that's it

builder.Services.AddSignalARRR(options => options
    .AddServerMethodsFrom(typeof(Program).Assembly));
```

## .NET Client

```bash
dotnet add package Microsoft.AspNetCore.SignalR.Protocols.MessagePack
```

```csharp
var connection = HARRRConnection.Create(builder => {
    builder.WithUrl("https://server/hub");
    builder.AddMessagePackProtocol();
});
```

## TypeScript Client

```bash
npm install @microsoft/signalr-protocol-msgpack
```

```ts
import { MessagePackHubProtocol } from '@microsoft/signalr-protocol-msgpack';

const connection = HARRRConnection.create(builder => {
    builder.withUrl('https://server/hub');
    builder.withHubProtocol(new MessagePackHubProtocol());
});
```

## Swift Client

**Status:** Open — needs macOS testing. Server already supports MessagePack; Swift support depends on `signalr-client-swift` preview capabilities.

## Tests

- 5 .NET MessagePack integration tests (invoke, send, echo, guid, multi-param)
- 5 TypeScript MessagePack integration tests (same scenarios)
- All running alongside 50+ JSON tests on the same server
