using System.Text.Json;
using System.Text.Json.Serialization;
using System.Security.Claims;
using Cocoar.SignalARRR.Server.ExtensionMethods;
using IntegrationTestServer;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.SignalR;

var diagnosticsLogFilePath = Environment.GetEnvironmentVariable("SIGNALARRR_DIAGNOSTICS_LOG_FILE");
void WriteDiagnostics(string message) {
    if (string.IsNullOrWhiteSpace(diagnosticsLogFilePath)) {
        return;
    }

    var directory = Path.GetDirectoryName(diagnosticsLogFilePath);
    if (!string.IsNullOrWhiteSpace(directory)) {
        Directory.CreateDirectory(directory);
    }

    for (int attempt = 0; attempt < 3; attempt++) {
        try {
            File.AppendAllText(
                diagnosticsLogFilePath,
                $"{DateTime.UtcNow:O} [IntegrationTestServer] {message}{Environment.NewLine}");
            break;
        } catch (IOException) when (attempt < 2) {
            Thread.Sleep(10);
        }
    }
}

// Observe the library's ActivitySource so server-side spans actually get created — without a
// listener every StartActivity returns null and the trace-propagation tests would assert against
// ASP.NET's ambient request activity instead of SignalARRR's own spans.
var signalArrrActivityListener = new System.Diagnostics.ActivityListener {
    ShouldListenTo = source => source.Name == Cocoar.SignalARRR.Common.SignalARRRTelemetry.ActivitySourceName,
    Sample = (ref System.Diagnostics.ActivityCreationOptions<System.Diagnostics.ActivityContext> _) =>
        System.Diagnostics.ActivitySamplingResult.AllData,
};
System.Diagnostics.ActivitySource.AddActivityListener(signalArrrActivityListener);

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.UseUrls("http://127.0.0.1:0");

builder.Services.AddRouting();

// EnableDetailedErrors deliberately NOT set — HARRRException extends HubException,
// so SignalR should pass the message through regardless.
builder.Services.AddSignalR()
    .AddJsonProtocol(options => {
        options.PayloadSerializerOptions.PropertyNamingPolicy = null;
        options.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    })
    .AddMessagePackProtocol();
builder.Services.AddSingleton<IUserIdProvider, QueryStringUserIdProvider>();

builder.Services.AddSignalARRR(b => b.AddServerMethodsFrom(typeof(TestHub).Assembly));
builder.Services.AddSignalARRRHealthChecks();

var backplaneConnectionString = Environment.GetEnvironmentVariable("SIGNALARRR_BACKPLANE_CONNECTION_STRING");
if (!string.IsNullOrWhiteSpace(backplaneConnectionString)) {
    builder.Services.AddSignalARRRRedisBackplane(options => {
        options.WithConnectionString(backplaneConnectionString);

        var channelPrefix = Environment.GetEnvironmentVariable("SIGNALARRR_BACKPLANE_CHANNEL_PREFIX");
        if (!string.IsNullOrWhiteSpace(channelPrefix)) {
            options.WithChannelPrefix(channelPrefix);
        }

        var nodeId = Environment.GetEnvironmentVariable("SIGNALARRR_BACKPLANE_NODE_ID");
        if (!string.IsNullOrWhiteSpace(nodeId)) {
            options.WithNodeId(nodeId);
        }

        var heartbeatIntervalMs = Environment.GetEnvironmentVariable("SIGNALARRR_BACKPLANE_HEARTBEAT_INTERVAL_MS");
        if (int.TryParse(heartbeatIntervalMs, out var heartbeatInterval)) {
            options.WithHeartbeatInterval(TimeSpan.FromMilliseconds(heartbeatInterval));
        }

        var nodeTimeoutMs = Environment.GetEnvironmentVariable("SIGNALARRR_BACKPLANE_NODE_TIMEOUT_MS");
        if (int.TryParse(nodeTimeoutMs, out var nodeTimeout)) {
            options.WithNodeTimeout(TimeSpan.FromMilliseconds(nodeTimeout));
        }

        var invokeTimeoutMs = Environment.GetEnvironmentVariable("SIGNALARRR_BACKPLANE_INVOKE_TIMEOUT_MS");
        if (int.TryParse(invokeTimeoutMs, out var invokeTimeout)) {
            options.WithInvokeTimeout(TimeSpan.FromMilliseconds(invokeTimeout));
        }
    });
}

