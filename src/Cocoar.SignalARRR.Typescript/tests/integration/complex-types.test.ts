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

describe('Complex Types', () => {

  test('Guid parameter passes correctly', async () => {
    const guid = '550e8400-e29b-41d4-a716-446655440000';
    const result = await connection.invoke<string>('ExtraMethods.GuidToString', guid);
    expect(result).toBe(guid);
  });

  test('List returned correctly', async () => {
    const result = await connection.invoke<string[]>('ExtraMethods.GenerateItems', 4);
    expect(result).toEqual(['item-0', 'item-1', 'item-2', 'item-3']);
  });

  test('Dictionary returned correctly', async () => {
    const result = await connection.invoke<Record<string, number>>('ExtraMethods.WordLengths', 'hello world');
    expect(result).toEqual({ hello: 5, world: 5 });
  });

  test('multiple parameter types work together', async () => {
    const result = await connection.invoke<string>('ExtraMethods.Combine', 'test', 42, true);
    expect(result).toBe('test-42-True');
  });

  test('DateTime serializes correctly', async () => {
    const result = await connection.invoke<string>('ExtraMethods.FormatDate', '2025-06-15T00:00:00Z');
    expect(result).toBe('2025-06-15');
  });

});
