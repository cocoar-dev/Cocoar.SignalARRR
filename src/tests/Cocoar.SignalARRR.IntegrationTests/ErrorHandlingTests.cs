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
        public async Task InvokeWithWrongParameterCount_IsRejected() {
            // Extra parameters used to be silently ignored. Since F-6 the argument count is the
            // dispatch key — overloads are told apart by it — so a count no registered method
            // accepts is an error, not something to swallow.
            await Assert.ThrowsAsync<HubException>(async () => {
                await _connection.InvokeAsync<string>("GetName", "unexpected parameter", TestContext.Current.CancellationToken);
            });
        }

        [Fact]
        public async Task StructuredError_ArgumentException_ParsesCorrectly() {
            var ct = TestContext.Current.CancellationToken;
            var ex = await Assert.ThrowsAsync<HubException>(async () => {
                await _connection.InvokeCoreAsync<string>(
                    new Cocoar.SignalARRR.Common.ClientRequestMessage("ExtraMethods.ThrowArgumentException", new object[] { "testParam" }), ct);
            });

            var error = Cocoar.SignalARRR.Common.HARRRError.Parse(ex);
            Assert.Equal("System.ArgumentException", error.Type);
            Assert.Contains("Invalid value provided", error.Message);
        }

        [Fact]
        public async Task StructuredError_InvalidOperationException_ParsesCorrectly() {
            var ct = TestContext.Current.CancellationToken;
            var ex = await Assert.ThrowsAsync<HubException>(async () => {
                await _connection.InvokeCoreAsync<string>(
                    new Cocoar.SignalARRR.Common.ClientRequestMessage("ExtraMethods.ThrowInvalidOperation"), ct);
            });

            var error = Cocoar.SignalARRR.Common.HARRRError.Parse(ex);

            Assert.Equal("System.InvalidOperationException", error.Type);
            Assert.Equal("This operation is not allowed", error.Message);
        }
    }
}
