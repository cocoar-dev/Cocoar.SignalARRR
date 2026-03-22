import { describe, test, expect, beforeAll, afterAll } from 'vitest';
import * as signalR from '@microsoft/signalr';
import { HARRRConnection } from '../../src/index.js';

const SERVER_URL = process.env['SIGNALARRR_TEST_SERVER_URL'] ?? 'http://127.0.0.1:5000';

let connection: HARRRConnection;

beforeAll(async () => {
  connection = HARRRConnection.create((builder: signalR.HubConnectionBuilder) => {
    builder.withUrl(`${SERVER_URL}/signalr/testhub`);
  });

  // Register handler that returns binary data (ArrayBuffer) — triggers upload flow
  connection.onServerMethod(
    'TestShared.ITestClientMethods|GetFileStream',
    (content: unknown) => {
      const text = String(content);
      const encoder = new TextEncoder();
      return encoder.encode(text).buffer;  // Returns ArrayBuffer
    },
  );

  await connection.start();
  await new Promise(r => setTimeout(r, 500));
});

afterAll(async () => {
  await connection.stop();
});

describe('Stream Return Value (Client → Server File Transfer)', () => {

  test('server calls client GetFileStream and receives stream content via HTTP upload', async () => {
    const query = new URLSearchParams({
      connectionId: connection.connectionId!,
      content: 'HelloFromTypeScript',
    });
    const response = await fetch(`${SERVER_URL}/__test/trigger-client-getfilestream?${query}`, {
      method: 'POST',
    });
    const body = await response.text();
    expect(response.ok, `Server returned ${response.status}: ${body}`).toBe(true);

    // Server received the stream content that was uploaded via HTTP
    expect(JSON.parse(body)).toBe('HelloFromTypeScript');
  });

});
