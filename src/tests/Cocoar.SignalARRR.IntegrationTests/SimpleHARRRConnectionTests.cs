using System;
using System.Threading.Tasks;
using Cocoar.SignalARRR.Client;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Cocoar.SignalARRR.IntegrationTests {
    [Collection("Simple")]
    public class SimpleHARRRConnectionTests : IAsyncLifetime {

        SignalARRRServerInstanceFixture fixture;
        HARRRConnection harrrConnection;


        public SimpleHARRRConnectionTests(SignalARRRServerInstanceFixture fixture) {
            this.fixture = fixture;

            harrrConnection = HARRRConnection.Create(builder => {
                builder.WithUrl($"{fixture.ServerUrl}/signalr/testhub");
            });
        }

        public async ValueTask InitializeAsync() {
            await harrrConnection.StartAsync();
        }

        public async ValueTask DisposeAsync() {
            await harrrConnection.StopAsync();
            await harrrConnection.DisposeAsync();
        }

        [Fact]
        public async Task GetString() {
            var name = await harrrConnection.InvokeAsync<string>("GetName", TestContext.Current.CancellationToken);

            Assert.Equal("MyName", name);
        }

        [Fact]
        public async Task GetStringAsync() {
            var name = await harrrConnection.InvokeAsync<string>("GetNameAsync", TestContext.Current.CancellationToken);

            Assert.Equal("MyNameAsync", name);
        }
    }
}
