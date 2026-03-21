using System.Text.Json;
using System.Text.Json.Serialization;
using Cocoar.SignalARRR.Server.ExtensionMethods;
using IntegrationTestServer;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.UseUrls("http://127.0.0.1:0");

builder.Services.AddSignalR().AddJsonProtocol(options => {
    options.PayloadSerializerOptions.PropertyNamingPolicy = null;
    options.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

builder.Services.AddSignalARRR(b => b.AddServerMethodsFrom(typeof(TestHub).Assembly));

var app = builder.Build();

app.MapHARRRController<TestHub>("/signalr/testhub");

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
}

await app.WaitForShutdownAsync();
