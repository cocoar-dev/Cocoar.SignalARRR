using System;
using System.Threading.Tasks;
using Cocoar.SignalARRR.Client;
using Cocoar.SignalARRR.Common.Exceptions;
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
            await Assert.ThrowsAsync<HARRRRemoteException>(async () => {
                await _connection.InvokeAsync<string>("NonExistentMethod", TestContext.Current.CancellationToken);
            });
        }

        [Fact]
        public async Task InvokeWithWrongParameterCount_IsRejected() {
            // Extra parameters used to be silently ignored. Since F-6 the argument count is the
            // dispatch key — overloads are told apart by it — so a count no registered method
            // accepts is an error, not something to swallow.
            await Assert.ThrowsAsync<HARRRRemoteException>(async () => {
                await _connection.InvokeAsync<string>("GetName", "unexpected parameter", TestContext.Current.CancellationToken);
            });
        }

        [Fact]
        public async Task StructuredError_ArgumentException_ParsesCorrectly() {
            var ct = TestContext.Current.CancellationToken;
            var ex = await Assert.ThrowsAsync<HARRRRemoteException>(async () => {
                await _connection.InvokeCoreAsync<string>(
                    new Cocoar.SignalARRR.Common.ClientRequestMessage("ExtraMethods.ThrowArgumentException", new object[] { "testParam" }), ct);
            });

            // The structured error is a typed property now; nobody has to parse message strings.
            var error = ex.Error;
            Assert.Equal("System.ArgumentException", error.Type);
            Assert.Contains("Invalid value provided", error.Message);
        }

        [Fact]
        public async Task StructuredError_UnexpectedException_WithholdsTheDetail() {
            var ct = TestContext.Current.CancellationToken;
            var ex = await Assert.ThrowsAsync<HARRRRemoteException>(async () => {
                await _connection.InvokeCoreAsync<string>(
                    new Cocoar.SignalARRR.Common.ClientRequestMessage("ExtraMethods.ThrowInvalidOperation"), ct);
            });

            var error = ex.Error;

            // Contrast with the ArgumentException case above, which still arrives verbatim: that
            // one names a pipeline stage the library controls, this one is whatever the method
            // threw and could say anything about the server's insides.
            Assert.NotEqual("System.InvalidOperationException", error.Type);
            Assert.DoesNotContain("This operation is not allowed", error.Message);
        }
    }
}
