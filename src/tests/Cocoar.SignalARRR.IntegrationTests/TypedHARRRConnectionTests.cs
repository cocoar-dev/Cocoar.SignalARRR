using System;
using System.Threading.Tasks;
using Cocoar.SignalARRR.Client;
using Cocoar.SignalARRR.Tests.SharedModels;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Cocoar.SignalARRR.IntegrationTests {
    [Collection("Simple")]
    public class TypedHARRRConnectionTests : IAsyncLifetime {

        SignalARRRServerInstanceFixture fixture;
        HARRRConnection harrrConnection;


        public TypedHARRRConnectionTests(SignalARRRServerInstanceFixture fixture) {
            this.fixture = fixture;

            harrrConnection = HARRRConnection.Create(builder => {
                builder.WithUrl($"{fixture.ServerUrl}/signalr/testhub");
            });
        }

        public async ValueTask InitializeAsync() {
            // Start connection once for all tests in this class
            await harrrConnection.StartAsync();
        }

        public async ValueTask DisposeAsync() {
            // Stop connection after all tests in this class
            await harrrConnection.StopAsync();
            await harrrConnection.DisposeAsync();
        }

        private T GetTypeConnection<T>() where T : class {
            return harrrConnection.GetTypedMethods<T>();
        }

        [Fact]
        public void GetString() {
            var serverMethods = GetTypeConnection<ITestServerMethods>();
            var name = serverMethods.GetName();

            Assert.Equal("MyName", name);
        }

        [Fact]
        public async Task GetStringAsync() {
            var serverMethods = GetTypeConnection<ITestServerMethods>();
            var name = await serverMethods.GetNameAsync();

            Assert.Equal("MyNameAsync", name);
        }
    }
}
