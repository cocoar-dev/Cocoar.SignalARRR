using System;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Cocoar.SignalARRR.Server;
using SignalARRR.Tests.SharedModels;

namespace Cocoar.SignalARRR.IntegrationTests {
    
    public partial class TestHub : HARRR, ITestServerMethods {

        private IObservable<int> GetNextInt { get; }
        
        public TestHub(IServiceProvider serviceProvider) : base(serviceProvider) {
            GetNextInt = Observable.Interval(TimeSpan.FromSeconds(1)).Select(t => (int)t).AsObservable();
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

        public ChannelReader<int> Counter(int count, int delay, CancellationToken cancellationToken) {
            var channel = Channel.CreateUnbounded<int>();

            GetNextInt
                .Take(count)
                .Select(i => Observable.FromAsync(async () => await channel.Writer.WriteAsync(i, cancellationToken)))
                .Concat()
                .Finally(() => channel.Writer.Complete())
                .Subscribe(cancellationToken);

            return channel.Reader;
        }
    }
}
