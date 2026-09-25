using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Cocoar.SignalARRR.Server;
using Cocoar.SignalARRR.Tests.SharedModels;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;

namespace IntegrationTestServer {

    public partial class TestHub : HARRR, ITestServerMethods {

        public TestHub(IServiceProvider serviceProvider) : base(serviceProvider) {
        }

        public string GetName() {
            return "MyName";
        }

        public Task<string> GetNameAsync() {
            return Task.FromResult("MyNameAsync");
        }

        public Guid GetGuid() {
            return Guid.NewGuid();
        }

        public Task<Guid> GetGuidAsync() {
            return Task.FromResult(Guid.NewGuid());
        }

        public void Nothing() {
        }

        public Task NothingAsync() {
            return Task.CompletedTask;
        }

        /// <summary>
        /// Regression guard for the fire-and-forget <c>SendMessage</c> bug. Because this method
        /// returns <see cref="Task"/> (no result), the client invokes it via <c>send</c>, which the
        /// server routes through <c>HARRR.SendMessage</c>. The body reads the hub-injected
        /// <see cref="HARRR.Context"/> and registers the caller in a ClientManager group that
        /// <c>WithGroup(...)</c> broadcasts target. The previous <c>Task.Run</c> fire-and-forget ran
        /// only after SignalR had disposed the Hub, so the <c>Context</c> access threw and the
        /// group-join silently never happened — making subsequent group broadcasts miss this client.
        /// </summary>
        public async Task SubscribeViaSend(string group) {
            var connectionId = Context.ConnectionId;
            var clientManager = ServiceProvider.GetRequiredService<ClientManager>();
            await clientManager.AddToGroupAsync(connectionId, group);
        }

        public string Echo(string message) {
            return message;
        }

        public string GetConnectionId() {
            return Context.ConnectionId;
        }

        /// <summary>
        /// Where the transport request carried the connection token: <c>"header=&lt;value&gt;;query=&lt;value&gt;"</c>,
        /// with <c>-</c> for absent. This server does not run the query-to-header middleware, so it
        /// sees exactly what the client sent.
        /// </summary>
        public string TransportCredential() {
            var request = Context.GetHttpContext()?.Request;
            string Show(string? value) => string.IsNullOrEmpty(value) ? "-" : value;
            return $"header={Show(request?.Headers.Authorization)};query={Show(request?.Query["access_token"])}";
        }

        public ChannelReader<int> Counter(int count, int delay, CancellationToken cancellationToken) {
            var channel = Channel.CreateUnbounded<int>();

            _ = Task.Run(async () => {
                for (var i = 0; i < count; i++) {
                    if (cancellationToken.IsCancellationRequested) break;
                    await channel.Writer.WriteAsync(i, cancellationToken);
                    if (delay > 0) await Task.Delay(delay, cancellationToken);
                }
                channel.Writer.Complete();
            }, cancellationToken);

            return channel.Reader;
        }
    }
}
