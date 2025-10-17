using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;
using Serilog;
using SignalARRR.Server;
using SignalARRR.Server.ExtensionMethods;
using SignalARRR.Server.JsonConverters;
using TestServer.LocalTokenAuthenticatonHandler;
using TestShared;

namespace TestServer
{
    public class Startup
    {
        // This method gets called by the runtime. Use this method to add services to the container.
        // For more information on how to configure your application, visit https://go.microsoft.com/fwlink/?LinkID=398940
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddMvc().AddNewtonsoftJson(options => {
                options.SerializerSettings.Converters.Add(new IpAddressConverter());
                options.SerializerSettings.Converters.Add(new StringEnumConverter());
                options.SerializerSettings.ContractResolver = new DefaultContractResolver();
            });

            //services.AddAuthentication("AccessToken").AddTestTokenValidation();

            services.AddSignalR().AddNewtonsoftJsonProtocol(options =>
                {
                    options.PayloadSerializerSettings.ContractResolver = new DefaultContractResolver();
                    options.PayloadSerializerSettings.Converters.Add(new StringEnumConverter());
                    options.PayloadSerializerSettings.Converters.Add(new IpAddressConverter());
                    options.PayloadSerializerSettings.ReferenceLoopHandling = ReferenceLoopHandling.Ignore;
                })
                .AddMessagePackProtocol();

            services.AddSignalARRR(builder => builder
                .PreBuiltClientMethods<ITestClientMethods>());

            services.AddSingleton<ConsoleWriter>();
            services.AddSingleton<ConsoleWriter2>();

            //services.AddAuthorization((options) => {
            //    options.AddPolicy("TestPolicy1", policy => {
            //        policy.AddAuthenticationSchemes("AccessToken");
            //        policy.RequireAuthenticatedUser();
            //        policy.RequireRole("testrole");
            //    });
            //});
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            app.UseSerilogRequestLogging();

            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

            app.UseRouting();

            //app.UseAuthentication();
            //app.UseAuthorization();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapHARRRController<TestHub>("/signalr/testhub");

                endpoints.MapControllers();

                endpoints.MapGet("/", async context =>
                {
                    await context.Response.WriteAsync("Hello World!");
                });
            });
        }
    }
}
