using System;
using System.Net;
using System.Threading.Tasks;
using Cocoar.SignalARRR.Client;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Logging;
using SignalARRR.Tests.SharedModels;
using Xunit;

namespace Cocoar.SignalARRR.IntegrationTests
{
    [Collection("Simple")]
    public class TypedHARRRConnectionTests {

        SignalARRRServerInstanceFixture fixture;
        HARRRConnection harrrConnection;


        public TypedHARRRConnectionTests(SignalARRRServerInstanceFixture fixture) {
            this.fixture = fixture;


            var testServer = this.fixture.GetHost().GetTestServer();

            harrrConnection = HARRRConnection.Create(builder => {
                builder.WithUrl($"{testServer.BaseAddress}signalr/testhub", options => {
                    options.HttpMessageHandlerFactory = _ => testServer.CreateHandler();
                    options.Proxy = new WebProxy("localhost.:8888");
                });
            });
            
        }

        private async Task<T> GetTypeConnection<T>() where T : class {
            await harrrConnection.StartAsync();
            return harrrConnection.GetTypedMethods<T>();
        }

        [Fact]
        public async Task GetString() {


            var serverMethods = await GetTypeConnection<ITestServerMethods>();
            var name = serverMethods.GetName();

            Assert.Equal("MyName", name);
        }

        [Fact]
        public async Task GetStringAsync() {

            var serverMethods = await GetTypeConnection<ITestServerMethods>();
            var name = await serverMethods.GetNameAsync();

            Assert.Equal("MyNameAsync", name);
        }
    }
}
