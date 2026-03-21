using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Cocoar.SignalARRR.Server;
using Cocoar.SignalARRR.Tests.SharedModels;

namespace IntegrationTestServer {

    public partial class TestHub : HARRR, ITestServerMethods {

        public TestHub(IServiceProvider serviceProvider) : base(serviceProvider) {
        }

        public string GetName() {
            return "SwiftTestName";
        }

        public Task<string> GetNameAsync() {
            return Task.FromResult("SwiftTestNameAsync");
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

        public string Echo(string message) {
            return message;
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
