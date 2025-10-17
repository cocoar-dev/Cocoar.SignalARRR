using System;
using System.Net;
using System.Threading.Tasks;
using Cocoar.SignalARRR.Client;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Cocoar.SignalARRR.IntegrationTests
{
    [Collection("Simple")]
    public class SimpleHARRRConnectionTests {

        SignalARRRServerInstanceFixture fixture;
        HARRRConnection harrrConnection;


        public SimpleHARRRConnectionTests(SignalARRRServerInstanceFixture fixture) {
            this.fixture = fixture;


            var testServer = this.fixture.GetHost().GetTestServer();

            harrrConnection = HARRRConnection.Create(builder => {
                builder.WithUrl($"{testServer.BaseAddress}signalr/testhub", options => {
                    options.HttpMessageHandlerFactory = _ => testServer.CreateHandler();
                    options.Proxy = new WebProxy("localhost.:8888");
                });
            });
            
        }

        [Fact]
        public async Task GetString() {

            await harrrConnection.StartAsync();

            var name = await harrrConnection.InvokeAsync<string>("GetName");

            Assert.Equal("MyName", name);
        }

        [Fact]
        public async Task GetStringAsync() {

            await harrrConnection.StartAsync();

            var name = await harrrConnection.InvokeAsync<string>("GetNameAsync");

            Assert.Equal("MyNameAsync", name);
        }
    }
}
