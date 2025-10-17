using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SignalARRR.Tests.SharedModels {
    public interface ITestServerMethods {

        string GetName();

        Task<string> GetNameAsync();

        Guid GetGuid();
        Task<Guid> GetGuidAsync();

        void Nothing();

        Task NothingAsync();
    }
}
