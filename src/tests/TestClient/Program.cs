using System;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SignalARRR.Client;
using TestShared;

namespace TestClient {
    class Program {
        static HARRRConnection connection;

        static async Task Main(string[] args) {

            connection = HARRRConnection.Create(
                builder => builder
                    .WithUrl("http://localhost.:5000/signalr/testhub", options => {
                        options.Headers["#tag"] = "bpk";
                        options.Headers["#Hostname"] = Environment.MachineName;
                        options.Transports = HttpTransportType.WebSockets;
                    })
                    .AddJsonProtocol(options => {
                        options.PayloadSerializerOptions.PropertyNamingPolicy = null;
                        options.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter());
                        options.PayloadSerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
                    })
                    .ConfigureLogging(log => {
                        log.AddConsole();
                        log.SetMinimumLevel(LogLevel.Error);
                    }),
                optionsBuilder => optionsBuilder.UseHttpResponse()
                );


            connection.RegisterType<ISharedMethods, MySharedMethods>();

            await connection.StartAsync();


            connection.On<string>("test", s => Console.WriteLine(s));
            connection.OnServerRequest("GetDate", (par) => {
                return new {
                    Date = DateTime.Now,
                    Framework = ".Net Core",
                    Name = "Bernhard"
                };
            });



            var testHubClient = connection.GetTypedMethods<ITestHub>();
            var testHubClient2 = connection.GetTypedMethods<ITestHub>();
            var testHubClient3 = connection.GetTypedMethods<ITestHub>();

            var channelCounter = new ChannelCounter(connection);
            var channelCounterTask = new ChannelCounterTask(connection);
            var asyncEnumerableCounter = new AsyncEnumerableCounter(connection);
            var asyncEnumerableCounterTask = new AsyncEnumerableCounterTask(connection);
            var observableCounter = new ObservableCounter(connection);
            var observableCounterTask = new ObservableCounterTask(connection);

            ConsoleHelper.PressedKey.Subscribe(keyInfo => {

                switch (keyInfo.KeyChar) {
                    case 'p': {
                            connection.SendAsync("Ping");
                            Console.WriteLine("Finsihed");
                            break;
                        }
                    case 'ü': {
                            connection.SendAsync("Test1.Ping");
                            break;
                        }

                    case 'd': {
                            try {
                                var dt = connection.InvokeAsync<object>("Test1.GibMirDatum").GetAwaiter().GetResult();
                                Console.WriteLine(dt);
                            } catch (Exception e) {
                                Console.WriteLine(e);

                            }

                            break;
                        }

                    case 'f': {
                            try {
                                var dt = connection.InvokeAsync<object>("GetDate").GetAwaiter().GetResult();
                                Console.WriteLine(dt);
                            } catch (Exception e) {
                                Console.WriteLine(e);

                            }

                            break;
                        }

                    case 'g': {

                            try {
                                var g = Guid.NewGuid();


                                var stg = testHubClient3.StringToGuid(g).GetAwaiter().GetResult();
                                Console.WriteLine($"From Typed Client: {stg}");

                                testHubClient.Ping();

                                var dt2 = testHubClient.GetDate2(DateTime.Now.AddHours(10));
                                Console.WriteLine(dt2);

                            } catch (Exception e) {
                                Console.WriteLine(e);

                            }

                            break;

                        }

                    case 'm': {
                            connection.SendAsync("WriteLine", "Schau ma mal.....").GetAwaiter().GetResult();
                            break;
                        }

                    case '1': {
                            _ = channelCounter.StartAsync();
                            break;
                        }

                    case 'q': {
                            channelCounter.Stop();
                            break;
                        }

                    case '2': {
                            _ = channelCounterTask.StartAsync();
                            break;
                        }

                    case 'w': {
                            channelCounterTask.Stop();
                            break;
                        }

                    case '3': {
                            _ = asyncEnumerableCounter.StartAsync();
                            break;
                        }

                    case 'e': {
                            asyncEnumerableCounter.Stop();
                            break;
                        }

                    case '4': {
                            _ = asyncEnumerableCounterTask.StartAsync();
                            break;
                        }

                    case 'r': {
                            asyncEnumerableCounterTask.Stop();
                            break;
                        }

                    case '5': {
                            _ = observableCounter.StartAsync();
                            break;
                        }

                    case 't': {
                            observableCounter.Stop();
                            break;
                        }

                    case '6': {
                            _ = observableCounterTask.StartAsync();
                            break;
                        }

                    case 'z': {
                            observableCounterTask.Stop();
                            break;
                        }

                    case 'x': {
                            try {
                                var dm = new DummyClass();
                                dm.Name = "Bernhard";
                                dm.Timestamp = DateTime.Now;

                                var res = connection.InvokeAsync<DummyClass>("Test1.GetDummyOrException", dm).GetAwaiter().GetResult();
                                Console.WriteLine(res.Name);
                            } catch (Exception e) {
                                Console.WriteLine(e);

                            }
                            break;

                        }

                    case 'y': {
                            try {
                                var dm = new DummyClass();
                                dm.Name = "Bernhard";
                                dm.Timestamp = DateTime.Now;
                                dm.Year = dm.Timestamp.Year + 10;

                                var res = connection.InvokeAsync<DummyClass>("Test1.GetDummyOrException", dm).GetAwaiter().GetResult();
                                Console.WriteLine(res.Name);
                            } catch (Exception e) {
                                Console.WriteLine(e);

                            }
                            break;

                        }
                }


            });


            await ConsoleHelper.RegisterNewLineHandlerAsync();

            await connection.StopAsync();
        }

        static object GetDate() {
            return new { Date = DateTime.Now, Name = "Bernhard" };
        }


    }
}
