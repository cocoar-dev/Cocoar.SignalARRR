using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Cocoar.SignalARRR.Client;

namespace Cocoar.SignalARRR.IntegrationTests {
    public static class TestHelper {
        /// <summary>
        /// Wait until the server has registered the client in ClientManager.
        /// Polls the /__test/client-exists endpoint instead of using a fixed delay.
        /// </summary>
        public static async Task WaitForClientRegistration(
            string serverUrl, HARRRConnection connection, CancellationToken ct) {
            using var http = new HttpClient();
            for (int i = 0; i < 50; i++) {
                var connectionId = Uri.EscapeDataString(connection.ConnectionId ?? string.Empty);
                var response = await http.GetAsync(
                    $"{serverUrl}/__test/client-exists?connectionId={connectionId}", ct);
                if (response.IsSuccessStatusCode) {
                    var body = await response.Content.ReadAsStringAsync(ct);
                    if (body.Contains("true")) return;
                }
                await Task.Delay(100, ct);
            }
            throw new System.TimeoutException("Client was not registered in ClientManager within 5 seconds");
        }
    }
}
