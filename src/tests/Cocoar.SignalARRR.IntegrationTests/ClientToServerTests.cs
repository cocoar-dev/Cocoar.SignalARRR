using System;
using System.Threading;
using System.Threading.Tasks;
using Cocoar.SignalARRR.Client;
using Cocoar.SignalARRR.Tests.SharedModels;
using Microsoft.AspNetCore.SignalR.Client;
using Xunit;

namespace Cocoar.SignalARRR.IntegrationTests {
    [Collection("Simple")]
    public class ClientToServerTests : IAsyncLifetime {
        private readonly SignalARRRServerInstanceFixture _fixture;
        private readonly HARRRConnection _connection;

        public ClientToServerTests(SignalARRRServerInstanceFixture fixture) {
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
        public async Task SendAsync_VoidMethod_CompletesSynccessfully() {
            // Nothing() is a void method on the server
            await _connection.SendAsync("Nothing", TestContext.Current.CancellationToken);

            // If we get here without exception, it worked
            Assert.True(true);
        }

        [Fact]
        public async Task InvokeAsync_ReturnsGuid() {
            var result = await _connection.InvokeAsync<Guid>("GetGuid", TestContext.Current.CancellationToken);

            Assert.NotEqual(Guid.Empty, result);
        }

        [Fact]
        public async Task InvokeAsync_ReturnsGuidAsync() {
            var result = await _connection.InvokeAsync<Guid>("GetGuidAsync", TestContext.Current.CancellationToken);

            Assert.NotEqual(Guid.Empty, result);
        }

        [Fact]
        public void TypedClient_InvokesMethod() {
            var typedClient = _connection.GetTypedMethods<ITestServerMethods>();
            var result = typedClient.GetName();

            Assert.Equal("MyName", result);
        }

        [Fact]
        public async Task TypedClient_InvokesAsyncMethod() {
            var typedClient = _connection.GetTypedMethods<ITestServerMethods>();
            var result = await typedClient.GetNameAsync();

            Assert.Equal("MyNameAsync", result);
        }

        [Fact]
        public void TypedClient_InvokesGuidMethod() {
            var typedClient = _connection.GetTypedMethods<ITestServerMethods>();
            var result = typedClient.GetGuid();

            Assert.NotEqual(Guid.Empty, result);
        }

        [Fact]
        public async Task TypedClient_InvokesGuidAsyncMethod() {
            var typedClient = _connection.GetTypedMethods<ITestServerMethods>();
            var result = await typedClient.GetGuidAsync();

            Assert.NotEqual(Guid.Empty, result);
        }

        [Fact]
        public void TypedClient_InvokesVoidMethod() {
            var typedClient = _connection.GetTypedMethods<ITestServerMethods>();
            typedClient.Nothing();

            // If we get here without exception, it worked
            Assert.True(true);
        }

        [Fact]
        public async Task TypedClient_InvokesVoidAsyncMethod() {
            var typedClient = _connection.GetTypedMethods<ITestServerMethods>();
            await typedClient.NothingAsync();

            // If we get here without exception, it worked
            Assert.True(true);
        }
    }
}
