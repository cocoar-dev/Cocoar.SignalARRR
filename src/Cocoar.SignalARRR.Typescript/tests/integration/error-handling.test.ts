import { describe, test, expect, beforeAll, afterAll } from 'vitest';
import * as signalR from '@microsoft/signalr';
import { HARRRConnection, parseHARRRError } from '../../src/index.js';

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

describe('Error Handling', () => {

  test('structured error: ArgumentException has correct type and message', async () => {
    try {
      await connection.invoke<string>('ExtraMethods.ThrowArgumentException', 'testParam');
      expect.fail('Expected an error to be thrown');
    } catch (err: any) {
      // invoke() parses errors via _extractException → { type, message }
      expect(err.type).toBe('System.ArgumentException');
      expect(err.message).toContain('Invalid value provided');
    }
  });

  test('structured error: InvalidOperationException has correct type and message', async () => {
    try {
      await connection.invoke<string>('ExtraMethods.ThrowInvalidOperation');
      expect.fail('Expected an error to be thrown');
    } catch (err: any) {
      expect(err.type).toBe('System.InvalidOperationException');
      expect(err.message).toBe('This operation is not allowed');
    }
  });

});
