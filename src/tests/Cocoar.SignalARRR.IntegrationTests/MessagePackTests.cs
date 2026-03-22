using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Cocoar.SignalARRR.Client;
using Cocoar.SignalARRR.Common;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using TestShared;
using Xunit;

namespace Cocoar.SignalARRR.IntegrationTests {
    [Collection("Simple")]
    public class MessagePackTests {
        private readonly SignalARRRServerInstanceFixture _fixture;

        public MessagePackTests(SignalARRRServerInstanceFixture fixture) {
            _fixture = fixture;
        }

        private HARRRConnection CreateMessagePackConnection() {
            return HARRRConnection.Create(builder => {
                builder.WithUrl($"{_fixture.ServerUrl}/signalr/testhub");
                builder.AddMessagePackProtocol();
            });
        }

        [Fact]
        public async Task MessagePack_InvokeReturnsString() {
            var ct = TestContext.Current.CancellationToken;
            var connection = CreateMessagePackConnection();
            await connection.StartAsync(ct);

            try {
                var result = await connection.InvokeCoreAsync<string>(
                    new ClientRequestMessage("GetNameAsync"), ct);
                Assert.Equal("MyNameAsync", result);
            } finally {
                await connection.StopAsync(ct);
                await connection.DisposeAsync();
            }
        }

        [Fact]
        public async Task MessagePack_InvokeReturnsGuid() {
            var ct = TestContext.Current.CancellationToken;
            var connection = CreateMessagePackConnection();
            await connection.StartAsync(ct);

            try {
                var result = await connection.InvokeCoreAsync<Guid>(
                    new ClientRequestMessage("GetGuidAsync"), ct);
                Assert.NotEqual(Guid.Empty, result);
            } finally {
                await connection.StopAsync(ct);
                await connection.DisposeAsync();
            }
        }

        [Fact]
        public async Task MessagePack_SendVoidMethod() {
            var ct = TestContext.Current.CancellationToken;
            var connection = CreateMessagePackConnection();
            await connection.StartAsync(ct);

            try {
                await connection.SendCoreAsync(
                    new ClientRequestMessage("NothingAsync"), ct);
            } finally {
                await connection.StopAsync(ct);
                await connection.DisposeAsync();
            }
        }

        [Fact]
        public async Task MessagePack_MultipleParameterTypes() {
            var ct = TestContext.Current.CancellationToken;
            var connection = CreateMessagePackConnection();
            await connection.StartAsync(ct);

            try {
                var result = await connection.InvokeCoreAsync<string>(
                    new ClientRequestMessage("ExtraMethods.Combine", new object[] { "test", 42, true }), ct);
                Assert.Equal("test-42-True", result);
            } finally {
                await connection.StopAsync(ct);
                await connection.DisposeAsync();
            }
        }

        [Fact]
        public async Task MessagePack_Echo() {
            var ct = TestContext.Current.CancellationToken;
            var connection = CreateMessagePackConnection();
            await connection.StartAsync(ct);

            try {
                var result = await connection.InvokeCoreAsync<string>(
                    new ClientRequestMessage("Echo", new object[] { "hello-msgpack" }), ct);
                Assert.Equal("hello-msgpack", result);
            } finally {
                await connection.StopAsync(ct);
                await connection.DisposeAsync();
            }
        }
    }
}
