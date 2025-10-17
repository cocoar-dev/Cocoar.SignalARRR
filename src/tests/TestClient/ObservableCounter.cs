using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using SignalARRR;
using SignalARRR.Client;
using TestShared;

namespace TestClient {
    public class ObservableCounter {

        private CancellationTokenSource cancellationTokenSource;
        public HARRRConnection Connection { get; }

        public ObservableCounter(HARRRConnection connection) {
            Connection = connection;
        }


        private IDisposable obs;
        public async Task StartAsync() {
            cancellationTokenSource = new CancellationTokenSource();

            try {

                var cl = Connection.GetTypedMethods<ITestHub>();
                var stream1 = cl.ObservableCounter(10, 500);

                var ch = cl.ChannelCounter(10, 5000, cancellationTokenSource.Token);
                //var stream = Connection.StreamAsync<string>(
                //"Test1.ObservableCounter", 10, 500, cancellationTokenSource.Token);

                //var ch = ToChannelReader(stream, cancellationTokenSource.Token);


                //await foreach (var count in stream) {
                //    Console.WriteLine($"{count}");
                //}

                obs = stream1.Subscribe(count => Console.WriteLine($"Observable: {count}"));

                while (await ch.WaitToReadAsync(cancellationTokenSource.Token)) {
                    while (ch.TryRead(out var item)) {
                        Console.WriteLine($"ChannelReader: {item}");
                    }
                }

                Console.WriteLine("Finished ObservableCounter");
            } catch (OperationCanceledException) {
                Console.WriteLine("Finished ObservableCounter");
            } 
        }

        //public ChannelReader<T> ToChannelReader<T>(IAsyncEnumerable<T> asyncEnumerable, CancellationToken token) {

        //    var output = Channel.CreateUnbounded<T>(new UnboundedChannelOptions { SingleWriter = true });
        //    var writer = output.Writer;
        //    Task.Run(async () =>
        //    {
        //        await foreach (var x1 in asyncEnumerable)
        //        {
        //            if (!writer.TryWrite(x1)) {
        //                await writer.WriteAsync(x1, token);
        //            }
        //        }
                
        //        writer.TryComplete();
        //    });
        //    return output.Reader;
        //}

        public void Stop() {
            obs?.Dispose();
            cancellationTokenSource.Cancel();
        }
    }
}
