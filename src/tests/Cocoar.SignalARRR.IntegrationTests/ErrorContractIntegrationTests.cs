using System;
using System.Threading.Tasks;
using Cocoar.SignalARRR.Client;
using Cocoar.SignalARRR.Common;
using Cocoar.SignalARRR.Common.Exceptions;
using Microsoft.AspNetCore.SignalR.Client;
using Xunit;

namespace Cocoar.SignalARRR.IntegrationTests {

    /// <summary>
    /// The error contract over the real wire (O-7): the .NET client throws
    /// <see cref="HARRRRemoteException"/> with the machine-readable code, a human-readable
    /// message, and the nested cause chain — instead of a bare HubException whose message was raw
    /// JSON nobody parsed.
    /// </summary>
    [Collection("Simple")]
    public class ErrorContractIntegrationTests : IAsyncLifetime {
        private readonly SignalARRRServerInstanceFixture _fixture;
        private HARRRConnection _connection = null!;

        public ErrorContractIntegrationTests(SignalARRRServerInstanceFixture fixture) {
            _fixture = fixture;
        }

        public async ValueTask InitializeAsync() {
            _connection = HARRRConnection.Create(builder =>
                builder.WithUrl($"{_fixture.ServerUrl}/signalr/testhub"));
            await _connection.StartAsync(TestContext.Current.CancellationToken);
        }

        public async ValueTask DisposeAsync() {
            await _connection.StopAsync();
            await _connection.DisposeAsync();
        }

        [Fact]
        public async Task An_unknown_method_reports_method_not_found() {
            var ct = TestContext.Current.CancellationToken;

            var ex = await Assert.ThrowsAsync<HARRRRemoteException>(() => _connection.InvokeCoreAsync<string>(
                new ClientRequestMessage("ExtraMethods.NoSuchMethod"), ct));

            Assert.Equal(HARRRErrorCodes.MethodNotFound, ex.Code);
        }

        [Fact]
        public async Task A_wrong_argument_count_reports_invalid_argument_count() {
            var ct = TestContext.Current.CancellationToken;

            var ex = await Assert.ThrowsAsync<HARRRRemoteException>(() => _connection.InvokeCoreAsync<string>(
                new ClientRequestMessage("ExtraMethods.PickOverload", new object[] { 1, 2, 3 }), ct));

            Assert.Equal(HARRRErrorCodes.InvalidArgumentCount, ex.Code);
        }

        [Fact]
        public async Task An_application_code_arrives_verbatim_with_a_readable_message() {
            var ct = TestContext.Current.CancellationToken;

            var ex = await Assert.ThrowsAsync<HARRRRemoteException>(() => _connection.InvokeCoreAsync<string>(
                new ClientRequestMessage("ExtraMethods.ThrowRoomFull"), ct));

            Assert.Equal("room_full", ex.Code);
            // The message is the human text, not the raw JSON envelope.
            Assert.Equal("The room is full.", ex.Message);
        }

        [Fact]
        public async Task A_method_exception_reports_internal_without_the_detail() {
            var ct = TestContext.Current.CancellationToken;

            var ex = await Assert.ThrowsAsync<HARRRRemoteException>(() => _connection.InvokeCoreAsync<string>(
                new ClientRequestMessage("ExtraMethods.ThrowInvalidOperation"), ct));

            // Across a real connection, not just in the wrapper: what the method threw stays on
            // the server. The code is still there to branch on, and the correlation id in the
            // message is what ties this call to the server log entry.
            Assert.Equal(HARRRErrorCodes.Internal, ex.Code);
            Assert.DoesNotContain("This operation is not allowed", ex.Message);
            Assert.DoesNotContain(nameof(InvalidOperationException), ex.Error.Type);
            Assert.Matches("Correlation id: [0-9a-f]{12}", ex.Message);
        }
    }
}
