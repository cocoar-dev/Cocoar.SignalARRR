# HTTP Stream References

File transfer via `Stream` parameters works in both directions for all three clients, with both buffered and streaming download options.

## Streaming Download (Not Buffered)

**Status:** Implemented

All three clients now offer both buffered and streaming download options:

| Client | Buffered (default) | Streaming |
|--------|-------------------|-----------|
| .NET | `ProcessStreamArgument()` → `Stream` | Same — already streaming via `ResponseHeadersRead` |
| .NET | `ProcessStreamArgumentBuffered()` → `byte[]` | — |
| TypeScript | `resolveStreamReference()` → `ArrayBuffer` | `resolveStreamReferenceAsStream()` → `ReadableStream<Uint8Array>` |
| Swift | `StreamReferenceResolver.resolve()` → `Data` | `StreamReferenceResolver.resolveAsStream()` → `URLSession.AsyncBytes` |

The default `_prepareArgs` in all clients uses the buffered variant for backward compatibility. Developers can use the streaming variants directly when handling large files.

**.NET** additionally uses `HttpCompletionOption.ResponseHeadersRead` so even the default `Stream` return doesn't wait for the full response body before returning.

## Extensible Reference Type Registry

**Status:** Future consideration

Currently, the reference type system is hard-coded for `Stream` → `StreamReference`. If more types need the "send reference, resolve on the other side" pattern, the system should be made extensible with an `IReferenceTypeHandler` interface and a registry. Build this when a second use case emerges — not before.
