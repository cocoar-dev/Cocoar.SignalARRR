using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Cocoar.SignalARRR.Server.ExtensionMethods;
using Cocoar.SignalARRR.Server.JsonConverters;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration.UserSecrets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;
using Xunit;

namespace Cocoar.SignalARRR.IntegrationTests {
    public class SignalARRRServerInstanceFixture: IDisposable {


        IHost _host;

        public SignalARRRServerInstanceFixture() {
             
            
            var hostBuilder = new HostBuilder()
                .ConfigureWebHost(webBuilder =>
                {
                    webBuilder
                        .UseTestServer()
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
                            });

                        });

                    
                });

                    
                _host = hostBuilder.Start();

        }

        public IHost GetHost() {
            return _host;
        }

        public void Dispose()
        {
            _host.Dispose();
        }
    }

    [CollectionDefinition("Simple")]
    public class SimpleSignalARRCollection : ICollectionFixture<SignalARRRServerInstanceFixture> {

    }
}