var app = builder.Build();
app.Lifetime.ApplicationStarted.Register(() => WriteDiagnostics("application-started"));
app.Lifetime.ApplicationStopping.Register(() => WriteDiagnostics("application-stopping"));

app.MapSignalARRRHub<TestHub>("/signalr/testhub");

// Both hubs below exist only for StatefulReconnectTests, and both are deliberately separate from
// TestHub: that test leaves a connection with a dead transport registered for a moment, and the
// broadcast-to-all tests running in parallel against this same process would reach it and fail for
// a reason of their own making. The second one is the control — plain SignalR, no SignalARRR.
app.MapSignalARRRHub<ReconnectHub>("/signalr/reconnecthub", options => options.AllowStatefulReconnects = true);
app.MapHub<PlainReconnectHub>("/signalr/plainhub", options => options.AllowStatefulReconnects = true);

// Test trigger endpoints for server→client calls (used by .NET, TS, and Swift integration tests)

app.MapSignalARRRTest("/__test/trigger-client-call", async (context, clientManager) => {
    var request = context.Request;
    var connectionId = request.Query["connectionId"].ToString();
    var method = request.Query["method"].ToString();
    var arg = request.Query["arg"].ToString();

    if (string.IsNullOrWhiteSpace(connectionId) || string.IsNullOrWhiteSpace(method)) {
        return Results.BadRequest("Missing connectionId or method");
    }

    var hubContext = context.RequestServices.GetRequiredService<Microsoft.AspNetCore.SignalR.IHubContext<TestHub>>();
    var msg = new Cocoar.SignalARRR.Common.ServerRequestMessage(method, string.IsNullOrEmpty(arg) ? Array.Empty<object>() : new object[] { arg });

    await hubContext.Clients.Client(connectionId)
        .SendCoreAsync(Cocoar.SignalARRR.Common.Constants.MethodNames.InvokeServerMessage, new object[] { msg }, default);

    return "Sent";
});

app.MapSignalARRRTest("/__test/trigger-client-cancellation", async (context, clientManager) => {
    var request = context.Request;
    var connectionId = request.Query["connectionId"].ToString();
    var delayMs = int.TryParse(request.Query["delayMs"].ToString(), out var d) ? d : 200;

    if (string.IsNullOrWhiteSpace(connectionId)) {
        return Results.BadRequest("Missing connectionId");
    }

    var cts = new CancellationTokenSource();
    var typedClient = clientManager.GetTypedMethods<TestShared.ITestClientMethods>(connectionId);

    var waitTask = typedClient.Wait(30, cts.Token);
    await Task.Delay(delayMs);
    cts.Cancel();

    // Reports *why* it ended, not just that it did. Returning a bare "cancelled" for any exception
    // made the test that reads this pass while the call was in fact failing outright: with the
    // token missing from the arguments, the client bound one argument too few and threw
    // IndexOutOfRangeException, which looked exactly like a successful cancellation from here.
    try {
        await waitTask;
        return (object)"completed";
    } catch (OperationCanceledException) {
        return (object)"cancelled";
    } catch (Exception ex) {
        return (object)$"failed: {ex.GetType().Name}: {ex.Message}";
    }
});

app.MapSignalARRRTest("/__test/trigger-client-stream", async (context, clientManager) => {
    var request = context.Request;
    var connectionId = request.Query["connectionId"].ToString();
    var count = int.TryParse(request.Query["count"].ToString(), out var c) ? c : 5;

    if (string.IsNullOrWhiteSpace(connectionId)) {
        return Results.BadRequest("Missing connectionId");
    }

    var typedClient = clientManager.GetTypedMethods<TestShared.ITestClientMethods>(connectionId);
    var items = new List<int>();
    await foreach (var item in typedClient.StreamNumbers(count)) {
        items.Add(item);
    }

    return (object)items;
});

