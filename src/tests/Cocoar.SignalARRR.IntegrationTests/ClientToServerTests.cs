using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Cocoar.SignalARRR.Client;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.TestHost;
using SignalARRR.Tests.SharedModels;
using Xunit;

namespace Cocoar.SignalARRR.IntegrationTests
{
    [Collection("Simple")]
    public class ClientToServerTests
    {
        private readonly SignalARRRServerInstanceFixture _fixture;
        private readonly HARRRConnection _connection;

        public ClientToServerTests(SignalARRRServerInstanceFixture fixture)
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
        public async Task SendAsync_VoidMethod_CompletesSynccessfully()
        {
            await _connection.StartAsync();

            // Nothing() is a void method on the server
            await _connection.SendAsync("Nothing");

            // If we get here without exception, it worked
            Assert.True(true);
        }

        [Fact]
        public async Task InvokeAsync_ReturnsGuid()
        {
            await _connection.StartAsync();

            var result = await _connection.InvokeAsync<Guid>("GetGuid");

            Assert.NotEqual(Guid.Empty, result);
        }

        [Fact]
        public async Task InvokeAsync_ReturnsGuidAsync()
        {
            await _connection.StartAsync();

            var result = await _connection.InvokeAsync<Guid>("GetGuidAsync");

            Assert.NotEqual(Guid.Empty, result);
        }

        [Fact]
        public async Task TypedClient_InvokesMethod()
        {
            await _connection.StartAsync();

            var typedClient = _connection.GetTypedMethods<ITestServerMethods>();
            var result = typedClient.GetName();

            Assert.Equal("MyName", result);
        }

        [Fact]
        public async Task TypedClient_InvokesAsyncMethod()
        {
            await _connection.StartAsync();

            var typedClient = _connection.GetTypedMethods<ITestServerMethods>();
            var result = await typedClient.GetNameAsync();

            Assert.Equal("MyNameAsync", result);
        }

        [Fact]
        public async Task TypedClient_InvokesGuidMethod()
        {
            await _connection.StartAsync();

            var typedClient = _connection.GetTypedMethods<ITestServerMethods>();
            var result = typedClient.GetGuid();

            Assert.NotEqual(Guid.Empty, result);
        }

        [Fact]
        public async Task TypedClient_InvokesGuidAsyncMethod()
        {
            await _connection.StartAsync();

            var typedClient = _connection.GetTypedMethods<ITestServerMethods>();
            var result = await typedClient.GetGuidAsync();

            Assert.NotEqual(Guid.Empty, result);
        }

        [Fact]
        public async Task TypedClient_InvokesVoidMethod()
        {
            await _connection.StartAsync();

            var typedClient = _connection.GetTypedMethods<ITestServerMethods>();
            typedClient.Nothing();

            // If we get here without exception, it worked
            Assert.True(true);
        }

        [Fact]
        public async Task TypedClient_InvokesVoidAsyncMethod()
        {
            await _connection.StartAsync();

            var typedClient = _connection.GetTypedMethods<ITestServerMethods>();
            await typedClient.NothingAsync();

            // If we get here without exception, it worked
            Assert.True(true);
        }
    }
}
