using System;
using System.Threading.Tasks;
using Cocoar.SignalARRR.Tests.SharedModels;
using Microsoft.AspNetCore.SignalR.Client;
using Xunit;

namespace Cocoar.SignalARRR.Client.FullFramework.Tests {
    [Collection("FullFramework")]
    public class BasicTests : IDisposable {
        private readonly ServerFixture _fixture;
        private readonly HARRRConnection _connection;

        public BasicTests(ServerFixture fixture) {
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
        public async Task InvokeAsync_ReturnsString() {
            var name = await _connection.InvokeAsync<string>("GetName");
            Assert.Equal("MyName", name);
        }

        [Fact]
        public async Task InvokeAsync_ReturnsStringAsync() {
            var name = await _connection.InvokeAsync<string>("GetNameAsync");
            Assert.Equal("MyNameAsync", name);
        }

        [Fact]
        public async Task InvokeAsync_ReturnsGuid() {
            var result = await _connection.InvokeAsync<Guid>("GetGuid");
            Assert.NotEqual(Guid.Empty, result);
        }

        [Fact]
        public async Task SendAsync_VoidMethod() {
            await _connection.SendAsync("Nothing");
            Assert.True(true);
        }
    }
}
