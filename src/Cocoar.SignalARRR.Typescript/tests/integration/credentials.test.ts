import { describe, test, expect } from 'vitest';
import * as signalR from '@microsoft/signalr';
import { HARRRConnection } from '../../src/index.js';

const SERVER_URL = process.env['SIGNALARRR_TEST_SERVER_URL'] ?? 'http://127.0.0.1:5000';

/**
 * `HARRRConnection.create(url, { connectionCredential })` hands the credential to SignalR as its
 * `accessTokenFactory`. `TestHub.TransportCredential` reports where the transport request carried
 * it; the test server does not copy the query into the header. Where the token travels is SignalR's
 * own choice: under Node a header for WebSocket and Long Polling, the URL for SSE.
 */
describe('Connection credential', () => {
  async function transportCredential(transport: signalR.HttpTransportType): Promise<string> {
    const connection = HARRRConnection.create(`${SERVER_URL}/signalr/testhub`, {
      connectionCredential: async () => 'probe-token',
      httpConnectionOptions: { transport },
    });
    await connection.start();
    try {
      return await connection.invoke<string>('TransportCredential');
    } finally {
      await connection.stop();
    }
  }

  test('reaches the WebSocket transport as a header', async () => {
    expect(await transportCredential(signalR.HttpTransportType.WebSockets)).toBe('header=Bearer probe-token;query=-');
  });

  test('reaches the Long Polling transport as a header', async () => {
    expect(await transportCredential(signalR.HttpTransportType.LongPolling)).toBe('header=Bearer probe-token;query=-');
  });

  test('a rejected negotiate reports its HTTP status', async () => {
    const connection = HARRRConnection.create(`${SERVER_URL}/signalr/no-such-hub`, {}, (builder) =>
      builder.configureLogging(signalR.LogLevel.None),
    );
    const error = await connection.start().then(
      () => undefined,
      (e: unknown) => e as { statusCode?: number },
    );
    expect(error?.statusCode).toBe(404);
  });

  test('reaches the SSE transport in the URL, as SignalR sends it under Node', async () => {
    expect(await transportCredential(signalR.HttpTransportType.ServerSentEvents)).toBe('header=-;query=probe-token');
  });
});
