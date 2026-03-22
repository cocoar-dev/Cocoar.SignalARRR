using System.Threading.Tasks;
using Cocoar.SignalARRR.Client;
using Cocoar.SignalARRR.Common;
using Microsoft.AspNetCore.SignalR.Client;
using Xunit;

namespace Cocoar.SignalARRR.IntegrationTests {
    [Collection("Simple")]
    public class MultiServerMethodsTests : IAsyncLifetime {
        private readonly SignalARRRServerInstanceFixture _fixture;
        private HARRRConnection _connection = null!;

        public MultiServerMethodsTests(SignalARRRServerInstanceFixture fixture) {
            _fixture = fixture;
        }

        public async ValueTask InitializeAsync() {
            _connection = HARRRConnection.Create(builder =>
                builder.WithUrl($"{_fixture.ServerUrl}/signalr/testhub"));
            await _connection.StartAsync(TestContext.Current.CancellationToken);
        }

        public async ValueTask DisposeAsync() {
            await _connection.StopAsync();
            await _connection.DisposeAsync();
        }

        [Fact]
        public async Task SecondServerMethodsClass_Greet_ReturnsGreeting() {
            var ct = TestContext.Current.CancellationToken;
            var result = await _connection.InvokeCoreAsync<string>(
                new ClientRequestMessage("ExtraMethods.Greet", new object[] { "World" }), ct);

            Assert.Equal("Hello, World!", result);
        }

        [Fact]
        public async Task SecondServerMethodsClass_Add_ReturnsSum() {
            var ct = TestContext.Current.CancellationToken;
            var result = await _connection.InvokeCoreAsync<int>(
                new ClientRequestMessage("ExtraMethods.Add", new object[] { 3, 4 }), ct);

            Assert.Equal(7, result);
        }

        [Fact]
        public async Task MessageNameAttribute_CustomEcho_WorksWithCustomName() {
            var ct = TestContext.Current.CancellationToken;
            // Method is defined as EchoWithCustomName but decorated with [MessageName("CustomEcho")]
            var result = await _connection.InvokeCoreAsync<string>(
                new ClientRequestMessage("ExtraMethods.CustomEcho", new object[] { "test-value" }), ct);

            Assert.Equal("test-value", result);
        }

        [Fact]
        public async Task OriginalHub_StillWorks_AfterAddingSecondClass() {
            var ct = TestContext.Current.CancellationToken;
            // Methods from the original TestHub should still work
            var result = await _connection.InvokeCoreAsync<string>(
                new ClientRequestMessage("GetNameAsync"), ct);

            Assert.Equal("MyNameAsync", result);
        }
    }
}
