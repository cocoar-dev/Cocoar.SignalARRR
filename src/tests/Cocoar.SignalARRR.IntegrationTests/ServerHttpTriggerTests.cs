using System.Net.Http;
using System.Threading.Tasks;
using Cocoar.SignalARRR.Client;
using Microsoft.AspNetCore.SignalR.Client;
using TestShared;
using Xunit;

namespace Cocoar.SignalARRR.IntegrationTests
{
    [Collection("Simple")]
    public class ServerHttpTriggerTests
    {
        private readonly SignalARRRServerInstanceFixture _fixture;

        public ServerHttpTriggerTests(SignalARRRServerInstanceFixture fixture)
        {
            _fixture = fixture;
        }

        [Fact]
        public async Task HttpTrigger_ServerCallsClient_ReturnsResult()
        {
            // Arrange: connect client and register interface implementation
            var connection = HARRRConnection.Create(builder => builder.WithUrl($"{_fixture.ServerUrl}/signalr/testhub"));
            connection.RegisterInterface<ITestClientMethods, TestClientMethodsImpl>(new TestClientMethodsImpl());
            await connection.StartAsync();

            try
            {
                var connectionId = connection.ConnectionId;
                var method = "TestShared.ITestClientMethods|GetById"; // interface-qualified method name
                var arg = "abc";

                using var http = new HttpClient();
                var url = $"{_fixture.ServerUrl}/__test/trigger-client-call?connectionId={connectionId}&method={System.Uri.EscapeDataString(method)}&arg={System.Uri.EscapeDataString(arg)}";
                var response = await http.PostAsync(url, content: null);
                response.EnsureSuccessStatusCode();

                var result = await response.Content.ReadAsStringAsync();
                // Fire-and-forget path returns a simple "Sent" payload
                Assert.Equal("\"Sent\"", result);
            }
            finally
            {
                await connection.StopAsync();
                await connection.DisposeAsync();
            }
        }

        [Fact]
    public async Task HttpTrigger_TypedServerCallsClient_UsesTypedProxy()
        {
            // Arrange: connect client and register interface implementation
            var connection = HARRRConnection.Create(builder => builder.WithUrl($"{_fixture.ServerUrl}/signalr/testhub"));
            connection.RegisterInterface<ITestClientMethods, TestClientMethodsImpl>(new TestClientMethodsImpl());
            await connection.StartAsync();

            try
            {
                var connectionId = connection.ConnectionId;
                using var http = new HttpClient();
                var url = $"{_fixture.ServerUrl}/__test/trigger-client-typed-call?connectionId={connectionId}";
                var response = await http.PostAsync(url, content: null);
                response.EnsureSuccessStatusCode();

                var result = await response.Content.ReadAsStringAsync();
                // Returns "Sent" for void typed call
                Assert.Equal("\"Sent\"", result);
            }
            finally
            {
                await connection.StopAsync();
                await connection.DisposeAsync();
            }
        }
    }
}