app.MapSignalARRRTest("/__test/trigger-client-typed-call", (context, clientManager) => {
    var request = context.Request;
    var connectionId = request.Query["connectionId"].ToString();

    if (string.IsNullOrWhiteSpace(connectionId)) {
        return Results.BadRequest("Missing connectionId");
    }

    var typedClient = clientManager.GetTypedMethods<TestShared.ITestClientMethods>(connectionId);
    typedClient.Nix();

    return "Sent";
});

// Server calls client method and awaits return value (tests sync return types via ServerReplyManager)
app.MapSignalARRRTest("/__test/trigger-client-getbyid", async (context, clientManager) => {
    var connectionId = context.Request.Query["connectionId"].ToString();
    var id = context.Request.Query["id"].ToString();

    if (string.IsNullOrWhiteSpace(connectionId) || string.IsNullOrWhiteSpace(id)) {
        return Results.BadRequest("Missing connectionId or id");
    }

    var typedClient = clientManager.GetTypedMethods<TestShared.ITestClientMethods>(connectionId);
    var result = await Task.Run(() => typedClient.GetById(id));
    return (object)result;
});

// Server calls client method with list return
app.MapSignalARRRTest("/__test/trigger-client-getcontent", async (context, clientManager) => {
    var connectionId = context.Request.Query["connectionId"].ToString();
    var count = int.TryParse(context.Request.Query["count"].ToString(), out var c) ? c : 3;

    if (string.IsNullOrWhiteSpace(connectionId)) {
        return Results.BadRequest("Missing connectionId");
    }

    var typedClient = clientManager.GetTypedMethods<TestShared.ITestClientMethods>(connectionId);
    var result = await Task.Run(() => typedClient.GetContent(count));
    return (object)result;
});

// Server calls client GetByGenericId with Guid parameter
app.MapSignalARRRTest("/__test/trigger-client-getbygenericid", async (context, clientManager) => {
    var connectionId = context.Request.Query["connectionId"].ToString();
    var guidStr = context.Request.Query["id"].ToString();

    if (string.IsNullOrWhiteSpace(connectionId) || string.IsNullOrWhiteSpace(guidStr)) {
        return Results.BadRequest("Missing connectionId or id");
    }

    var typedClient = clientManager.GetTypedMethods<TestShared.ITestClientMethods>(connectionId);
    var result = await Task.Run(() => typedClient.GetByGenericId(Guid.Parse(guidStr)));
    return (object)result;
});

app.MapHealthChecks("/health");

// N-3 probe state: what a server method's CancellationToken observed (see ExtraMethods).
app.MapSignalARRRTest("/__test/abort-probe", (context, clientManager) => {
    var probeId = context.Request.Query["probeId"].ToString();
    return (object)(IntegrationTestServer.AbortProbes.State.TryGetValue(probeId, out var state) ? state : "none");
});

// N-2 probe: invokes the client's Wait WITHOUT ever cancelling — the client-side token may then
// only fire through the client's own connection-lifetime binding. Fire-and-forget on purpose:
// the invocation is expected to die with the connection.
app.MapSignalARRRTest("/__test/trigger-client-wait-nocancel", (context, clientManager) => {
    var connectionId = context.Request.Query["connectionId"].ToString();
    var seconds = int.TryParse(context.Request.Query["seconds"].ToString(), out var s) ? s : 30;

    if (string.IsNullOrWhiteSpace(connectionId)) {
        return Results.BadRequest("Missing connectionId");
    }

    var typedClient = clientManager.GetTypedMethods<TestShared.ITestClientMethods>(connectionId);
    _ = Task.Run(async () => {
        try {
            await typedClient.Wait(seconds, CancellationToken.None);
        } catch {
            // Expected: the connection is torn down mid-call.
        }
    });

    return (object)"started";
});

// Trace propagation probe (server→client): starts a span, calls the client's TraceProbe, and
// returns "<serverTraceId>|<clientTraceId>" so the test can assert both halves saw one trace.
app.MapSignalARRRTest("/__test/trace-probe", async (context, clientManager) => {
    var connectionId = context.Request.Query["connectionId"].ToString();

    if (string.IsNullOrWhiteSpace(connectionId)) {
        return Results.BadRequest("Missing connectionId");
    }

    using var activity = Cocoar.SignalARRR.Common.SignalARRRTelemetry.ActivitySource.StartActivity("test-trace-probe");
    var serverTraceId = activity?.TraceId.ToString() ?? "";

    var typedClient = clientManager.GetTypedMethods<TestShared.ITelemetryProbeMethods>(connectionId);
    var clientTraceId = await Task.Run(() => typedClient.TraceProbe());
    return (object)$"{serverTraceId}|{clientTraceId}";
});

