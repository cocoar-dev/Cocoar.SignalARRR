/**
 * The `__type` marker the server puts on arguments that are handles rather than values.
 *
 * Some arguments are not data but references: a cancellation token the server can trip later, a
 * stream the client has to fetch. The client has to recognise them to swap them back, and it used
 * to do that by guessing from the shape. Guessing is wrong on ordinary data that happens to look
 * the same — a payload with a lone string `Id` was silently replaced by a cancellation token. The
 * .NET clients never had the problem because they know the parameter types; this one does not.
 */
export const REMOTE_REFERENCE_PROPERTY = '__type';

export const RemoteReferenceKind = {
  CancellationToken: 'cancellationToken',
  Stream: 'stream',
} as const;

export type RemoteReferenceKind = (typeof RemoteReferenceKind)[keyof typeof RemoteReferenceKind];

/** Whether the value carries the marker at all, regardless of which kind it names. */
export function isRemoteReference(v: unknown): v is Record<string, unknown> {
  return (
    typeof v === 'object' &&
    v !== null &&
    typeof (v as Record<string, unknown>)[REMOTE_REFERENCE_PROPERTY] === 'string'
  );
}
