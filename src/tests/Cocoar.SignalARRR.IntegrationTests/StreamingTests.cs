using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Cocoar.SignalARRR.Client;
using Microsoft.AspNetCore.SignalR.Client;
using Xunit;

namespace Cocoar.SignalARRR.IntegrationTests {
    [Collection("Simple")]
    public class StreamingTests : IAsyncLifetime {
        private readonly SignalARRRServerInstanceFixture _fixture;
        private readonly HARRRConnection _connection;

        public StreamingTests(SignalARRRServerInstanceFixture fixture) {
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
        public async Task StreamChannel_ReceivesAllItems() {
            var items = new List<int>();
            // Use very small delay (10ms) for fast tests
            var stream = _connection.StreamAsync<int>("Counter", 5, 10, TestContext.Current.CancellationToken);

            await foreach (var item in stream) {
                items.Add(item);
            }

            Assert.Equal(5, items.Count);
            Assert.Equal(new[] { 0, 1, 2, 3, 4 }, items);
        }

        [Fact]
        public async Task StreamChannel_CancellationStopsStream() {
            var cts = new CancellationTokenSource();
            var items = new List<int>();
            var stream = _connection.StreamAsync<int>("Counter", 100, 10, cts.Token);

            try {
                var count = 0;
                await foreach (var item in stream) {
                    items.Add(item);
                    count++;
                    if (count == 3) {
                        cts.Cancel();
                    }
                }
            } catch (OperationCanceledException) {
                // Expected when cancellation is requested
            }

            // We requested 100 items but cancelled after receiving 3.
            // The exact count after cancellation is non-deterministic, but
            // we must have received at least 3 and far fewer than all 100.
            Assert.True(items.Count >= 3, $"Should have received at least 3 items but got {items.Count}");
            Assert.True(items.Count < 100, $"Cancellation should have stopped the stream but received all {items.Count} items");
        }
    }
}
