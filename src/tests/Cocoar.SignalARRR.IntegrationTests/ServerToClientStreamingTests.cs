using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Cocoar.SignalARRR.Client;
using Microsoft.AspNetCore.SignalR.Client;
using TestShared;
using Xunit;

namespace Cocoar.SignalARRR.IntegrationTests {
    [Collection("Simple")]
    public class ServerToClientStreamingTests {
        private readonly SignalARRRServerInstanceFixture _fixture;

        public ServerToClientStreamingTests(SignalARRRServerInstanceFixture fixture) {
            _fixture = fixture;
        }

        [Fact]
        public async Task ServerRequestsStreamFromClient_ReceivesAllItems() {
            var ct = TestContext.Current.CancellationToken;
            var connection = HARRRConnection.Create(builder => builder.WithUrl($"{_fixture.ServerUrl}/signalr/testhub"));
            connection.RegisterInterface<ITestClientMethods, TestClientMethodsImpl>(new TestClientMethodsImpl());
            await connection.StartAsync(ct);
            await Task.Delay(100, ct); // Ensure connection is fully registered

            try {
                var connectionId = connection.ConnectionId;
                var count = 5;

                using var http = new HttpClient();
                http.Timeout = System.TimeSpan.FromSeconds(30);
                var url = $"{_fixture.ServerUrl}/__test/trigger-client-stream?connectionId={connectionId}&count={count}";
                var response = await http.PostAsync(url, content: null, ct);
                var result = await response.Content.ReadAsStringAsync(ct);
                Assert.True(response.IsSuccessStatusCode, $"Server returned {response.StatusCode}: {result}");
                var items = JsonSerializer.Deserialize<List<int>>(result);

                Assert.NotNull(items);
                Assert.Equal(count, items.Count);
                Assert.Equal(new[] { 0, 1, 2, 3, 4 }, items);
            } finally {
                await connection.StopAsync(ct);
                await connection.DisposeAsync();
            }
        }
    }
}
