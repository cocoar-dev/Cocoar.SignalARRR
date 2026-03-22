/**
 * Structured error envelope from SignalARRR server exceptions.
 * The server serializes exceptions as JSON in the HubException message string.
 */
export interface HARRRError {
  Type: string;
  Message: string;
  StackTrace?: string;
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

  // Try JSON format
  try {
    const parsed = JSON.parse(jsonCandidate);
    if (typeof parsed === 'object' && parsed !== null && typeof parsed.Type === 'string' && typeof parsed.Message === 'string') {
      return parsed as HARRRError;
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
