import { describe, it, expect, afterEach } from 'vitest';
import * as signalR from '@microsoft/signalr';
import { HARRRConnection } from '../../src/harrr-connection.js';
import type { ClientRequestMessage } from '../../src/models/client-request-message.js';
import { resolveStreamReference, transferAuthHeaders } from '../../src/models/stream-reference.js';

/**
 * The field on the wire is a `string`. A factory that returns a promise — which is what every
 * OAuth-backed application returns, and what SignalR's own contract allows — used to be serialised
 * as the promise object, so the server could not bind the message and every client-initiated call
 * on that connection failed. These tests hold all three send paths to the string.
 */

const TOKEN = 'header.payload.signature';

interface Frame {
  target: string;
  args: unknown[];
}

function createStub(accessTokenFactory?: unknown) {
  const frames: Frame[] = [];
  const handlers = new Map<string, (...args: any[]) => any>();
  const subscribers: signalR.IStreamSubscriber<unknown>[] = [];
  let innerDisposals = 0;

  const stub = {
    connection: { _options: accessTokenFactory ? { accessTokenFactory } : {} },
    on(name: string, handler: (...args: any[]) => any) {
      handlers.set(name, handler);
    },
    async invoke(target: string, ...args: unknown[]) {
      frames.push({ target, args });
      return target === 'RequestUploadSlot' ? 'https://host/hub/upload/slot-1' : 'result';
    },
    async send(target: string, ...args: unknown[]) {
      frames.push({ target, args });
    },
    stream(target: string, ...args: unknown[]) {
      frames.push({ target, args });
      return {
        subscribe(subscriber: signalR.IStreamSubscriber<unknown>) {
          subscribers.push(subscriber);
          return {
            dispose() {
              innerDisposals++;
            },
          };
        },
      };
    },
  };

  return {
    connection: new HARRRConnection(stub as unknown as signalR.HubConnection),
    frames,
    handlers,
    subscribers,
    innerDisposals: () => innerDisposals,
  };
}

function messageOf(frame: Frame): ClientRequestMessage {
  return frame.args[0] as ClientRequestMessage;
}

async function until(predicate: () => boolean, what: string): Promise<void> {
  for (let attempt = 0; attempt < 200; attempt++) {
    if (predicate()) return;
    await new Promise(resolve => setTimeout(resolve, 1));
  }
  throw new Error(`Timed out waiting for ${what}`);
}

describe('an asynchronous accessTokenFactory', () => {
  it('is awaited on invoke', async () => {
    const { connection, frames } = createStub(async () => TOKEN);

    await connection.invoke('Alerts.List');

    expect(typeof messageOf(frames[0]!).Authorization).toBe('string');
    expect(messageOf(frames[0]!).Authorization).toBe(TOKEN);
  });

  it('is awaited on send', async () => {
    const { connection, frames } = createStub(async () => TOKEN);

    await connection.send('Alerts.Acknowledge', 42);

    expect(typeof messageOf(frames[0]!).Authorization).toBe('string');
    expect(messageOf(frames[0]!).Authorization).toBe(TOKEN);
  });

  it('is awaited on stream — the path that used to hand the promise to the server', async () => {
    const { connection, frames } = createStub(async () => TOKEN);

    connection.stream('Alerts.Subscribe').subscribe({
      next: () => undefined,
      error: () => undefined,
      complete: () => undefined,
    });

    await until(() => frames.length > 0, 'the stream frame');
    expect(typeof messageOf(frames[0]!).Authorization).toBe('string');
    expect(messageOf(frames[0]!).Authorization).toBe(TOKEN);
  });

  it('reaches the server as a string through a challenge as well', async () => {
    const { handlers } = createStub(async () => TOKEN);

    // SignalR awaits the return value of a client-result handler, so a promise is resolved for us.
    await expect(handlers.get('ChallengeAuthentication')!({ Method: 'x' })).resolves.toBe(TOKEN);
  });
});

describe('other shapes of accessTokenFactory', () => {
  it('keeps working when the factory is synchronous', async () => {
    const { connection, frames } = createStub(() => TOKEN);

    await connection.invoke('Alerts.List');

    expect(messageOf(frames[0]!).Authorization).toBe(TOKEN);
  });

  it('sends an empty string when there is no factory', async () => {
    const { connection, frames } = createStub();

    await connection.invoke('Alerts.List');

    expect(messageOf(frames[0]!).Authorization).toBe('');
  });

  it('sends an empty string when the factory resolves to nothing', async () => {
    const { connection, frames } = createStub(async () => undefined);

    await connection.invoke('Alerts.List');

    expect(messageOf(frames[0]!).Authorization).toBe('');
  });

  it('reports a rejecting factory to the caller instead of sending a broken message', async () => {
    const { connection, frames } = createStub(async () => {
      throw new Error('token endpoint unreachable');
    });

    await expect(connection.invoke('Alerts.List')).rejects.toThrow('token endpoint unreachable');
    expect(frames).toHaveLength(0);
  });
});

