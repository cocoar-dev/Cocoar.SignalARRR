using System.Net.Http;
using System.Threading.Tasks;
using Cocoar.SignalARRR.Client;
using Microsoft.AspNetCore.SignalR.Client;
using TestShared;
using Xunit;

namespace Cocoar.SignalARRR.IntegrationTests {
    [Collection("Simple")]
    public class CancellationTokenTests {
        private readonly SignalARRRServerInstanceFixture _fixture;

        public CancellationTokenTests(SignalARRRServerInstanceFixture fixture) {
            _fixture = fixture;
        }

        [Fact]
        public async Task ServerCancelsClientCancellationToken_ClientReceivesCancellation() {
            var ct = TestContext.Current.CancellationToken;
            var connection = HARRRConnection.Create(builder => builder.WithUrl($"{_fixture.ServerUrl}/signalr/testhub"));
            connection.RegisterInterface<ITestClientMethods, TestClientMethodsImpl>(new TestClientMethodsImpl());
            await connection.StartAsync(ct);
            await Task.Delay(100, ct); // Ensure connection is fully registered

            try {
                var connectionId = connection.ConnectionId;

                using var http = new HttpClient();
                http.Timeout = System.TimeSpan.FromSeconds(30);
                var url = $"{_fixture.ServerUrl}/__test/trigger-client-cancellation?connectionId={connectionId}&delayMs=200";
                var response = await http.PostAsync(url, content: null, ct);
                var result = await response.Content.ReadAsStringAsync(ct);
                Assert.True(response.IsSuccessStatusCode, $"Server returned {response.StatusCode}: {result}");
                Assert.Contains("cancelled", result.ToLower());
            } finally {
                await connection.StopAsync(ct);
                await connection.DisposeAsync();
            }
        }
    }
}