// Returns client's custom attributes (from headers with # prefix and query params with @ prefix)
app.MapSignalARRRTest("/__test/get-client-attributes", (context, clientManager) => {
    var connectionId = context.Request.Query["connectionId"].ToString();

    if (string.IsNullOrWhiteSpace(connectionId)) {
        return Results.BadRequest("Missing connectionId");
    }

    var client = clientManager.GetClientById(connectionId);
    var attrs = new Dictionary<string, string?>();
    foreach (var kvp in client.Attributes) {
        attrs[kvp.Key] = kvp.Value.ToString();
    }
    return (object)attrs;
});

// Check if a client is registered in ClientManager (used by tests to wait for registration)
app.MapGet("/__test/client-exists", (HttpContext context) => {
    var connectionId = context.Request.Query["connectionId"].ToString();
    if (string.IsNullOrWhiteSpace(connectionId)) return Results.BadRequest("Missing connectionId");

    var clientManager = context.RequestServices.GetRequiredService<Cocoar.SignalARRR.Server.ClientManager>();
    try {
        // Null-checked, not just non-throwing: GetClient returns null for an unknown id rather than
        // throwing, so answering "true" for anything that did not throw made this endpoint answer
        // true always — and WaitForClientRegistration, which polls it, return on its first attempt
        // without waiting for anything at all. Every test using it was racing the registration and
        // winning only because the window is small.
        var client = clientManager.GetClientById(connectionId);
        return Results.Ok(client != null);
    } catch {
        return Results.Ok(false);
    }
});

// Server calls client GetFileStream — client returns a Stream via HTTP upload
app.MapSignalARRRTest("/__test/trigger-client-getfilestream", async (context, clientManager) => {
    var connectionId = context.Request.Query["connectionId"].ToString();
    var content = context.Request.Query["content"].ToString();

    if (string.IsNullOrWhiteSpace(connectionId) || string.IsNullOrWhiteSpace(content)) {
        return Results.BadRequest("Missing connectionId or content");
    }

    var typedClient = clientManager.GetTypedMethods<TestShared.ITestClientMethods>(connectionId);
    // GetFileStream returns Stream — this triggers the upload flow:
    // Client calls RequestUploadSlot, uploads via HTTP, returns StreamReference
    // Server resolves StreamReference and gets the actual Stream
    var stream = await Task.Run(() => typedClient.GetFileStream(content));

    using var reader = new System.IO.StreamReader(stream);
    var result = await reader.ReadToEndAsync();
    stream.Dispose();

    return (object)result;
});

// Join a SignalR group via ClientManager (tracks in both SignalR AND ClientContext.Groups)
app.MapGet("/__test/join-group", async (HttpContext context) => {
    var connectionId = context.Request.Query["connectionId"].ToString();
    var groupName = context.Request.Query["group"].ToString();
    if (string.IsNullOrWhiteSpace(connectionId) || string.IsNullOrWhiteSpace(groupName))
        return Results.BadRequest("Missing connectionId or group");

    var clientManager = context.RequestServices.GetRequiredService<Cocoar.SignalARRR.Server.ClientManager>();
    await clientManager.AddToGroupAsync(connectionId, groupName);
    return Results.Ok(true);
});

app.MapGet("/__test/leave-group", async (HttpContext context) => {
    var connectionId = context.Request.Query["connectionId"].ToString();
    var groupName = context.Request.Query["group"].ToString();
    if (string.IsNullOrWhiteSpace(connectionId) || string.IsNullOrWhiteSpace(groupName))
        return Results.BadRequest("Missing connectionId or group");

    var clientManager = context.RequestServices.GetRequiredService<Cocoar.SignalARRR.Server.ClientManager>();
    await clientManager.RemoveFromGroupAsync(connectionId, groupName);
    return Results.Ok(true);
});

