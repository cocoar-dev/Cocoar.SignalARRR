using Cocoar.SignalARRR.Server;

namespace Cocoar.SignalARRR.Benchmarks;

/// <summary>
/// Minimal hub for the roundtrip benchmarks. Methods do no work of their own, so the
/// measurement is the framework path: dispatch, binding, authorization, serialization.
/// </summary>
public class BenchmarkHub : HARRR {

    public BenchmarkHub(IServiceProvider serviceProvider) : base(serviceProvider) {
    }

    public int Ping() => 42;

    public int Echo1(int value) => value;

    public string Echo3(int number, string text, Guid id) => $"{number}|{text}|{id}";
}

/// <summary>
/// The same three shapes on the ServerMethods dispatch path, which additionally builds the
/// invoke-type instance per message (the P-4 finding) — measured separately for that reason.
/// </summary>
public class BenchmarkMethods : ServerMethods<BenchmarkHub> {

    public int SmPing() => 42;

    public int SmEcho1(int value) => value;

    public string SmEcho3(int number, string text, Guid id) => $"{number}|{text}|{id}";
}
