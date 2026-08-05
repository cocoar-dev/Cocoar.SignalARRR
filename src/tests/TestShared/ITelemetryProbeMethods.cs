using Cocoar.SignalARRR.Contracts;

namespace TestShared {
    /// <summary>
    /// Client contract for the trace-propagation tests: returns what the client observes as its
    /// current trace id while handling a server-to-client call.
    /// </summary>
    [SignalARRRContract]
    public interface ITelemetryProbeMethods {
        string TraceProbe();
    }
}
