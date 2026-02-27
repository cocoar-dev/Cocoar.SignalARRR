using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Cocoar.SignalARRR.Server.ExtensionMethods;
using Cocoar.SignalARRR.Server.JsonConverters;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Configuration.UserSecrets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;
using Xunit;
using Cocoar.SignalARRR.IntegrationTests.Extensions;

namespace Cocoar.SignalARRR.IntegrationTests {
    public class SignalARRRServerInstanceFixture: IDisposable {


        IHost _host;
        public string ServerUrl { get; private set; }

        public SignalARRRServerInstanceFixture() {
             
            
            var hostBuilder = new HostBuilder()
                .ConfigureWebHost(webBuilder =>
                {
                    webBuilder
                        .UseKestrel()
                        .UseUrls("http://127.0.0.1:0") // Use random available port
                        .ConfigureServices(services =>
                        {

                            services.AddRouting();

                            services.AddMvc().AddNewtonsoftJson(options => {
                                options.SerializerSettings.Converters.Add(new IpAddressConverter());
                                options.SerializerSettings.Converters.Add(new StringEnumConverter());
                                options.SerializerSettings.ContractResolver = new DefaultContractResolver();
                            });

                            services.AddSignalR().AddNewtonsoftJsonProtocol(options =>
                                {
                                    options.PayloadSerializerSettings.ContractResolver = new DefaultContractResolver();
                                    options.PayloadSerializerSettings.Converters.Add(new StringEnumConverter());
                                    options.PayloadSerializerSettings.Converters.Add(new IpAddressConverter());
                                    options.PayloadSerializerSettings.ReferenceLoopHandling = ReferenceLoopHandling.Ignore;
                                });
                            services.AddSignalARRR(builder => builder
                                .AddServerMethodsFrom(typeof(TestHub).Assembly)
                            );
                            
                        })
                        .Configure(app =>
                        {

                            app.UseRouting();
                            app.UseEndpoints(endpoints =>
                            {
                                endpoints.MapHARRRController<TestHub>("/signalr/testhub");

                                // Minimal API test trigger: server -> client call via HTTP
                                endpoints.MapSignalARRRTest("/__test/trigger-client-call", async (context, _clientManager) =>
                                {
                                    var request = context.Request;
                                    var connectionId = request.Query["connectionId"].ToString();
                                    var method = request.Query["method"].ToString();
                                    var arg = request.Query["arg"].ToString();

                                    if (string.IsNullOrWhiteSpace(connectionId) || string.IsNullOrWhiteSpace(method))
                                    {
                                        return Results.BadRequest("Missing connectionId or method");
                                    }

                                    // Resolve hub context to talk to connected clients
                                    var hubContext = context.RequestServices.GetRequiredService<Microsoft.AspNetCore.SignalR.IHubContext<TestHub>>();

                                    // Build a SignalARRR server request message targeting client interface methods
                                    var msg = new Cocoar.SignalARRR.Common.ServerRequestMessage(method, string.IsNullOrEmpty(arg) ? Array.Empty<object>() : new object[] { arg });

                                    // Fire-and-forget: send a server message to the client (no awaited result)
                                    await hubContext.Clients.Client(connectionId)
                                        .SendCoreAsync(Cocoar.SignalARRR.Common.Constants.MethodNames.InvokeServerMessage, new object[] { msg }, default);

                                    return "Sent";
                                });

                                // Minimal API test trigger: server cancels client's CancellationToken
                                endpoints.MapSignalARRRTest("/__test/trigger-client-cancellation", async (context, clientManager) =>
                                {
                                    var request = context.Request;
                                    var connectionId = request.Query["connectionId"].ToString();
                                    var delayMs = int.TryParse(request.Query["delayMs"].ToString(), out var d) ? d : 200;

                                    if (string.IsNullOrWhiteSpace(connectionId))
                                    {
                                        return Results.BadRequest("Missing connectionId");
                                    }

                                    var cts = new CancellationTokenSource();
                                    var typedClient = clientManager.GetTypedMethods<TestShared.ITestClientMethods>(connectionId);

                                    // Start the Wait call and cancel after delay
                                    var waitTask = typedClient.Wait(30, cts.Token);
                                    await Task.Delay(delayMs);
                                    cts.Cancel();

                                    try
                                    {
                                        await waitTask;
                                        return (object)"completed";
                                    }
                                    catch (Exception)
                                    {
                                        // Expected: cancellation causes InvokeCoreAsync to abort
                                        return (object)"cancelled";
                                    }
                                });

                                // Minimal API test trigger: server requests stream from client
                                endpoints.MapSignalARRRTest("/__test/trigger-client-stream", async (context, clientManager) =>
                                {
                                    var request = context.Request;
                                    var connectionId = request.Query["connectionId"].ToString();
                                    var count = int.TryParse(request.Query["count"].ToString(), out var c) ? c : 5;

                                    if (string.IsNullOrWhiteSpace(connectionId))
                                    {
                                        return Results.BadRequest("Missing connectionId");
                                    }

                                    var typedClient = clientManager.GetTypedMethods<TestShared.ITestClientMethods>(connectionId);
                                    var items = new List<int>();
                                    await foreach (var item in typedClient.StreamNumbers(count))
                                    {
                                        items.Add(item);
                                    }

                                    return (object)items;
                                });

                                // Minimal API test trigger: server -> client typed call using SignalARRR typed methods
                                endpoints.MapSignalARRRTest("/__test/trigger-client-typed-call", (context, clientManager) =>
                                {
                                    var request = context.Request;
                                    var connectionId = request.Query["connectionId"].ToString();

                                    if (string.IsNullOrWhiteSpace(connectionId))
                                    {
                                        return Results.BadRequest("Missing connectionId");
                                    }

                                    // Use helper to get typed proxy for the specific client (throws if not found)
                                    var typedClient = clientManager.GetTypedMethods<TestShared.ITestClientMethods>(connectionId);
                                    typedClient.Nix();

                                    return "Sent";
                                });
                            });

                        });

                    
                });

                    
                _host = hostBuilder.Start();
                
                // Get the actual URL that the server is listening on
                var addresses = _host.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>();
                ServerUrl = addresses!.Addresses.First();

        }

        public IHost GetHost() {
            return _host;
        }

        public void Dispose()
        {
            _host?.StopAsync().GetAwaiter().GetResult();
            _host?.Dispose();
        }
    }

    [CollectionDefinition("Simple")]
    public class SimpleSignalARRCollection : ICollectionFixture<SignalARRRServerInstanceFixture> {

    }
}
