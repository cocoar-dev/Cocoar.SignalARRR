using System.Text.Json.Serialization;
using BenchmarkDotNet.Attributes;
using Cocoar.SignalARRR.Client;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.SignalR.Client;

namespace Cocoar.SignalARRR.Benchmarks;

/// <summary>
/// Full client-to-server roundtrips over a real Kestrel loopback connection — the number a
/// consumer of the library actually experiences. Raw <c>InvokeCoreAsync</c> is used on purpose:
/// it is byte-identical with what the TypeScript and Swift clients send, and it keeps typed-proxy
/// overhead out of the measurement.
///
/// Two dispatch paths, three argument counts each: methods declared on the hub itself, and
/// methods on a <c>ServerMethods</c> class (which additionally constructs the invoke-type
/// instance per message — the P-4 finding). Telemetry stays in its default state (no listener),
/// matching a production host that has not opted in.
/// </summary>
[MemoryDiagnoser]
public class RoundtripBenchmarks {

    private WebApplication _app = null!;
    private HARRRConnection _connection = null!;

    private static readonly object[] NoArgs = [];
    private static readonly object[] OneArg = [7];
    private static readonly object[] ThreeArgs = [7, "text", Guid.Parse("11111111-2222-3333-4444-555555555555")];

    [GlobalSetup]
    public async Task Setup() {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");

        builder.Services.AddRouting();
        builder.Services.AddSignalR().AddJsonProtocol(options => {
            options.PayloadSerializerOptions.PropertyNamingPolicy = null;
            options.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter());
        });
        builder.Services.AddSignalARRR(b => b.AddServerMethodsFrom(typeof(BenchmarkHub).Assembly));

        _app = builder.Build();
        _app.MapSignalARRRHub<BenchmarkHub>("/signalr/benchmarkhub");
        await _app.StartAsync();

        var address = _app.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!.Addresses.First();

        _connection = HARRRConnection.Create(hub => hub.WithUrl($"{address}/signalr/benchmarkhub"));
        await _connection.StartAsync();

        // One warm call per method so startup work (registry, serializer metadata) does not
        // land in the first measured iteration.
        await HubMethod_0Args();
        await HubMethod_1Arg();
        await HubMethod_3Args();
        await ServerMethodsClass_0Args();
        await ServerMethodsClass_1Arg();
        await ServerMethodsClass_3Args();
    }

    [GlobalCleanup]
    public async Task Cleanup() {
        await _connection.StopAsync();
        await _connection.DisposeAsync();
        await _app.StopAsync();
        await _app.DisposeAsync();
    }

    [Benchmark(Baseline = true)]
    public Task<int> HubMethod_0Args() => _connection.InvokeCoreAsync<int>("Ping", NoArgs);

    [Benchmark]
    public Task<int> HubMethod_1Arg() => _connection.InvokeCoreAsync<int>("Echo1", OneArg);

    [Benchmark]
    public Task<string> HubMethod_3Args() => _connection.InvokeCoreAsync<string>("Echo3", ThreeArgs);

    [Benchmark]
    public Task<int> ServerMethodsClass_0Args() => _connection.InvokeCoreAsync<int>("BenchmarkMethods.SmPing", NoArgs);

    [Benchmark]
    public Task<int> ServerMethodsClass_1Arg() => _connection.InvokeCoreAsync<int>("BenchmarkMethods.SmEcho1", OneArg);

    [Benchmark]
    public Task<string> ServerMethodsClass_3Args() => _connection.InvokeCoreAsync<string>("BenchmarkMethods.SmEcho3", ThreeArgs);
}
