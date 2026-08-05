import { REMOTE_REFERENCE_PROPERTY, RemoteReferenceKind, isRemoteReference } from './remote-reference.js';

export interface CancellationTokenReference {
  Id: string;
}

const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

/**
 * Recognises an argument the server sent in place of a cancellation token.
 *
 * The `__type` marker is exact and is preferred. Everything below it is a fallback for a server
 * that does not send the marker yet, and it is deliberately strict: the previous check accepted
 * *any* object carrying a string `Id`, so an ordinary payload such as
 * `{ Id: "user-42", Name: "Ada" }` was swapped for a cancellation token and never reached the
 * handler. Requiring a lone, GUID-shaped `Id` makes that collision unlikely — only the marker
 * rules it out.
 */
export function asCancellationTokenReference(v: unknown): CancellationTokenReference | undefined {
  if (typeof v !== 'object' || v === null) return undefined;

  const obj = v as Record<string, unknown>;
  const id = obj['Id'];
  if (typeof id !== 'string') return undefined;

  if (isRemoteReference(obj)) {
    return obj[REMOTE_REFERENCE_PROPERTY] === RemoteReferenceKind.CancellationToken ? { Id: id } : undefined;
  }

  return Object.keys(obj).length === 1 && GUID.test(id) ? { Id: id } : undefined;
}

/** @deprecated Prefer {@link asCancellationTokenReference}; this cannot return the id. */
export function isCancellationTokenReference(v: unknown): v is CancellationTokenReference {
  return asCancellationTokenReference(v) !== undefined;
}
