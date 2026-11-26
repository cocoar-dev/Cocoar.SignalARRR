using System.Text.Json.Serialization;
using Cocoar.SignalARRR.AotSmokeServer;
using Cocoar.SignalARRR.Server;
using Cocoar.SignalARRR.Server.ExtensionMethods;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;

var builder = WebApplication.CreateSlimBuilder(args);
builder.WebHost.UseUrls("http://127.0.0.1:0");
builder.Logging.SetMinimumLevel(LogLevel.Warning);

builder.Services.AddSignalR()
    .AddJsonProtocol(o => o.PayloadSerializerOptions.TypeInfoResolverChain.Insert(0, SmokeJson.Default));
builder.Services.AddAuthentication();
builder.Services.AddAuthorization(o => o.AddPolicy("Nobody", p => p.RequireAssertion(_ => false)));
builder.Services.AddSingleton<SmokeGreeting>();
builder.Services.AddSignalARRR(b => b.AddServerMethodsFrom(typeof(SmokeHub).Assembly));

var app = builder.Build();

app.MapSignalARRRHub<SmokeHub>("/signalr/testhub");

// Server-to-client calls, triggered over HTTP like the IntegrationTestServer's test endpoints.
app.MapPost("/__test/push-notification", (HttpContext context, ClientManager clientManager) => {
    var client = clientManager.GetTypedMethods<Cocoar.SignalARRR.Tests.SharedModels.ITestServerPushClient>(context.Request.Query["connectionId"].ToString());
    client.PushNotification(context.Request.Query["message"].ToString());
    return "Sent";
});
app.MapPost("/__test/config-updated", (HttpContext context, ClientManager clientManager) => {
    var client = clientManager.GetTypedMethods<Cocoar.SignalARRR.Tests.SharedModels.ITestServerPushClient>(context.Request.Query["connectionId"].ToString());
    client.ConfigUpdated(null, context.Request.Query["configJson"].ToString());
    return "Sent";
});
app.MapPost("/__test/request-client-info", async (HttpContext context, ClientManager clientManager) => {
    var client = clientManager.GetTypedMethods<Cocoar.SignalARRR.Tests.SharedModels.ITestServerPushClient>(context.Request.Query["connectionId"].ToString());
    return await client.RequestClientInfo();
});

await app.StartAsync();
var serverUrl = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
Console.WriteLine($"SERVER_URL={serverUrl}");
Console.Out.Flush();
await app.WaitForShutdownAsync();

/// <summary>The app's own wire types; SignalARRR brings its envelope types itself.</summary>
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(int))]
[JsonSerializable(typeof(bool))]
[JsonSerializable(typeof(Guid))]
internal partial class SmokeJson : JsonSerializerContext { }
