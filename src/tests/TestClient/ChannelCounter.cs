using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SignalARRR;
using SignalARRR.Client;

namespace TestClient {
    public class ChannelCounter {

        private CancellationTokenSource cancellationTokenSource;
        public HARRRConnection Connection { get; }

        public ChannelCounter(HARRRConnection connection) {
            Connection = connection;
        }

       

        public async Task StartAsync() {
            cancellationTokenSource = new CancellationTokenSource();

            try {
                var stream = Connection.StreamAsync<string>(
                "Test1.ChannelCounter", 10, 500, cancellationTokenSource.Token);

                await foreach (var count in stream) {
                    Console.WriteLine($"{count}");
                }

                Console.WriteLine("Finished ChannelCounter");
            } catch (OperationCanceledException) {
                Console.WriteLine("Finished ChannelCounter");
            } 
        }

        public void Stop() {
            cancellationTokenSource.Cancel();
        }
    }
}
