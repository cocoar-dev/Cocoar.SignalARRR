using System;
using System.Threading.Tasks;
using Cocoar.SignalARRR.Client;
using Cocoar.SignalARRR.Common;
using Microsoft.AspNetCore.SignalR.Client;
using Xunit;

namespace Cocoar.SignalARRR.IntegrationTests {

    /// <summary>
    /// Overload and default-value dispatch over the real wire (F-6).
    /// </summary>
    /// <remarks>
    /// Uses the raw name-based invoke — a <see cref="ClientRequestMessage"/> with an argument
    /// array — which is byte-for-byte what a TypeScript or Swift client sends. The message carries
    /// no parameter types, so the argument count is the only thing telling the overloads apart.
    /// </remarks>
    [Collection("Simple")]
    public class OverloadDispatchTests : IAsyncLifetime {
        private readonly SignalARRRServerInstanceFixture _fixture;
        private HARRRConnection _connection = null!;

        public OverloadDispatchTests(SignalARRRServerInstanceFixture fixture) {
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
        public async Task An_overload_is_selected_by_the_argument_count_of_the_message() {
            var ct = TestContext.Current.CancellationToken;

            var one = await _connection.InvokeCoreAsync<string>(
                new ClientRequestMessage("ExtraMethods.PickOverload", new object[] { 1 }), ct);
            var two = await _connection.InvokeCoreAsync<string>(
                new ClientRequestMessage("ExtraMethods.PickOverload", new object[] { 1, 2 }), ct);

            Assert.Equal("one", one);
            Assert.Equal("two", two);
        }

        [Fact]
        public async Task An_omitted_trailing_argument_is_filled_from_the_parameter_default() {
            var ct = TestContext.Current.CancellationToken;

            // This is what a TypeScript caller omitting an optional argument sends. Before F-6 the
            // server threw "not enough arguments" — defaults in the contract were dead metadata for
            // every non-.NET client.
            var defaulted = await _connection.InvokeCoreAsync<string>(
                new ClientRequestMessage("ExtraMethods.PageInfo", new object[] { 3 }), ct);
            var explicitSize = await _connection.InvokeCoreAsync<string>(
                new ClientRequestMessage("ExtraMethods.PageInfo", new object[] { 3, 10 }), ct);

            Assert.Equal("3:25", defaulted);
            Assert.Equal("3:10", explicitSize);
        }

        [Fact]
        public async Task A_count_no_method_accepts_is_rejected() {
            var ct = TestContext.Current.CancellationToken;

            await Assert.ThrowsAnyAsync<Exception>(() => _connection.InvokeCoreAsync<string>(
                new ClientRequestMessage("ExtraMethods.PickOverload", new object[] { 1, 2, 3 }), ct));
        }
    }
}
