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

describe('Multiple ServerMethods Classes', () => {

  test('second class: Greet returns greeting', async () => {
    const result = await connection.invoke<string>('ExtraMethods.Greet', 'World');
    expect(result).toBe('Hello, World!');
  });

  test('second class: Add returns sum', async () => {
    const result = await connection.invoke<number>('ExtraMethods.Add', 3, 4);
    expect(result).toBe(7);
  });

  test('[MessageName] attribute: CustomEcho works', async () => {
    const result = await connection.invoke<string>('ExtraMethods.CustomEcho', 'test-value');
    expect(result).toBe('test-value');
  });

  test('original hub methods still work alongside second class', async () => {
    const result = await connection.invoke<string>('GetNameAsync');
    expect(result).toBe('MyNameAsync');
  });

});
