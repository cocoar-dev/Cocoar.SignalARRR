using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SignalARRR;
using SignalARRR.Client;

namespace TestClient {
    public class ObservableCounterTask {

        private CancellationTokenSource cancellationTokenSource;
        public HARRRConnection Connection { get; }

        public ObservableCounterTask(HARRRConnection connection) {
            Connection = connection;
        }

       

        public async Task StartAsync() {
            cancellationTokenSource = new CancellationTokenSource();

            try {
                var stream = Connection.StreamAsync<string>(
                "Test1.ObservableCounterTask", 10, 500, cancellationTokenSource.Token);

                await foreach (var count in stream) {
                    Console.WriteLine($"{count}");
                }

                Console.WriteLine("Finished ObservableCounterTask");
            } catch (OperationCanceledException) {
                Console.WriteLine("Finished ObservableCounterTask");
            } 
        }

        public void Stop() {
            cancellationTokenSource.Cancel();
        }
    }
}