describe('the deferred stream result', () => {
  it('forwards items and completion to a subscriber that arrived before the stream existed', async () => {
    const { connection, frames, subscribers } = createStub(async () => TOKEN);
    const received: unknown[] = [];
    let completed = false;

    connection.stream('Alerts.Subscribe').subscribe({
      next: value => received.push(value),
      error: () => undefined,
      complete: () => {
        completed = true;
      },
    });

    await until(() => subscribers.length > 0, 'the inner subscription');
    subscribers[0]!.next('first');
    subscribers[0]!.next('second');
    subscribers[0]!.complete();

    expect(frames[0]!.target).toBe('StreamMessage');
    expect(received).toEqual(['first', 'second']);
    expect(completed).toBe(true);
  });

  it('reports a rejecting factory through the subscriber rather than throwing out of stream()', async () => {
    const { connection, frames } = createStub(async () => {
      throw new Error('token endpoint unreachable');
    });
    let reported: unknown;

    // Not a throw: `stream()` returns before the token is known, so this is the only place left
    // where the caller can see the failure.
    connection.stream('Alerts.Subscribe').subscribe({
      next: () => undefined,
      error: err => {
        reported = err;
      },
      complete: () => undefined,
    });

    await until(() => reported !== undefined, 'the reported error');
    expect((reported as Error).message).toBe('token endpoint unreachable');
    expect(frames).toHaveLength(0);
  });

  it('never subscribes to the stream when disposed before the token resolves', async () => {
    const { connection, subscribers, innerDisposals } = createStub(
      () => new Promise<string>(resolve => setTimeout(() => resolve(TOKEN), 10)),
    );

    const subscription = connection.stream('Alerts.Subscribe').subscribe({
      next: () => undefined,
      error: () => undefined,
      complete: () => undefined,
    });
    subscription.dispose();

    await new Promise(resolve => setTimeout(resolve, 30));
    expect(subscribers).toHaveLength(0);
    expect(innerDisposals()).toBe(0);
  });

  it('disposes the underlying subscription once the stream is running', async () => {
    const { connection, subscribers, innerDisposals } = createStub(async () => TOKEN);

    const subscription = connection.stream('Alerts.Subscribe').subscribe({
      next: () => undefined,
      error: () => undefined,
      complete: () => undefined,
    });

    await until(() => subscribers.length > 0, 'the inner subscription');
    subscription.dispose();
    expect(innerDisposals()).toBe(1);
  });
});

describe('outgoing arguments on the stream path', () => {
  const originalFetch = globalThis.fetch;

  afterEach(() => {
    globalThis.fetch = originalFetch;
  });

  it('uploads binary arguments and sends a stream reference, as invoke and send do', async () => {
    // This path could not upload anything while the message was built synchronously, so a Blob went
    // out as a raw object. The .NET and Swift clients prepare stream arguments here too.
    globalThis.fetch = (async () => ({ ok: true })) as unknown as typeof fetch;
    const { connection, frames } = createStub(async () => TOKEN);

    connection.stream('Alerts.Upload', new Blob(['payload'])).subscribe({
      next: () => undefined,
      error: () => undefined,
      complete: () => undefined,
    });

    await until(() => frames.some(f => f.target === 'StreamMessage'), 'the stream frame');
    expect(frames[0]!.target).toBe('RequestUploadSlot');
    const message = messageOf(frames.find(f => f.target === 'StreamMessage')!);
    expect(message.Arguments[0]).toEqual({ Uri: 'https://host/hub/upload/slot-1' });
  });
});

describe('file transfer credentials', () => {
  const originalFetch = globalThis.fetch;

  afterEach(() => {
    globalThis.fetch = originalFetch;
  });

  it('builds a Bearer header from a bare token and passes a scheme through verbatim', () => {
    // Mirrors the server, which prefixes 'Bearer ' only when the credential carries no scheme.
    expect(transferAuthHeaders('abc')).toEqual({ Authorization: 'Bearer abc' });
    expect(transferAuthHeaders('Basic dXNlcjpwdw==')).toEqual({ Authorization: 'Basic dXNlcjpwdw==' });
    expect(transferAuthHeaders(undefined)).toEqual({});
    expect(transferAuthHeaders('')).toEqual({});
  });

  it('sends the credential when downloading a stream reference', async () => {
    // The endpoint carries the hub's [Authorize], so a bare fetch got 401 and the argument never
    // reached the handler.
    const requests: Array<{ url: string; init?: RequestInit }> = [];
    globalThis.fetch = (async (url: string, init?: RequestInit) => {
      requests.push({ url, init });
      return { ok: true, arrayBuffer: async () => new ArrayBuffer(4) };
    }) as unknown as typeof fetch;

    await resolveStreamReference({ Uri: 'https://host/hub/download/1' }, TOKEN);

    expect((requests[0]!.init!.headers as Record<string, string>)['Authorization']).toBe(`Bearer ${TOKEN}`);
  });

  it('sends the credential when uploading, and surfaces a rejected upload', async () => {
    const requests: Array<{ url: string; init?: RequestInit }> = [];
    globalThis.fetch = (async (url: string, init?: RequestInit) => {
      requests.push({ url, init });
      return { ok: false, status: 401, statusText: 'Unauthorized' };
    }) as unknown as typeof fetch;
    const { connection } = createStub(async () => TOKEN);

    // The response used to be discarded, so a 401 produced a stream reference pointing at nothing.
    await expect(connection.invoke('Alerts.Upload', new Blob(['x']))).rejects.toThrow('401');

    const headers = requests[0]!.init!.headers as Record<string, string>;
    expect(headers['Authorization']).toBe(`Bearer ${TOKEN}`);
    expect(headers['Content-Type']).toBe('application/octet-stream');
  });
});
