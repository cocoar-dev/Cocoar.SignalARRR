using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR.Client;
using Xunit;

namespace Cocoar.SignalARRR.Client.FullFramework.Tests {
    [Collection("FullFramework")]
    public class StreamingTests : IDisposable {
        private readonly ServerFixture _fixture;
        private readonly HARRRConnection _connection;

        public StreamingTests(ServerFixture fixture) {
            _fixture = fixture;
            _connection = HARRRConnection.Create(builder => {
                builder.WithUrl(_fixture.ServerUrl + "/signalr/testhub");
            });
            _connection.StartAsync().GetAwaiter().GetResult();
        }

        public void Dispose() {
            _connection.StopAsync().GetAwaiter().GetResult();
            _connection.DisposeAsync().GetAwaiter().GetResult();
        }

        [Fact]
        public async Task Stream_ReceivesAllItems() {
            var items = new List<int>();
            var stream = _connection.StreamAsyncCore<int>("Counter", new object[] { 5, 10 });

            await foreach (var item in stream) {
                items.Add(item);
            }

            Assert.Equal(5, items.Count);
            Assert.Equal(new[] { 0, 1, 2, 3, 4 }, items);
        }

        [Fact]
        public async Task Stream_CancellationStopsStream() {
            var cts = new CancellationTokenSource();
            var items = new List<int>();
            var stream = _connection.StreamAsyncCore<int>("Counter", new object[] { 100, 10 }, cts.Token);

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
                // Expected
            }

            Assert.True(items.Count >= 3, "Should have received at least 3 items but got " + items.Count);
            Assert.True(items.Count < 100, "Cancellation should have stopped the stream but received " + items.Count);
        }
    }
}
