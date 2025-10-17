using System;
using System.Net;
using System.Threading.Tasks;
using Cocoar.SignalARRR.Client;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.TestHost;
using SignalARRR.Tests.SharedModels;
using Xunit;

namespace SignalARRR.Tests
{
    [Collection("Simple")]
    public class ErrorHandlingTests
    {
        private readonly SignalARRRServerInstanceFixture _fixture;
        private readonly HARRRConnection _connection;

        public ErrorHandlingTests(SignalARRRServerInstanceFixture fixture)
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
        public async Task InvokeNonExistentMethod_ThrowsException()
        {
            await _connection.StartAsync();

            await Assert.ThrowsAsync<HubException>(async () =>
            {
                await _connection.InvokeAsync<string>("NonExistentMethod");
            });
        }

        [Fact]
        public async Task InvokeWithWrongParameterCount_IgnoresExtraParameters()
        {
            await _connection.StartAsync();

            // SignalR doesn't throw for extra parameters, it just ignores them
            var result = await _connection.InvokeAsync<string>("GetName", "unexpected parameter");
            Assert.Equal("MyName", result);
        }
    }
}
