using System.Linq.Expressions;
using System.Reflection;
using BenchmarkDotNet.Attributes;
using Cocoar.Reflectensions.Helper;

namespace Cocoar.SignalARRR.Benchmarks;

/// <summary>
/// The P-5 decision benchmark: what does one method invocation cost on the current dispatch
/// path (Reflectensions <c>InvokeHelper</c>) compared to raw <c>MethodInfo.Invoke</c>, a
/// compiled delegate, and a direct call as the floor? No server, no transport — this isolates
/// exactly the slice P-5 would change, so its share of the ~104–230 µs roundtrip can be judged
/// before committing to a rewrite of the dispatch core.
/// </summary>
[MemoryDiagnoser]
public class InvokerBenchmarks {

    public class Target {
        public string Sync3(int number, string text, Guid id) => $"{number}|{text}|{id}";
        public Task<string> Async3(int number, string text, Guid id) => Task.FromResult($"{number}|{text}|{id}");
    }

    private readonly Target _target = new();
    private MethodInfo _sync3 = null!;
    private MethodInfo _async3 = null!;
    private Func<object, object[], object?> _compiledSync3 = null!;
    private Func<object, object[], object?> _compiledAsync3 = null!;
    private static readonly object[] Args = [7, "text", Guid.Parse("11111111-2222-3333-4444-555555555555")];

    [GlobalSetup]
    public void Setup() {
        _sync3 = typeof(Target).GetMethod(nameof(Target.Sync3))!;
        _async3 = typeof(Target).GetMethod(nameof(Target.Async3))!;
        _compiledSync3 = Compile(_sync3);
        _compiledAsync3 = Compile(_async3);
    }

    private static Func<object, object[], object?> Compile(MethodInfo method) {
        var instance = Expression.Parameter(typeof(object));
        var args = Expression.Parameter(typeof(object[]));
        var parameters = method.GetParameters()
            .Select((p, i) => (Expression)Expression.Convert(
                Expression.ArrayIndex(args, Expression.Constant(i)), p.ParameterType));
        var call = Expression.Call(Expression.Convert(instance, method.DeclaringType!), method, parameters);
        var body = Expression.Convert(call, typeof(object));
        return Expression.Lambda<Func<object, object[], object?>>(body, instance, args).Compile();
    }

    // The current dispatch path: Reflectensions wraps sync methods in Task.Run per its docs —
    // this row carries that scheduler hop.
    [Benchmark(Baseline = true)]
    public Task<object?> Reflectensions_Sync3() => InvokeHelper.InvokeMethodAsync<object?>(_target, _sync3, Args);

    [Benchmark]
    public Task<object?> Reflectensions_Async3() => InvokeHelper.InvokeMethodAsync<object?>(_target, _async3, Args);

    [Benchmark]
    public object? MethodInfoInvoke_Sync3() => _sync3.Invoke(_target, Args);

    [Benchmark]
    public async Task<object?> MethodInfoInvoke_Async3() => await (Task<string>)_async3.Invoke(_target, Args)!;

    [Benchmark]
    public object? Compiled_Sync3() => _compiledSync3(_target, Args);

    [Benchmark]
    public async Task<object?> Compiled_Async3() => await (Task<string>)_compiledAsync3(_target, Args)!;

    [Benchmark]
    public string Direct_Sync3() => _target.Sync3(7, "text", Guid.Parse("11111111-2222-3333-4444-555555555555"));
}
