using System;
using System.Threading.Tasks;
using Cocoar.SignalARRR.Client;
using Cocoar.SignalARRR.Common;
using Microsoft.AspNetCore.SignalR.Client;
using Xunit;

namespace Cocoar.SignalARRR.IntegrationTests {

    /// <summary>
    /// Pins the property that a received argument value never appears in an error message (O-5).
    /// </summary>
    /// <remarks>
    /// A parameter that fails to deserialize might be a password or a token sent to the wrong
    /// method — the error names the types involved, and must keep doing so instead of echoing the
    /// value into the server log and the wire.
    /// </remarks>
    [Collection("Simple")]
    public class ErrorMessageHygieneTests : IAsyncLifetime {
        private readonly SignalARRRServerInstanceFixture _fixture;
        private HARRRConnection _connection = null!;

        public ErrorMessageHygieneTests(SignalARRRServerInstanceFixture fixture) {
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
        public async Task A_value_that_fails_to_deserialize_is_not_echoed_into_the_error() {
            var ct = TestContext.Current.CancellationToken;
            const string secret = "hunter2-super-secret-credential";

            // ExtraMethods.Add(int a, int b) — a string that is no number cannot be coerced.
            var ex = await Assert.ThrowsAnyAsync<Exception>(() => _connection.InvokeCoreAsync<int>(
                new ClientRequestMessage("ExtraMethods.Add", new object[] { secret, 2 }), ct));

            Assert.DoesNotContain(secret, ex.ToString());
        }
    }
}
