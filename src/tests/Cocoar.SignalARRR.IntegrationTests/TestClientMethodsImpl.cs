using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using TestShared;

namespace Cocoar.SignalARRR.IntegrationTests {
    // Minimal implementation for tests; only used methods return values, others throw if invoked.
    public class TestClientMethodsImpl : ITestClientMethods {
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
            return Task.Run(async () => { await Task.Delay(TimeSpan.FromSeconds(seconds), cancellationToken); return "done"; }, cancellationToken);
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
    }
}
