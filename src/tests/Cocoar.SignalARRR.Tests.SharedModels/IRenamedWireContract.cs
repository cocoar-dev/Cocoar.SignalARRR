using System.Threading.Tasks;
using Cocoar.SignalARRR.Common.Attributes;
using Cocoar.SignalARRR.Contracts;

namespace Cocoar.SignalARRR.Tests.SharedModels {

    /// <summary>
    /// A contract whose wire names are declared rather than inherited from the C# identifiers (N-5).
    /// </summary>
    /// <remarks>
    /// It lives here, in a separate assembly referencing only <c>Cocoar.SignalARRR.Contracts</c>, on
    /// purpose: that is the documented shape of a shared-contracts project, and the source generator
    /// has to produce the declared names from it. The generator cannot use reflection, so it
    /// re-implements the naming rule against Roslyn symbols — if the two ever drift apart, calls
    /// resolve to nothing at runtime, which is what the accompanying test exists to catch.
    /// </remarks>
    [SignalARRRContract]
    [MessageName("renamed.contract")]
    public interface IRenamedWireContract {

        [MessageName("greet")]
        Task<string> SayHello(string name);

        /// <summary>No attribute — falls back to the C# name, alongside the renamed members.</summary>
        Task Untouched();
    }
}
