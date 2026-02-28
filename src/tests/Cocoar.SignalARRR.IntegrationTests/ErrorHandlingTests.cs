using System;
using System.Threading.Tasks;
using Cocoar.SignalARRR.Client;
using Microsoft.AspNetCore.SignalR;
using Cocoar.SignalARRR.Tests.SharedModels;
using Microsoft.AspNetCore.SignalR.Client;
using Xunit;

namespace Cocoar.SignalARRR.IntegrationTests {
    [Collection("Simple")]
    public class ErrorHandlingTests : IAsyncLifetime {
        private readonly SignalARRRServerInstanceFixture _fixture;
        private readonly HARRRConnection _connection;

        public ErrorHandlingTests(SignalARRRServerInstanceFixture fixture) {
            _fixture = fixture;

            _connection = HARRRConnection.Create(builder => {
                builder.WithUrl($"{fixture.ServerUrl}/signalr/testhub");
            });
        }

        public async ValueTask InitializeAsync() {
            await _connection.StartAsync();
        }

        public async ValueTask DisposeAsync() {
            await _connection.StopAsync();
            await _connection.DisposeAsync();
        }

        [Fact]
        public async Task InvokeNonExistentMethod_ThrowsException() {
            await Assert.ThrowsAsync<HubException>(async () => {
                await _connection.InvokeAsync<string>("NonExistentMethod", TestContext.Current.CancellationToken);
            });
        }

        [Fact]
        public async Task InvokeWithWrongParameterCount_IgnoresExtraParameters() {
            // SignalR doesn't throw for extra parameters, it just ignores them
            var result = await _connection.InvokeAsync<string>("GetName", "unexpected parameter", TestContext.Current.CancellationToken);
            Assert.Equal("MyName", result);
        }
    }
}
