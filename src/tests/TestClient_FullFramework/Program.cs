using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SignalARRR;
using SignalARRR.Client;

namespace TestClient_FullFramework
{
    class Program
    {
        static HARRRConnection connection;

        static async Task Main(string[] args) 
            {
            connection = HARRRConnection.Create(builder => builder
                .WithUrl("http://localhost.:5000/signalr/testhub", options => {
                    options.Headers["#tag"] = "bpk";
                    options.Headers["#Hostname"] = Environment.MachineName;
                })
                .ConfigureLogging(log => {
                log.AddConsole();
                log.SetMinimumLevel(LogLevel.Debug);
            }));


            await connection.StartAsync();


            connection.On<string>("test", s => Console.WriteLine(s));
            //connection.OnServerRequest("GetDate", (Dictionary<string, string> par) => {
            //    //Task.Delay(3000).GetAwaiter().GetResult();
            //    return new {
            //        Date = DateTime.Now,
            //        Framework = ".Net Framework",
            //        Name = "Bernhard"
            //    };
            //});

            var tm = new TestClientMethods(DateTime.Now);
            //connection.RegisterClientMethods( tm, "scsm.");


            await ConsoleHelper.RegisterNewLineHandlerAsync();

            await connection.StopAsync();

        }
    }

    public class TestClientMethods {
        private readonly DateTime _dateTime;

        public TestClientMethods(DateTime dateTime) {
            _dateTime = dateTime;
        }


        public List<object> GetDate(object dateTime) {

            var l = new List<object>();
            l.Add(_dateTime);
            l.Add(dateTime);
            l.Add(DateTime.Now);

            return l;
        }

    }
}
