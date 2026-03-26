using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

// These interfaces must match the namespaces and names used on the server.
// No [SignalARRRContract] needed — FullFramework client uses DispatchProxy.

namespace Cocoar.SignalARRR.Tests.SharedModels {
    public interface ITestServerMethods {
        string GetName();
        Task<string> GetNameAsync();
        Guid GetGuid();
        Task<Guid> GetGuidAsync();
        void Nothing();
        Task NothingAsync();
    }
}

namespace TestShared {
    public interface ITestClientMethods {
        void Nix();
        List<string> GetContent(int count);
        string GetById(string id);
        string GetByGenericId(Guid id);
        Task<string> Wait(int seconds, CancellationToken cancellationToken);
    }

    public class TestClientMethodsImpl : ITestClientMethods {
        public void Nix() { }

        public List<string> GetContent(int count) {
            var items = new List<string>();
            for (int i = 0; i < count; i++) items.Add($"item-{i}");
            return items;
        }

        public string GetById(string id) => $"result-{id}";
        public string GetByGenericId(Guid id) => $"guid-{id}";

        public async Task<string> Wait(int seconds, CancellationToken cancellationToken) {
            await Task.Delay(seconds * 1000, cancellationToken);
            return "done";
        }
    }
}
