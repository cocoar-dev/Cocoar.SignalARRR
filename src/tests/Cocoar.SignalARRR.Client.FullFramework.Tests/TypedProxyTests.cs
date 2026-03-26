using System;
using System.Threading.Tasks;
using Cocoar.SignalARRR.Tests.SharedModels;
using Microsoft.AspNetCore.SignalR.Client;
using Xunit;

namespace Cocoar.SignalARRR.Client.FullFramework.Tests {
    [Collection("FullFramework")]
    public class TypedProxyTests : IDisposable {
        private readonly ServerFixture _fixture;
        private readonly HARRRConnection _connection;

        public TypedProxyTests(ServerFixture fixture) {
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
        public void TypedProxy_SyncInvoke_ReturnsString() {
            var hub = _connection.GetTypedMethods<ITestServerMethods>();
            var result = hub.GetName();
            Assert.Equal("MyName", result);
        }

        [Fact]
        public async Task TypedProxy_AsyncInvoke_ReturnsString() {
            var hub = _connection.GetTypedMethods<ITestServerMethods>();
            var result = await hub.GetNameAsync();
            Assert.Equal("MyNameAsync", result);
        }

        [Fact]
        public void TypedProxy_SyncInvoke_ReturnsGuid() {
            var hub = _connection.GetTypedMethods<ITestServerMethods>();
            var result = hub.GetGuid();
            Assert.NotEqual(Guid.Empty, result);
        }

        [Fact]
        public async Task TypedProxy_AsyncInvoke_ReturnsGuid() {
            var hub = _connection.GetTypedMethods<ITestServerMethods>();
            var result = await hub.GetGuidAsync();
            Assert.NotEqual(Guid.Empty, result);
        }

        [Fact]
        public void TypedProxy_VoidMethod() {
            var hub = _connection.GetTypedMethods<ITestServerMethods>();
            hub.Nothing();
            Assert.True(true);
        }

        [Fact]
        public async Task TypedProxy_AsyncVoidMethod() {
            var hub = _connection.GetTypedMethods<ITestServerMethods>();
            await hub.NothingAsync();
            Assert.True(true);
        }
    }
}
