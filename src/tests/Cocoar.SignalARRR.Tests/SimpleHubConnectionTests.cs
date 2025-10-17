using System;
using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Logging;
using Xunit;

namespace SignalARRR.Tests
{
    [Collection("Simple")]
    public class SimpleHubConnectionTests {

        SignalARRRServerInstanceFixture fixture;
        HubConnection hubConnection;


        public SimpleHubConnectionTests(SignalARRRServerInstanceFixture fixture) {
            this.fixture = fixture;


            var testServer = this.fixture.GetHost().GetTestServer();

            hubConnection = new HubConnectionBuilder()
                .WithUrl($"{testServer.BaseAddress}signalr/testhub", options => {
                    options.HttpMessageHandlerFactory = _ => testServer.CreateHandler();
                    options.Proxy = new WebProxy("localhost.:8888");
                }).Build();
            
            
        }

        [Fact]
        public async Task GetString() {

            await hubConnection.StartAsync();

            var name = await hubConnection.InvokeAsync<string>("GetName");

            Assert.Equal("MyName", name);
        }

        [Fact]
        public async Task GetStringAsync() {

            await hubConnection.StartAsync();

            var name = await hubConnection.InvokeAsync<string>("GetNameAsync");

            Assert.Equal("MyNameAsync", name);
        }
    }
}
