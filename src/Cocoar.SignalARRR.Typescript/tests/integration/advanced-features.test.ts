import { describe, test, expect, beforeAll, afterAll } from 'vitest';
import * as signalR from '@microsoft/signalr';
import { HARRRConnection } from '../../src/index.js';

const SERVER_URL = process.env['SIGNALARRR_TEST_SERVER_URL'] ?? 'http://127.0.0.1:5000';

describe('Advanced Features', () => {

  test('[FromServices] injects service provider', async () => {
    const connection = HARRRConnection.create((builder: signalR.HubConnectionBuilder) => {
      builder.withUrl(`${SERVER_URL}/signalr/testhub`);
    });
    await connection.start();

    try {
      // Client sends NO argument — server injects IServiceProvider via [FromServices]
      const result = await connection.invoke<string>('ExtraMethods.GetServiceInfo');
      expect(result).toBe('ServiceProviderInjected');
    } finally {
      await connection.stop();
    }
  });

  test('client attributes from headers are accessible', async () => {
    const connection = HARRRConnection.create((builder: signalR.HubConnectionBuilder) => {
      builder.withUrl(`${SERVER_URL}/signalr/testhub`, {
        headers: {
          '#AppVersion': '3.0.0',
          '#ClientType': 'TypeScript',
        },
      });
    });
    await connection.start();
    // Wait for server to register client
    await new Promise(r => setTimeout(r, 500));

    try {
      const query = new URLSearchParams({ connectionId: connection.connectionId! });
      const response = await fetch(`${SERVER_URL}/__test/get-client-attributes?${query}`, { method: 'POST' });
      expect(response.ok).toBe(true);

      const attrs = await response.json() as Record<string, string>;
      expect(attrs['AppVersion']).toBe('3.0.0');
      expect(attrs['ClientType']).toBe('TypeScript');
    } finally {
      await connection.stop();
    }
  });

});
