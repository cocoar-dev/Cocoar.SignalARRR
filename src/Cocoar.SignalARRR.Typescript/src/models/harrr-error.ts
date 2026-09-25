/**
 * The machine-readable error codes of the wire contract — the same set as `HARRRErrorCodes` in the
 * .NET, Swift and Kotlin clients. Branch on these: a code is the only cross-language signal, a .NET
 * type name means nothing here. Application code may put its own codes on the wire
 * (`HARRRException("room_full", ...)` on the server); those travel verbatim in `code`, while
 * `normalizedCode` folds anything this client does not know to `internal`.
 */
export const HARRRErrorCodes = {
  /** Authorization rejected the call. */
  Unauthorized: 'unauthorized',
  /** No method (or interface) is registered under the requested name. */
  MethodNotFound: 'method_not_found',
  /** The name exists, but no registered method accepts this argument count. */
  InvalidArgumentCount: 'invalid_argument_count',
  /** An argument could not be deserialized or coerced to the parameter type. */
  ArgumentBindingFailed: 'argument_binding_failed',
  /** The call was cancelled — an expected outcome, not a failure. */
  Cancelled: 'cancelled',
  /** A server-side deadline expired (e.g. waiting for a stream upload). */
  Timeout: 'timeout',
  /** No client answered the invoke (locally or across the backplane). */
  NoClientResponded: 'no_client_responded',
  /** The connection already holds as many unused upload slots as it is allowed to. */
  UploadSlotLimitReached: 'upload_slot_limit_reached',
  /** The invoked method itself threw — the default bucket. */
  Internal: 'internal',
} as const;

const knownCodes: ReadonlySet<string> = new Set(Object.values(HARRRErrorCodes));

/** Maps a wire code to one this client knows; unknown or missing codes become `internal`. */
export function normalizeErrorCode(code: string | undefined | null): string {
  return code != null && knownCodes.has(code) ? code : HARRRErrorCodes.Internal;
}

/**
 * Structured error envelope from SignalARRR server exceptions.
 * The server serializes exceptions as JSON in the HubException message string.
 */
export interface HARRRError {
  /** Envelope version; 1 and up carry `Code`. */
  Version?: number;
  /** The machine-readable code — see `HARRRErrorCodes`. */
  Code?: string;
  /** The .NET exception type, for diagnostics. */
  Type: string;
  Message: string;
  StackTrace?: string;
  InnerError?: HARRRError;
}

/**
 * What `invoke()` rejects with when the server returns an error. `code` is what to branch on —
 * compare `normalizedCode` against `HARRRErrorCodes`; `message` is for humans; `type` is the .NET
 * exception type.
 */
export interface HARRRInvocationError {
  type: string;
  message: string;
  /** The code as sent, including application-defined codes; `undefined` from an older server. */
  code: string | undefined;
  /** `code` folded to one of `HARRRErrorCodes`; unknown or missing codes become `internal`. */
  normalizedCode: string;
}

/**
 * Parse a HubException message into a structured HARRRError.
 * Supports both the new JSON format and the legacy `[Type] Message` format.
 *
 * SignalR wraps HubException messages with prefix text like:
 * "An unexpected error occurred invoking '...' on the server. HARRRException: {json}"
 */
export function parseHARRRError(error: unknown): HARRRError {
  const msg = error instanceof Error ? error.message : String(error);

  // Extract JSON after "HARRRException: " marker (SignalR wrapping)
  const marker = 'HARRRException: ';
  const markerIndex = msg.indexOf(marker);
  const jsonCandidate = markerIndex >= 0 ? msg.substring(markerIndex + marker.length) : msg;

  // Try JSON format. Accepted when it carries actual error content — a versioned envelope, a code,
  // or a type and message — the same test as the .NET and Kotlin clients, plus the Type/Message
  // shape this client always accepted.
  try {
    const parsed = JSON.parse(jsonCandidate);
    if (typeof parsed === 'object' && parsed !== null) {
      const versioned = typeof parsed.Version === 'number' && parsed.Version >= 1;
      const coded = typeof parsed.Code === 'string' && parsed.Code.length > 0;
      const typed = typeof parsed.Type === 'string' && typeof parsed.Message === 'string';
      if (versioned || coded || typed) {
        return {
          ...parsed,
          Type: typeof parsed.Type === 'string' && parsed.Type.length > 0 ? parsed.Type : 'Error',
          Message: typeof parsed.Message === 'string' ? parsed.Message : '',
        } as HARRRError;
      }
    }
  } catch {
    // Not JSON — try legacy format
  }

  // Legacy format: [Type] Message
  const matches = /\[([\w.]+)\]\s*(.*)/m.exec(msg);
  if (matches) {
    return {
      Type: matches[1] ?? 'Error',
      Message: matches[2] ?? msg,
    };
  }

  // Fallback
  return {
    Type: 'Error',
    Message: msg,
  };
}
