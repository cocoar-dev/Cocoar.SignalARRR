import { REMOTE_REFERENCE_PROPERTY, RemoteReferenceKind, isRemoteReference } from './remote-reference.js';

export interface StreamReference {
  Uri: string;
}

/**
 * Recognises an argument the server sent in place of a stream.
 *
 * The `__type` marker is exact and is preferred; the lone-`Uri` check below it is the fallback for
 * a server that does not send the marker yet. Note that the fallback must not simply count keys any
 * more: a marked reference has two, and rejecting it on that basis is how adding the marker would
 * have broken stream arguments outright.
 */
export function isStreamReference(v: unknown): v is StreamReference {
  if (typeof v !== 'object' || v === null) return false;

  const obj = v as Record<string, unknown>;
  if (typeof obj['Uri'] !== 'string') return false;

  if (isRemoteReference(obj)) {
    return obj[REMOTE_REFERENCE_PROPERTY] === RemoteReferenceKind.Stream;
  }

  return Object.keys(obj).length === 1;
}

/** Resolve a StreamReference by downloading the data — returns the full content buffered in memory. */
export async function resolveStreamReference(ref: StreamReference): Promise<ArrayBuffer> {
  const response = await fetchStreamReference(ref);
  return response.arrayBuffer();
}

/** Resolve a StreamReference as a ReadableStream — for large files, avoids buffering in memory. */
export async function resolveStreamReferenceAsStream(ref: StreamReference): Promise<ReadableStream<Uint8Array>> {
  const response = await fetchStreamReference(ref);
  if (!response.body) {
    throw new Error('StreamReference: response has no body stream');
  }
  return response.body;
}

async function fetchStreamReference(ref: StreamReference): Promise<Response> {
  const url = ref.Uri;
  const scheme = url.split(':')[0]?.toLowerCase();
  if (scheme !== 'http' && scheme !== 'https') {
    throw new Error(`StreamReference: unsupported URI scheme '${scheme}'`);
  }
  const response = await fetch(url);
  if (!response.ok) {
    throw new Error(`StreamReference: download failed (${response.status} ${response.statusText})`);
  }
  return response;
}
