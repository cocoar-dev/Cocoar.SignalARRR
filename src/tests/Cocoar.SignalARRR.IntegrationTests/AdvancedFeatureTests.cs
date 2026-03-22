using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Cocoar.SignalARRR.Client;
using Cocoar.SignalARRR.Common;
using Microsoft.AspNetCore.SignalR.Client;
using TestShared;
using Xunit;

namespace Cocoar.SignalARRR.IntegrationTests {
    [Collection("Simple")]
    public class AdvancedFeatureTests {
        private readonly SignalARRRServerInstanceFixture _fixture;

        public AdvancedFeatureTests(SignalARRRServerInstanceFixture fixture) {
            _fixture = fixture;
        }

        [Fact]
        public async Task FromServices_InjectsServiceProvider() {
            var ct = TestContext.Current.CancellationToken;
            var connection = HARRRConnection.Create(builder =>
                builder.WithUrl($"{_fixture.ServerUrl}/signalr/testhub"));
            await connection.StartAsync(ct);

            try {
                // GetServiceInfo has [FromServices] IServiceProvider — client doesn't send it, DI injects it
                var result = await connection.InvokeCoreAsync<string>(
                    new ClientRequestMessage("ExtraMethods.GetServiceInfo"), ct);

                Assert.Equal("ServiceProviderInjected", result);
            } finally {
                await connection.StopAsync(ct);
                await connection.DisposeAsync();
            }
        }

        [Fact]
        public async Task ClientAttributes_FromHeaders_AreAccessible() {
            var ct = TestContext.Current.CancellationToken;
            // Connect with custom headers (# prefix → stored as attributes)
            var connection = HARRRConnection.Create(builder =>
                builder.WithUrl($"{_fixture.ServerUrl}/signalr/testhub", options => {
                    options.Headers["#AppVersion"] = "2.1.0";
                    options.Headers["#Platform"] = "TestRunner";
                }));
            await connection.StartAsync(ct);

            try {
                // Query the server for this client's attributes
                using var http = new HttpClient();
                var url = $"{_fixture.ServerUrl}/__test/get-client-attributes?connectionId={connection.ConnectionId}";
                var response = await http.PostAsync(url, null, ct);
                response.EnsureSuccessStatusCode();

                var json = await response.Content.ReadAsStringAsync(ct);
                var attrs = JsonSerializer.Deserialize<Dictionary<string, string>>(json);

                Assert.NotNull(attrs);
                Assert.Equal("2.1.0", attrs!["AppVersion"]);
                Assert.Equal("TestRunner", attrs["Platform"]);
            } finally {
                await connection.StopAsync(ct);
                await connection.DisposeAsync();
            }
        }
    }
}