// Typed broadcast to a group via WithHub + WithGroup + SendAsync
app.MapPost("/__test/broadcast-group-nix", async (HttpContext context) => {
    var groupName = context.Request.Query["group"].ToString();
    if (string.IsNullOrWhiteSpace(groupName))
        return Results.BadRequest("Missing group");

    var clientManager = context.RequestServices.GetRequiredService<Cocoar.SignalARRR.Server.ClientManager>();
    await clientManager.WithHub<TestHub>().WithGroup(groupName)
        .SendAsync<TestShared.ITestClientMethods>(c => c.Nix());
    return Results.Ok("Sent");
});

// Typed broadcast to all clients on TestHub
app.MapPost("/__test/broadcast-all-nix", async (HttpContext context) => {
    var clientManager = context.RequestServices.GetRequiredService<Cocoar.SignalARRR.Server.ClientManager>();
    await clientManager.WithHub<TestHub>()
        .SendAsync<TestShared.ITestClientMethods>(c => c.Nix());
    return Results.Ok("Sent");
});

// N-4: broadcast a call that carries a cancellation token; the token is cancelled by the
// companion endpoint below, so the test controls when — after it has seen the call arrive.
app.MapPost("/__test/broadcast-wait-with-token", async (HttpContext context) => {
    var clientManager = context.RequestServices.GetRequiredService<Cocoar.SignalARRR.Server.ClientManager>();
    var probeId = Guid.NewGuid().ToString("N");
    var cts = new CancellationTokenSource();
    IntegrationTestServer.BroadcastCancelProbes.Sources[probeId] = cts;

    await clientManager.WithHub<TestHub>()
        .SendAsync<TestShared.ITestClientMethods>(c => c.Wait(30, cts.Token));

    return Results.Ok(probeId);
});

app.MapPost("/__test/broadcast-wait-cancel", (HttpContext context) => {
    var probeId = context.Request.Query["probeId"].ToString();
    if (!IntegrationTestServer.BroadcastCancelProbes.Sources.TryRemove(probeId, out var cts)) {
        return Results.BadRequest($"No broadcast cancel probe '{probeId}'.");
    }

    cts.Cancel();
    cts.Dispose();
    return Results.Ok("cancelled");
});

// Typed broadcast with attribute filter
app.MapPost("/__test/broadcast-filtered-nix", async (HttpContext context) => {
    var tag = context.Request.Query["tag"].ToString();
    var clientManager = context.RequestServices.GetRequiredService<Cocoar.SignalARRR.Server.ClientManager>();
    var clients = string.IsNullOrWhiteSpace(tag)
        ? clientManager.WithHub<TestHub>()
        : clientManager.WithHub<TestHub>().WithAttribute("role", tag);

    await clients.SendAsync<TestShared.ITestClientMethods>(c => c.Nix());
    return Results.Ok("Sent");
});

// Check client groups (for testing group tracking)
app.MapGet("/__test/client-groups", (HttpContext context) => {
    var connectionId = context.Request.Query["connectionId"].ToString();
    if (string.IsNullOrWhiteSpace(connectionId)) return Results.BadRequest("Missing connectionId");

    var clientManager = context.RequestServices.GetRequiredService<Cocoar.SignalARRR.Server.ClientManager>();
    var client = clientManager.GetClientById(connectionId);
    return Results.Ok(client.Groups);
});

// Typed InvokeAllAsync — calls GetById on each client, returns all results
app.MapPost("/__test/invoke-all-getbyid", async (HttpContext context) => {
    var id = context.Request.Query["id"].ToString();
    var clientManager = context.RequestServices.GetRequiredService<Cocoar.SignalARRR.Server.ClientManager>();

    var results = await clientManager.WithHub<TestHub>()
        .InvokeAllAsync<TestShared.ITestClientMethods, string>(c => c.GetById(id));

    var items = new System.Collections.Generic.List<object>();
    foreach (var r in results) {
        items.Add(new { r.ClientId, r.Value });
    }
    return Results.Ok(items);
});

// Typed InvokeOneAsync — calls GetById on clients until one succeeds
app.MapPost("/__test/invoke-one-getbyid", async (HttpContext context) => {
    var id = context.Request.Query["id"].ToString();
    var clientManager = context.RequestServices.GetRequiredService<Cocoar.SignalARRR.Server.ClientManager>();

    var result = await clientManager.WithHub<TestHub>()
        .InvokeOneAsync<TestShared.ITestClientMethods, string>(c => c.GetById(id));

    return Results.Ok(new { result.ClientId, result.Value });
});

