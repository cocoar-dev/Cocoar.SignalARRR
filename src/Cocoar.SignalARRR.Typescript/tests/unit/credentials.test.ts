import { describe, it, expect } from 'vitest';
import * as signalR from '@microsoft/signalr';
import { HARRRConnection } from '../../src/harrr-connection.js';
import type { HARRRConnectionOptions } from '../../src/harrr-connection-options.js';

/**
 * The credential options — `credential`, `connectionCredential`, `messageCredential` — named and
 * behaving as in every SignalARRR client: nothing coupled implicitly, and a clear error instead of
 * a silent choice when a credential is set in two places.
 */

function stubConnection() {
  const frames: { target: string; args: unknown[] }[] = [];
  const stub = {
    on() {},
    async invoke(target: string, ...args: unknown[]) {
      frames.push({ target, args });
      return 'result';
    },
  };
  return { hubConnection: stub as unknown as signalR.HubConnection, frames };
}

async function sentAuthorization(options: HARRRConnectionOptions): Promise<unknown> {
  const { hubConnection, frames } = stubConnection();
  const connection = new HARRRConnection(hubConnection, options);
  await connection.invoke('Anything');
  return (frames[0]!.args[0] as { Authorization: unknown }).Authorization;
}

describe('credential options', () => {
  it('sends the message credential with every message', async () => {
    expect(await sentAuthorization({ messageCredential: 'message-token' })).toBe('message-token');
    expect(await sentAuthorization({ messageCredential: async () => 'async-token' })).toBe('async-token');
  });

  it('still honours the former authorization option', async () => {
    expect(await sentAuthorization({ authorization: 'legacy-token' })).toBe('legacy-token');
  });

  it('rejects authorization together with messageCredential or credential', () => {
    const { hubConnection } = stubConnection();
    expect(() => new HARRRConnection(hubConnection, { authorization: 'a', messageCredential: 'b' })).toThrow(/former name/);
    expect(() => HARRRConnection.create('http://127.0.0.1:1/hub', { authorization: 'a', credential: 'b' })).toThrow(/former name/);
  });

  it('rejects a connection credential on a HubConnection that is already built', () => {
    const { hubConnection } = stubConnection();
    expect(() => new HARRRConnection(hubConnection, { connectionCredential: 'c' })).toThrow(/already built/);
    expect(() => HARRRConnection.create(hubConnection, { credential: 'c' })).toThrow(/already built/);
    expect(() => HARRRConnection.create((b) => b.withUrl('http://127.0.0.1:1/hub'), { credential: 'c' })).toThrow(/already built/);
  });

  it('rejects a connection credential set through SignalR and SignalARRR at once', () => {
    expect(() =>
      HARRRConnection.create('http://127.0.0.1:1/hub', {
        connectionCredential: 'signalarrr',
        httpConnectionOptions: { accessTokenFactory: () => 'signalr' },
      }),
    ).toThrow(/set twice/);
  });

  it('builds a connection from a URL with the connection credential applied', () => {
    const connection = HARRRConnection.create('http://127.0.0.1:1/hub', { credential: 'both' });
    expect(connection.asSignalRHubConnection()).toBeInstanceOf(signalR.HubConnection);
  });
});
