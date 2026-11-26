using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using Cocoar.SignalARRR.Client;
using Cocoar.SignalARRR.Tests.SharedModels;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;

// Usage: AotSmoke <server-url> [--smoke-server]
// Runs the client paths a NativeAOT app depends on against a server and exits non-zero when any
// of them fails. Each step prints one line, "OK" or "FAIL", so a red run says exactly which
// direction broke. With --smoke-server the server is the NativeAOT smoke server
// (Cocoar.SignalARRR.AotSmokeServer), and the steps for its generated server dispatch run too.

if (args.Length is < 1 or > 2 || (args.Length == 2 && args[1] != "--smoke-server")) {
    Console.Error.WriteLine("Usage: AotSmoke <server-url> [--smoke-server]");
    return 2;
}

var url = args[0].TrimEnd('/');
var smokeServer = args.Length == 2;
Console.WriteLine($"Dynamic code supported: {RuntimeFeature.IsDynamicCodeSupported}");

var failures = 0;

async Task Step(string name, Func<Task<object?>> body) {
    try {
        var result = await body().WaitAsync(TimeSpan.FromSeconds(10));
        Console.WriteLine($"OK   {name}: {result}");
    } catch (Exception ex) {
        failures++;
        var e = ex is AggregateException a ? a.GetBaseException() : ex;
        Console.WriteLine($"FAIL {name}: {e.GetType().Name}: {e.Message.Split('\n')[0]}");
        Console.WriteLine("       at " + (e.StackTrace ?? "").Split('\n').FirstOrDefault()?.Trim());
    }
}

static void Expect(bool condition, string message) {
    if (!condition) throw new InvalidOperationException(message);
}

static async Task<object?> ExpectUnauthorized(Func<Task<string>> call) {
    try {
        var result = await call();
        throw new InvalidOperationException($"expected the call to be denied, got '{result}'");
    } catch (Cocoar.SignalARRR.Common.Exceptions.HARRRRemoteException ex) when (ex.Error.NormalizedCode == Cocoar.SignalARRR.Common.HARRRErrorCodes.Unauthorized) {
        return "denied";
    }
}

HARRRConnection? connection = null;
var push = new PushClient();
using var http = new HttpClient();

await Step("Create", () => {
    connection = HARRRConnection.Create(b => b
        .WithUrl($"{url}/signalr/testhub")
        .AddJsonProtocol(o => o.PayloadSerializerOptions.TypeInfoResolverChain.Insert(0, AppJson.Default)));
    return Task.FromResult<object?>("created");
});
if (connection is null) return 1;
var c = connection;

await Step("RegisterInterface", () => {
    c.RegisterInterface<ITestServerPushClient>(push);
    return Task.FromResult<object?>("registered");
});
await Step("StartAsync", async () => { await c.StartAsync(); return c.ConnectionId; });

var typed = c.GetTypedMethods<ITestServerMethods>();
await Step("Proxy GetNameAsync (string)", async () => {
    var name = await typed.GetNameAsync();
    Expect(name == "MyNameAsync", $"expected 'MyNameAsync', got '{name}'");
    return name;
});
await Step("Proxy GetGuidAsync (Guid)", async () => {
    var guid = await typed.GetGuidAsync();
    Expect(guid != Guid.Empty, "expected a non-empty Guid");
    return guid;
});
await Step("Proxy NothingAsync (Task)", async () => { await typed.NothingAsync(); return "done"; });
await Step("Proxy GetName (sync)", () => Task.Run<object?>(() => typed.GetName()));
await Step("InvokeCoreAsync<string> Echo(string)", async () => {
    var echo = await c.InvokeCoreAsync<string>("Echo", new object[] { "hi" });
    Expect(echo == "hi", $"expected 'hi', got '{echo}'");
    return echo;
});
await Step("StreamAsyncCore<int> Counter(int,int)", async () => {
    var items = new List<int>();
    await foreach (var i in c.StreamAsyncCore<int>("Counter", new object[] { 3, 0 })) items.Add(i);
    Expect(items.Count == 3, $"expected 3 items, got {items.Count}");
    return string.Join(",", items);
});
await Step("Server->Client PushNotification(string)", async () => {
    (await http.PostAsync($"{url}/__test/push-notification?connectionId={c.ConnectionId}&message=hello", null)).EnsureSuccessStatusCode();
    var message = await push.Push.Task.WaitAsync(TimeSpan.FromSeconds(5));
    Expect(message == "hello", $"expected 'hello', got '{message}'");
    return message;
});
await Step("Server->Client ConfigUpdated(null, string)", async () => {
    (await http.PostAsync($"{url}/__test/config-updated?connectionId={c.ConnectionId}&configJson=%7B%7D", null)).EnsureSuccessStatusCode();
    var received = await push.Config.Task.WaitAsync(TimeSpan.FromSeconds(5));
    Expect(received == "path=null json={}", $"unexpected arguments: {received}");
    return received;
});
await Step("Server->Client RequestClientInfo() -> Task<string>", async () => {
    var response = await http.PostAsync($"{url}/__test/request-client-info?connectionId={c.ConnectionId}", null);
    var body = await response.Content.ReadAsStringAsync();
    Expect(response.IsSuccessStatusCode && body.Contains("aot-smoke-client"), $"{(int)response.StatusCode} {body}");
    return body;
});

