using System;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR.Client;
using TestShared;
using Xunit;

namespace Cocoar.SignalARRR.Client.FullFramework.Tests {
    [Collection("FullFramework")]
    public class ServerToClientTests : IDisposable {
        private readonly ServerFixture _fixture;
        private readonly HARRRConnection _connection;

        public ServerToClientTests(ServerFixture fixture) {
            _fixture = fixture;
            _connection = HARRRConnection.Create(builder => {
                builder.WithUrl(_fixture.ServerUrl + "/signalr/testhub");
            });
            _connection.RegisterInterface<ITestClientMethods, TestClientMethodsImpl>(new TestClientMethodsImpl());
            _connection.StartAsync().GetAwaiter().GetResult();
            WaitForClientRegistration().GetAwaiter().GetResult();
        }

        public void Dispose() {
            _connection.StopAsync().GetAwaiter().GetResult();
            _connection.DisposeAsync().GetAwaiter().GetResult();
        }

        [Fact]
        public async Task ServerCallsClient_Nix_VoidMethodCompletes() {
            using (var http = new HttpClient()) {
                var url = _fixture.ServerUrl + "/__test/trigger-client-typed-call?connectionId=" + _connection.ConnectionId;
                var response = await http.PostAsync(url, null);
                response.EnsureSuccessStatusCode();

                var result = await response.Content.ReadAsStringAsync();
                Assert.Equal("\"Sent\"", result);
            }
        }

        [Fact]
        public async Task ServerCallsClient_GetById_ReturnsValue() {
            using (var http = new HttpClient()) {
                var url = _fixture.ServerUrl + "/__test/trigger-client-getbyid?connectionId=" + _connection.ConnectionId + "&id=test123";
                var response = await http.PostAsync(url, null);
                response.EnsureSuccessStatusCode();

                var result = await response.Content.ReadAsStringAsync();
                Assert.Contains("result-test123", result);
            }
        }

        [Fact]
        public async Task ServerCallsClient_GetContent_ReturnsList() {
            using (var http = new HttpClient()) {
                var url = _fixture.ServerUrl + "/__test/trigger-client-getcontent?connectionId=" + _connection.ConnectionId + "&count=3";
                var response = await http.PostAsync(url, null);
                response.EnsureSuccessStatusCode();

                var result = await response.Content.ReadAsStringAsync();
                Assert.Contains("item-0", result);
                Assert.Contains("item-2", result);
            }
        }

        private async Task WaitForClientRegistration() {
            using (var http = new HttpClient()) {
                for (int i = 0; i < 50; i++) {
                    var response = await http.GetAsync(
                        _fixture.ServerUrl + "/__test/client-exists?connectionId=" + _connection.ConnectionId);
                    if (response.IsSuccessStatusCode) {
                        var body = await response.Content.ReadAsStringAsync();
                        if (body.Contains("true")) return;
                    }
                    await Task.Delay(100);
                }
            }
            throw new TimeoutException("Client was not registered within 5 seconds");
        }
    }
}
