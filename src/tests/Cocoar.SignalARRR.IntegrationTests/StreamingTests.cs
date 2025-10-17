using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Cocoar.SignalARRR.Client;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.TestHost;
using Xunit;

namespace Cocoar.SignalARRR.IntegrationTests
{
    [Collection("Simple")]
    public class StreamingTests
    {
        private readonly SignalARRRServerInstanceFixture _fixture;
        private readonly HARRRConnection _connection;

        public StreamingTests(SignalARRRServerInstanceFixture fixture)
        {
            _fixture = fixture;

            var testServer = _fixture.GetHost().GetTestServer();

            _connection = HARRRConnection.Create(builder =>
            {
                builder.WithUrl($"{testServer.BaseAddress}signalr/testhub", options =>
                {
                    options.HttpMessageHandlerFactory = _ => testServer.CreateHandler();
                    options.Proxy = new WebProxy("localhost:8888");
                });
            });
        }

        [Fact]
        public async Task StreamChannel_ReceivesAllItems()
        {
            await _connection.StartAsync();

            var items = new List<int>();
            var stream = _connection.StreamAsync<int>("Counter", 5, 100, CancellationToken.None);

            await foreach (var item in stream)
            {
                items.Add(item);
            }

            Assert.Equal(5, items.Count);
            Assert.Equal(new[] { 0, 1, 2, 3, 4 }, items);
        }

        [Fact]
        public async Task StreamChannel_CancellationStopsStream()
        {
            await _connection.StartAsync();

            var cts = new CancellationTokenSource();
            var items = new List<int>();
            var stream = _connection.StreamAsync<int>("Counter", 100, 100, cts.Token);

            try
            {
                var count = 0;
                await foreach (var item in stream)
                {
                    items.Add(item);
                    count++;
                    if (count == 3)
                    {
                        cts.Cancel();
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Expected when cancellation is requested
            }

            Assert.True(items.Count >= 3 && items.Count <= 4); // May receive one more after cancellation
        }
    }
}
