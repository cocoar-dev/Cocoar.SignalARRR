import { describe, test, expect, beforeAll, afterAll } from 'vitest';
import * as signalR from '@microsoft/signalr';
import { HARRRConnection } from '../../src/index.js';

const SERVER_URL = process.env['SIGNALARRR_TEST_SERVER_URL'] ?? 'http://127.0.0.1:5000';

let connection: HARRRConnection;

beforeAll(async () => {
  connection = HARRRConnection.create((builder: signalR.HubConnectionBuilder) => {
    builder.withUrl(`${SERVER_URL}/signalr/testhub`);
  });

  // Register a stream handler that yields integers
  connection.onServerStreamMethod(
    'TestShared.ITestClientMethods|StreamNumbers',
    (count: unknown) => {
      const n = count as number;
      return (async function* () {
        for (let i = 0; i < n; i++) {
          yield i;
        }
      })();
    },
  );

  await connection.start();
  // Wait for server to register client in ClientManager
  await new Promise(r => setTimeout(r, 500));
});

afterAll(async () => {
  await connection.stop();
});

async function triggerServerCall(endpoint: string, params: Record<string, string>): Promise<Response> {
  const query = new URLSearchParams({
    connectionId: connection.connectionId!,
    ...params,
  });
  return fetch(`${SERVER_URL}/${endpoint}?${query}`, { method: 'POST' });
}

describe('Client → Server Streaming', () => {

  test('server requests stream from TypeScript client and receives all items', async () => {
    const response = await triggerServerCall('__test/trigger-client-stream', { count: '5' });
    const body = await response.text();
    expect(response.ok, `Server returned ${response.status}: ${body}`).toBe(true);

    const items = JSON.parse(body);
    expect(items).toEqual([0, 1, 2, 3, 4]);
  });

});
