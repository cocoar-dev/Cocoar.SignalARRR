using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SignalARRR;
using SignalARRR.Client;

namespace TestClient {
    public class AsyncEnumerableCounter {

        private CancellationTokenSource cancellationTokenSource;
        public HARRRConnection Connection { get; }

        public AsyncEnumerableCounter(HARRRConnection connection) {
            Connection = connection;
        }

       

        public async Task StartAsync() {
            cancellationTokenSource = new CancellationTokenSource();

            try {
                var stream = Connection.StreamAsync<string>(
                "Test1.AsyncEnumerableCounter", 10, 500, cancellationTokenSource.Token);

                await foreach (var count in stream) {
                    Console.WriteLine($"{count}");
                }

                Console.WriteLine("Finished AsyncEnumerableCounter");
            } catch (OperationCanceledException) {
                Console.WriteLine("Finished AsyncEnumerableCounter");
            } 
        }

        public void Stop() {
            cancellationTokenSource.Cancel();
        }
    }
}
