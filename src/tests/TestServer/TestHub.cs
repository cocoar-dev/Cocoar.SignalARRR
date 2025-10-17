using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;
using SignalARRR.Server;

namespace TestServer {
    
    public partial class TestHub : HARRR {


        private IObservable<int> GetNextInt { get; }


        public TestHub(IServiceProvider serviceProvider) : base(serviceProvider) {

            GetNextInt = Observable.Interval(TimeSpan.FromSeconds(1)).Select(t => (int)t).AsObservable();
           
        }

        public ChannelReader<int> Counter(
    int count,
    int delay,
    CancellationToken cancellationToken) {
            var channel = Channel.CreateUnbounded<int>();


            GetNextInt
                .Take(count)
                .Select(i => Observable.FromAsync(async () => await channel.Writer.WriteAsync(i, cancellationToken)))
                .Concat()
                .Do(i => Console.WriteLine(i))
                .Finally(() => channel.Writer.Complete())
                .Subscribe(cancellationToken);
            
            return channel.Reader;
        }
    }
}
