import { describe, test, expect, beforeAll, afterAll } from 'vitest';
import * as signalR from '@microsoft/signalr';
import { HARRRConnection } from '../../src/index.js';

const SERVER_URL = process.env['SIGNALARRR_TEST_SERVER_URL'] ?? 'http://127.0.0.1:5000';

let connection: HARRRConnection;

beforeAll(async () => {
  connection = HARRRConnection.create((builder: signalR.HubConnectionBuilder) => {
    builder.withUrl(`${SERVER_URL}/signalr/testhub`);
  });
  await connection.start();
});

afterAll(async () => {
  await connection.stop();
});

describe('Client → Server', () => {

  test('invoke returns string (async method)', async () => {
    const result = await connection.invoke<string>('GetNameAsync');
    expect(result).toBe('MyNameAsync');
  });

  test('invoke returns string (sync method)', async () => {
    const result = await connection.invoke<string>('GetName');
    expect(result).toBe('MyName');
  });

  test('invoke returns guid', async () => {
    const result = await connection.invoke<string>('GetGuidAsync');
    expect(result).toBeTruthy();
    expect(result).toMatch(/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i);
  });

  test('send void method completes without error', async () => {
    await connection.send('NothingAsync');
  });

  test('invoke echo returns same value', async () => {
    const result = await connection.invoke<string>('Echo', 'hello');
    expect(result).toBe('hello');
  });

});
