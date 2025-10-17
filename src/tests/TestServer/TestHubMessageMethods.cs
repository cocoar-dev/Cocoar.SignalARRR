using System;
using System.Collections.Generic;
using System.Reactive.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using SignalARRR.Attributes;
using SignalARRR.Server;
using TestShared;

namespace TestServer {
    [MessageName("Test1")]
    public class TestHubMessageMethods : ServerMethods<TestHub>, ITestHub {

        private readonly ConsoleWriter2 _consoleWriter2;

        private IObservable<int> GetNextInt { get; }

        public TestHubMessageMethods(ConsoleWriter2 consoleWriter2) {
            _consoleWriter2 = consoleWriter2;
            GetNextInt = Observable.Interval(TimeSpan.FromSeconds(1)).Select(t => (int)t).AsObservable();
        }

        public void WriteLine(string line) {
            Console.WriteLine(line);
        }

        [MessageName("GibMirDatum")]
        //[Authorize("TestPolicy1")]
        public string GetDate([FromServices]ConsoleWriter writer) {
            writer.WriteInfo("From ServerMethod");
            _consoleWriter2.WriteInfo("From ServerMethod");
            Clients.All.SendAsync("test", "Das ist ein Test");

            
            return $"{DateTime.Now} -- From ServerMethod {Context.ConnectionId}";
        }

        public string GetDate2(DateTime date) {
            _consoleWriter2.WriteInfo("From ServerMethod");
            Clients.All.SendAsync("test", "Das ist ein Test");

            return $"{DateTime.Now} -- From ServerMethod {Context.ConnectionId} - Sent: {date}";
        }

        public void Ping() {
            Console.WriteLine($"Get Ping from {ClientContext.RemoteIp}");
        }

        public Task<Guid> StringToGuid(Guid guid, string test) {
            Console.WriteLine(test);
            return Task.FromResult(guid);
        }

        public IObservable<string> ObservableCounter(int count, int delay, CancellationToken cancellationToken, [FromServices]ConsoleWriter writer) {

            return GetNextInt
                .Take(count)
                .Do(i => writer.WriteInfo($"ObservableCounter: {i}"))
                .Select(i => $"ObservableCounter: {i}");

        }

        public IObservable<string> ObservableCounter(int count, int delay) {

            return GetNextInt
                .Take(count)
                .Do(i => _consoleWriter2.WriteInfo($"ObservableCounter: {i}"))
                .Select(i => $"ObservableCounter: {i}");

        }

        public Task<IObservable<string>> ObservableCounterTask(int count, int delay, CancellationToken cancellationToken) {

            return Task.Run(() => GetNextInt
                .Take(count)
                .Do(i => Console.WriteLine(i))
                .Select(i => $"ObservableCounterTask: {i}"));

        }

        public ChannelReader<string> ChannelCounter(int count, int delay, CancellationToken cancellationToken) {
            
            var channel = Channel.CreateUnbounded<string>();

            // We don't want to await WriteItemsAsync, otherwise we'd end up waiting 
            // for all the items to be written before returning the channel back to
            // the client.
            _ = WriteItemsAsync(channel.Writer, count, delay, cancellationToken);

            return channel.Reader;
        }

        private async Task WriteItemsAsync(
            ChannelWriter<string> writer,
            int count,
            int delay,
            CancellationToken cancellationToken) {
            Exception localException = null;
            try {
                for (var i = 0; i < count; i++) {
                    await writer.WriteAsync($"ChannelCounterTask: {i}", cancellationToken);

                    // Use the cancellationToken in other APIs that accept cancellation
                    // tokens so the cancellation can flow down to them.
                    await Task.Delay(delay, cancellationToken);
                }
            } catch (Exception ex) {
                localException = ex;
            }

            writer.Complete(localException);
        }

        public Task<ChannelReader<string>> ChannelCounterTask(int count, int delay, CancellationToken cancellationToken) {

            return Task.Run(() => {
                var channel = Channel.CreateUnbounded<string>();

                // We don't want to await WriteItemsAsync, otherwise we'd end up waiting 
                // for all the items to be written before returning the channel back to
                // the client.
                _ = WriteItemsAsync(channel.Writer, count, delay, cancellationToken);

                return channel.Reader;
            });

        }

        public IAsyncEnumerable<string> AsyncEnumerableCounter( int count, int delay, CancellationToken cancellationToken) {
            return InternalAsyncEnumerableCounter(count, delay, cancellationToken, "AsyncEnumerableCounter");
        }
        public Task<IAsyncEnumerable<string>> AsyncEnumerableCounterTask(int count, int delay, CancellationToken cancellationToken) {

            return Task.Run(() => InternalAsyncEnumerableCounter(count, delay, cancellationToken, "AsyncEnumerableCounterTask"));


        }

        public async IAsyncEnumerable<string> InternalAsyncEnumerableCounter(int count, int delay, [EnumeratorCancellation] CancellationToken cancellationToken, string name) {
            for (var i = 0; i < count; i++) {
                // Check the cancellation token regularly so that the server will stop
                // producing items if the client disconnects.
                cancellationToken.ThrowIfCancellationRequested();

                yield return $"{name}: {i}";

                // Use the cancellationToken in other APIs that accept cancellation
                // tokens so the cancellation can flow down to them.
                await Task.Delay(delay, cancellationToken);
            }
        }

        public async Task<DummyClass> GetDummyOrException(DummyClass dummy) {

            return await Task.Run(() => {
                if (dummy.Year == 0) {
                    throw new Exception("Year can't be '0'!");
                }

                return dummy;
            });

        }
    }
}

