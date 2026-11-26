using System.Threading.Tasks;
using Cocoar.SignalARRR.Client.ExtensionMethods;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Cocoar.SignalARRR.IntegrationTests {
    /// <summary>
    /// SignalARRR reaches the service provider of a <see cref="HubConnection"/> through SignalR's
    /// private field <c>_serviceProvider</c> (with <c>UnsafeAccessor</c>, so it works under NativeAOT).
    /// The field name is not a contract: this test runs for every target framework, so a SignalR
    /// version that renames it fails here instead of in <c>HARRRConnection.Create</c>.
    /// </summary>
    public class HubConnectionServiceProviderTests {

        [Fact]
        public async Task GetServiceProvider_ReturnsTheProviderTheConnectionWasBuiltWith() {
            await using var hubConnection = new HubConnectionBuilder()
                .WithUrl("http://127.0.0.1:1/hub")
                .Build();

            var serviceProvider = hubConnection.GetServiceProvider();

            Assert.NotNull(serviceProvider);
            Assert.Same(hubConnection, serviceProvider.GetRequiredService<HubConnection>());
        }
    }
}