if (smokeServer) {
    var smoke = c.GetTypedMethods<IAotSmokeServerMethods>();
    await Step("Server: interface method on ServerMethods (Add)", async () => {
        var sum = await smoke.Add(2, 3);
        Expect(sum == 5, $"expected 5, got {sum}");
        return sum;
    });
    await Step("Server: omitted default parameter (Page)", async () => {
        var page = await c.InvokeCoreAsync<string>("SmokeMethods.Page", new object[] { 3 });
        Expect(page == "3:25", $"expected '3:25', got '{page}'");
        return page;
    });
    await Step("Server: generic method Echo<int>", () => Task.Run<object?>(() => {
        var value = smoke.Echo(7);
        Expect(value == 7, $"expected 7, got {value}");
        return value;
    }));
    await Step("Server: generic method Echo<string>", () => Task.Run<object?>(() => {
        var value = smoke.Echo("generic");
        Expect(value == "generic", $"expected 'generic', got '{value}'");
        return value;
    }));
    await Step("Server: [FromServices] parameter", async () => {
        var injected = await c.InvokeCoreAsync<string>("SmokeMethods.Inject", Array.Empty<object>());
        Expect(injected == "injected", $"expected 'injected', got '{injected}'");
        return injected;
    });
    await Step("Server: Stream argument (upload)", async () => {
        var content = await c.InvokeCoreAsync<string>("SmokeMethods.ReadStream",
            new object[] { new MemoryStream(System.Text.Encoding.UTF8.GetBytes("uploaded")) });
        Expect(content == "uploaded", $"expected 'uploaded', got '{content}'");
        return content;
    });
    await Step("Server: method-level [Authorize] denies", () => ExpectUnauthorized(() => c.InvokeCoreAsync<string>("SmokeMethods.Secret", Array.Empty<object>())));
    await Step("Server: type-level [Authorize] denies", () => ExpectUnauthorized(() => c.InvokeCoreAsync<string>("SecureMethods.Closed", Array.Empty<object>())));
    await Step("Server: [AllowAnonymous] opts out", async () => await c.InvokeCoreAsync<string>("SecureMethods.Open", Array.Empty<object>()));
}

await c.StopAsync();
await c.DisposeAsync();

Console.WriteLine(failures == 0 ? "AOT smoke: all steps passed" : $"AOT smoke: {failures} step(s) failed");
return failures == 0 ? 0 : 1;

/// <summary>
/// The app's own wire types. SignalARRR brings its envelope types itself; what an AOT app has to
/// declare is the types of its arguments and results.
/// </summary>
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(Guid))]
[JsonSerializable(typeof(int))]
internal partial class AppJson : JsonSerializerContext { }

internal sealed class PushClient : ITestServerPushClient {
    public TaskCompletionSource<string> Push { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource<string> Config { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public void PushNotification(string message) => Push.TrySetResult(message);
    public void ConfigUpdated(string? path, string configJson) => Config.TrySetResult($"path={path ?? "null"} json={configJson}");
    public Task<string> RequestClientInfo() => Task.FromResult("aot-smoke-client");
}
