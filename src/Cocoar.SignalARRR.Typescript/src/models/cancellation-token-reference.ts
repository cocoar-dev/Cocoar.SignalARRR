export interface CancellationTokenReference {
  Id: string;
}

export function isCancellationTokenReference(v: unknown): v is CancellationTokenReference {
  return typeof v === 'object' && v !== null && typeof (v as Record<string, unknown>)['Id'] === 'string';
}
