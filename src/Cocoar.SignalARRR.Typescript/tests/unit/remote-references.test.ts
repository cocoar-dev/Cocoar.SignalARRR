import { describe, it, expect } from 'vitest';
import { asCancellationTokenReference } from '../../src/models/cancellation-token-reference.js';
import { isStreamReference } from '../../src/models/stream-reference.js';

const GUID = '3f2504e0-4f89-11d3-9a0c-0305e82c3301';

describe('cancellation token references', () => {
  it('recognises a marked reference', () => {
    expect(asCancellationTokenReference({ __type: 'cancellationToken', Id: GUID })).toEqual({ Id: GUID });
  });

  it('recognises an unmarked one, for a server that does not send the marker yet', () => {
    expect(asCancellationTokenReference({ Id: GUID })).toEqual({ Id: GUID });
  });

  it('does not mistake a payload that merely has an Id', () => {
    // This is the collision the previous check fell for: any object with a string Id was swapped
    // for a cancellation token, and the real argument never reached the handler.
    expect(asCancellationTokenReference({ Id: 'user-42', Name: 'Ada' })).toBeUndefined();
    expect(asCancellationTokenReference({ Id: GUID, Name: 'Ada' })).toBeUndefined();
    expect(asCancellationTokenReference({ Id: 'order-7' })).toBeUndefined();
  });

  it('trusts the marker over the shape', () => {
    // Marked as something else: not a cancellation token, however much it looks like one.
    expect(asCancellationTokenReference({ __type: 'stream', Id: GUID })).toBeUndefined();
  });

  it('rejects non-objects', () => {
    for (const value of [null, undefined, 42, 'text', [GUID]]) {
      expect(asCancellationTokenReference(value)).toBeUndefined();
    }
  });
});

describe('stream references', () => {
  it('recognises a marked reference', () => {
    expect(isStreamReference({ __type: 'stream', Uri: 'https://host/x' })).toBe(true);
  });

  it('recognises an unmarked one', () => {
    expect(isStreamReference({ Uri: 'https://host/x' })).toBe(true);
  });

  it('does not mistake a payload that merely has a Uri', () => {
    expect(isStreamReference({ Uri: 'https://host/x', Title: 'doc' })).toBe(false);
  });

  it('trusts the marker over the shape', () => {
    expect(isStreamReference({ __type: 'cancellationToken', Uri: 'https://host/x' })).toBe(false);
  });
});
