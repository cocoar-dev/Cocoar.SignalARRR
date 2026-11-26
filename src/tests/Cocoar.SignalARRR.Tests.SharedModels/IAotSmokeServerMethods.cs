using System.Threading.Tasks;
using Cocoar.SignalARRR.Contracts;

namespace Cocoar.SignalARRR.Tests.SharedModels {
    /// <summary>
    /// Client-to-server contract of the NativeAOT smoke server (src/tests/Cocoar.SignalARRR.AotSmokeServer):
    /// the shapes the generated server dispatch has to handle, called through a generated proxy.
    /// </summary>
    [SignalARRRContract]
    public interface IAotSmokeServerMethods {

        Task<int> Add(int a, int b);

        string Page(int index, int size = 25);

        /// <summary>Generic: the server declares the allowed type arguments with [GenericArguments].</summary>
        T Echo<T>(T value);
    }
}
