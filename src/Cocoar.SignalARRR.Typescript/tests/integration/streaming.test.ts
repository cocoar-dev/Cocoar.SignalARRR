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

describe('Streaming', () => {

  test('stream receives all items', async () => {
    const items: number[] = [];

    await new Promise<void>((resolve, reject) => {
      connection.stream<number>('Counter', 5, 10).subscribe({
        next: (item) => items.push(item),
        complete: () => resolve(),
        error: (err) => reject(err),
      });
    });

    expect(items).toEqual([0, 1, 2, 3, 4]);
  });

});