app.MapPost("/__test/broadcast-user-nix", async (HttpContext context) => {
    var userId = context.Request.Query["userId"].ToString();
    if (string.IsNullOrWhiteSpace(userId))
        return Results.BadRequest("Missing userId");

    var clientManager = context.RequestServices.GetRequiredService<Cocoar.SignalARRR.Server.ClientManager>();
    await clientManager.WithHub<TestHub>()
        .WithUser(userId)
        .SendAsync<TestShared.ITestClientMethods>(c => c.Nix());

    return Results.Ok("Sent");
});

app.MapPost("/__test/invoke-user-all-getbyid", async (HttpContext context) => {
    var userId = context.Request.Query["userId"].ToString();
    var id = context.Request.Query["id"].ToString();
    if (string.IsNullOrWhiteSpace(userId))
        return Results.BadRequest("Missing userId");

    var clientManager = context.RequestServices.GetRequiredService<Cocoar.SignalARRR.Server.ClientManager>();
    var results = await clientManager.WithHub<TestHub>()
        .WithUser(userId)
        .InvokeAllAsync<TestShared.ITestClientMethods, string>(c => c.GetById(id));

    var items = new System.Collections.Generic.List<object>();
    foreach (var r in results) {
        items.Add(new { r.ClientId, r.Value });
    }

    return Results.Ok(items);
});

app.MapPost("/__test/invoke-attribute-all-getbyid", async (HttpContext context) => {
    var tag = context.Request.Query["tag"].ToString();
    var id = context.Request.Query["id"].ToString();
    if (string.IsNullOrWhiteSpace(tag))
        return Results.BadRequest("Missing tag");

    var clientManager = context.RequestServices.GetRequiredService<Cocoar.SignalARRR.Server.ClientManager>();
    var results = await clientManager.WithHub<TestHub>()
        .WithAttribute("role", tag)
        .InvokeAllAsync<TestShared.ITestClientMethods, string>(c => c.GetById(id));

    var items = new System.Collections.Generic.List<object>();
    foreach (var r in results) {
        items.Add(new { r.ClientId, r.Value });
    }

    return Results.Ok(items);
});

app.MapGet("/__test/presence-all", async (HttpContext context) => {
    var clientManager = context.RequestServices.GetRequiredService<Cocoar.SignalARRR.Server.ClientManager>();
    var snapshots = await clientManager.GetConnectionsAsync<TestHub>();
    return Results.Ok(snapshots);
});

app.MapGet("/__test/presence-user", async (HttpContext context) => {
    var userId = context.Request.Query["userId"].ToString();
    if (string.IsNullOrWhiteSpace(userId))
        return Results.BadRequest("Missing userId");

    var clientManager = context.RequestServices.GetRequiredService<Cocoar.SignalARRR.Server.ClientManager>();
    var snapshots = await clientManager.GetConnectionsByUserAsync<TestHub>(userId);
    return Results.Ok(snapshots);
});

app.MapGet("/__test/presence-group", async (HttpContext context) => {
    var groupName = context.Request.Query["group"].ToString();
    if (string.IsNullOrWhiteSpace(groupName))
        return Results.BadRequest("Missing group");

    var clientManager = context.RequestServices.GetRequiredService<Cocoar.SignalARRR.Server.ClientManager>();
    var snapshots = await clientManager.GetConnectionsInGroupAsync<TestHub>(groupName);
    return Results.Ok(snapshots);
});

app.MapGet("/__test/presence-attribute", async (HttpContext context) => {
    var key = context.Request.Query["key"].ToString();
    var value = context.Request.Query["value"].ToString();
    if (string.IsNullOrWhiteSpace(key))
        return Results.BadRequest("Missing key");

    var clientManager = context.RequestServices.GetRequiredService<Cocoar.SignalARRR.Server.ClientManager>();
    var snapshots = await clientManager.GetConnectionsByAttributeAsync<TestHub>(key, string.IsNullOrWhiteSpace(value) ? null : value);
    return Results.Ok(snapshots);
});

