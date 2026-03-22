import { describe, test, expect, beforeAll, afterAll } from 'vitest';
import * as signalR from '@microsoft/signalr';
import { MessagePackHubProtocol } from '@microsoft/signalr-protocol-msgpack';
import { HARRRConnection } from '../../src/index.js';

const SERVER_URL = process.env['SIGNALARRR_TEST_SERVER_URL'] ?? 'http://127.0.0.1:5000';

let connection: HARRRConnection;

beforeAll(async () => {
  connection = HARRRConnection.create((builder: signalR.HubConnectionBuilder) => {
    builder.withUrl(`${SERVER_URL}/signalr/testhub`);
    builder.withHubProtocol(new MessagePackHubProtocol());
  });
  await connection.start();
});

afterAll(async () => {
  await connection.stop();
});

describe('MessagePack Protocol', () => {

  test('invoke returns string', async () => {
    const result = await connection.invoke<string>('GetNameAsync');
    expect(result).toBe('MyNameAsync');
  });

  test('invoke returns guid', async () => {
    const result = await connection.invoke<string>('GetGuidAsync');
    expect(result).toBeTruthy();
  });

  test('send void method', async () => {
    await connection.send('NothingAsync');
  });

  test('echo', async () => {
    const result = await connection.invoke<string>('Echo', 'hello-msgpack');
    expect(result).toBe('hello-msgpack');
  });

  test('multiple parameter types', async () => {
    const result = await connection.invoke<string>('ExtraMethods.Combine', 'test', 42, true);
    expect(result).toBe('test-42-True');
  });

});
