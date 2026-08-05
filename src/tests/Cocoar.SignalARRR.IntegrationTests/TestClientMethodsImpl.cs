using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using TestShared;

namespace Cocoar.SignalARRR.IntegrationTests {
    // Minimal implementation for tests; only used methods return values, others throw if invoked.
    public class TestClientMethodsImpl : ITestClientMethods {

        /// <summary>The <c>seconds</c> argument the last <see cref="Wait"/> call received.</summary>
        /// <remarks>
        /// Recorded so a test can tell that the argument arrived <em>as sent</em>. When a token was
        /// left out of the arguments while the binder still counted a slot for it, every following
        /// argument shifted — the failure this proves is absent.
        /// </remarks>
        public int? LastWaitSeconds { get; private set; }

        /// <summary>Whether the token handed to the last <see cref="Wait"/> call actually fired.</summary>
        /// <remarks>
        /// This is the thing worth asserting. What the server-side await throws says little: SignalR
        /// aborts the pending invocation itself and surfaces a <c>HubException</c>, which looks the
        /// same whether the client's token worked or the call fell over for an unrelated reason.
        /// </remarks>
        public bool WaitObservedCancellation { get; private set; }
        public T Invoke<T>(string command, Dictionary<string, object>? variables = null) {
            throw new NotImplementedException();
        }

        public void Nix() {
            // no-op
        }

        public List<string> GetContent(int count) {
            var list = new List<string>();
            for (int i = 0; i < count; i++) list.Add($"item-{i}");
            return list;
        }

        public bool CreateObject(string className, Dictionary<string, object> properties) {
            return true;
        }

        public bool CreateObjectFromTemplate(string templateName, Dictionary<string, object> properties) {
            return true;
        }

        public long FileLength(string id, Stream filestream) {
            return filestream?.Length ?? 0;
        }

        public void Complex1(ComplexTestClass compl) {
            // no-op
        }

        public IncidentClass TestExpandableObject(IncidentClass expandableObject) {
            return expandableObject;
        }

        public Task<string> Wait(int seconds, CancellationToken cancellationToken) {
            LastWaitSeconds = seconds;

            return Task.Run(async () => {
                try {
                    await Task.Delay(TimeSpan.FromSeconds(seconds), cancellationToken);
                    return "done";
                } catch (OperationCanceledException) {
                    WaitObservedCancellation = true;
                    throw;
                }
            }, cancellationToken);
        }

        public string GetByGenericId(Guid id) {
            return id.ToString();
        }

        public string GetById(string id) {
            return id;
        }

        public async IAsyncEnumerable<int> StreamNumbers(int count) {
            for (int i = 0; i < count; i++) {
                await Task.Delay(10);
                yield return i;
            }
        }

        public Stream GetFileStream(string content) {
            var stream = new MemoryStream();
            var writer = new StreamWriter(stream);
            writer.Write(content);
            writer.Flush();
            stream.Position = 0;
            return stream;
        }
    }
}
