export interface StreamReference {
  Uri: string;
}

export function isStreamReference(v: unknown): v is StreamReference {
  if (typeof v !== 'object' || v === null) return false;
  const obj = v as Record<string, unknown>;
  return typeof obj['Uri'] === 'string' && Object.keys(obj).length === 1;
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
