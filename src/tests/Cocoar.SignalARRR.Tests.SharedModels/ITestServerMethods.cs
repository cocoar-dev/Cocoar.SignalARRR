using System;
using System.Threading.Tasks;
using Cocoar.SignalARRR.Contracts;

namespace Cocoar.SignalARRR.Tests.SharedModels {
    [SignalARRRContract]
    public interface ITestServerMethods {

        string GetName();

        Task<string> GetNameAsync();

        Guid GetGuid();
        Task<Guid> GetGuidAsync();

        void Nothing();

        Task NothingAsync();
    }
}
