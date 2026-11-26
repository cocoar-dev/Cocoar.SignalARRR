using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Cocoar.SignalARRR.Common.Attributes;
using Cocoar.SignalARRR.Contracts;

namespace Cocoar.SignalARRR.Tests.SharedModels {
    /// <summary>Inherited members of <see cref="IDispatchProbe"/>, to pin how the generated dispatcher handles inheritance.</summary>
    public interface IDispatchProbeBase {
        string Inherited(int value);

        /// <summary>Hidden by <see cref="IDispatchProbe.Shadowed()"/>: the contract's own member of a name wins.</summary>
        string Shadowed(int value);
    }

    /// <summary>
    /// Exercises the shapes the generated dispatcher has to handle: defaults, overloads by argument
    /// count, inheritance, [MessageName], token slots and every streaming return type.
    /// </summary>
    [SignalARRRContract]
    public interface IDispatchProbe : IDispatchProbeBase {
        string WithDefaults(string first, int second = 7, string? third = "x");

        string Overload(int a);

        string Overload(int a, int b, int c);

        string Shadowed();

        [MessageName("Renamed")]
        string Original();

        Task<string> WaitFor(string label, CancellationToken cancellationToken);

        Task Nothing();

        ChannelReader<int> Channel(int count);

        IObservable<int> Observe(int count);
    }

    /// <summary>Two members reachable under one name with the same argument count: not receivable on a client.</summary>
    [SignalARRRContract]
    public interface IAmbiguousDispatchProbe {
        string Same(int value);

        string Same(string value);
    }
}