app.MapGet("/__test/presence-online-users", async (HttpContext context) => {
    var clientManager = context.RequestServices.GetRequiredService<Cocoar.SignalARRR.Server.ClientManager>();
    var users = await clientManager.GetOnlineUsersAsync<TestHub>();
    return Results.Ok(users);
});

app.MapGet("/__test/presence-user-online", async (HttpContext context) => {
    var userId = context.Request.Query["userId"].ToString();
    if (string.IsNullOrWhiteSpace(userId))
        return Results.BadRequest("Missing userId");

    var clientManager = context.RequestServices.GetRequiredService<Cocoar.SignalARRR.Server.ClientManager>();
    var isOnline = await clientManager.IsUserOnlineAsync<TestHub>(userId);
    return Results.Ok(isOnline);
});

// ── Cross-assembly server-to-client push tests ──
// Uses ITestServerPushClient from Cocoar.SignalARRR.Tests.SharedModels (separate assembly)
// to test the real-world shared-contracts pattern.

app.MapSignalARRRTest("/__test/push-notification", (context, clientManager) => {
    var connectionId = context.Request.Query["connectionId"].ToString();
    var message = context.Request.Query["message"].ToString();
    if (string.IsNullOrWhiteSpace(connectionId))
        return Results.BadRequest("Missing connectionId");

    var typedClient = clientManager.GetTypedMethods<Cocoar.SignalARRR.Tests.SharedModels.ITestServerPushClient>(connectionId);
    typedClient.PushNotification(message);
    return "Sent";
});

app.MapSignalARRRTest("/__test/request-client-info", async (context, clientManager) => {
    var connectionId = context.Request.Query["connectionId"].ToString();
    if (string.IsNullOrWhiteSpace(connectionId))
        return Results.BadRequest("Missing connectionId");

    var typedClient = clientManager.GetTypedMethods<Cocoar.SignalARRR.Tests.SharedModels.ITestServerPushClient>(connectionId);
    var info = await typedClient.RequestClientInfo();
    return info;
});

app.MapSignalARRRTest("/__test/config-updated", (context, clientManager) => {
    var connectionId = context.Request.Query["connectionId"].ToString();
    var configJson = context.Request.Query["configJson"].ToString();
    if (string.IsNullOrWhiteSpace(connectionId))
        return Results.BadRequest("Missing connectionId");

    // Exact ConfigHub pattern: void ConfigUpdated(string? path, string configJson) with null first arg
    var typedClient = clientManager.GetTypedMethods<Cocoar.SignalARRR.Tests.SharedModels.ITestServerPushClient>(connectionId);
    typedClient.ConfigUpdated(null, configJson);
    return "Sent";
});

app.MapSignalARRRTest("/__test/push-notification-all", (context, clientManager) => {
    var message = context.Request.Query["message"].ToString();

    foreach (var client in clientManager.WithHub<TestHub>().LocalClients()) {
        var typedClient = client.GetTypedMethods<Cocoar.SignalARRR.Tests.SharedModels.ITestServerPushClient>();
        typedClient.PushNotification(message);
    }
    return "Sent to all";
});

await app.StartAsync();

var server = app.Services.GetRequiredService<IServer>();
var addresses = server.Features.Get<IServerAddressesFeature>()!;
var serverUrl = addresses.Addresses.First();

// Write the URL to stdout for the orchestration script to capture
Console.WriteLine($"SERVER_URL={serverUrl}");
Console.Out.Flush();

// Also write to a file if SERVER_URL_FILE env var is set
var urlFile = Environment.GetEnvironmentVariable("SERVER_URL_FILE");
if (!string.IsNullOrEmpty(urlFile)) {
    await File.WriteAllTextAsync(urlFile, serverUrl);
    WriteDiagnostics($"server-url-published url={serverUrl}");
}

await app.WaitForShutdownAsync();

internal sealed class QueryStringUserIdProvider : IUserIdProvider {
    public string? GetUserId(HubConnectionContext connection) {
        var userId = connection.GetHttpContext()?.Request.Query["userId"].ToString();
        if (!string.IsNullOrWhiteSpace(userId)) {
            return userId;
        }

        return connection.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? connection.User?.Identity?.Name;
    }
}
