import { describe, test, expect, beforeAll, afterAll } from 'vitest';
import * as signalR from '@microsoft/signalr';
import { HARRRConnection } from '../../src/index.js';

const SERVER_URL = process.env['SIGNALARRR_TEST_SERVER_URL'] ?? 'http://127.0.0.1:5000';

let connection: HARRRConnection;

beforeAll(async () => {
  connection = HARRRConnection.create((builder: signalR.HubConnectionBuilder) => {
    builder.withUrl(`${SERVER_URL}/signalr/testhub`);
  });

  // Register handlers with interface-qualified names (server sends "Namespace.IInterface|Method")
  connection
    .onServerMethod('TestShared.ITestClientMethods|Nix', () => null)
    .onServerMethod('TestShared.ITestClientMethods|GetById', (id: string) => id)
    .onServerMethod('TestShared.ITestClientMethods|GetContent', (count: number) => {
      const items: string[] = [];
      for (let i = 0; i < count; i++) items.push(`item-${i}`);
      return items;
    })
    // Plain names for fire-and-forget trigger (uses custom method names)
    .onServerMethod('GetById', (id: string) => id);

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

describe('Server → Client', () => {

  test('server calls client Nix (void) via typed proxy', async () => {
    const response = await triggerServerCall('__test/trigger-client-typed-call', {});
    const body = await response.text();
    expect(response.ok, `Server returned ${response.status}: ${body}`).toBe(true);
    expect(JSON.parse(body)).toBe('Sent');
  });

  test('server fires fire-and-forget call to client', async () => {
    const response = await triggerServerCall('__test/trigger-client-call', {
      method: 'GetById',
      arg: 'fire-forget-test',
    });
    const body = await response.text();
    expect(response.ok, `Server returned ${response.status}: ${body}`).toBe(true);
    expect(JSON.parse(body)).toBe('Sent');
  });

  test('server calls client GetById and receives return value', async () => {
    const response = await triggerServerCall('__test/trigger-client-getbyid', { id: 'hello-from-ts' });
    const body = await response.text();
    expect(response.ok, `Server returned ${response.status}: ${body}`).toBe(true);
    expect(JSON.parse(body)).toBe('hello-from-ts');
  });

  test('server calls client GetContent and receives list', async () => {
    const response = await triggerServerCall('__test/trigger-client-getcontent', { count: '3' });
    const body = await response.text();
    expect(response.ok, `Server returned ${response.status}: ${body}`).toBe(true);
    expect(JSON.parse(body)).toEqual(['item-0', 'item-1', 'item-2']);
  });

});
