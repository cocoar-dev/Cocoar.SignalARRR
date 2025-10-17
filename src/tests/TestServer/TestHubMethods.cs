using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Channels;
using System.Threading.Tasks;
using SignalARRR.Attributes;

namespace TestServer
{
    public partial class TestHub
    {

        public void WriteLine(string line)
        {
            Console.WriteLine(line);
        }

        //[MessageName("GibMirDatum")]
        public string GetDate()
        {
            return $"{DateTime.Now} -- From Hub";
        }

        public async Task Ping()
        {

            Console.WriteLine($"Get Ping from {ClientContext.RemoteIp}");
            await Task.Delay(2000);
            Console.WriteLine($"Get Ping2 from {ClientContext.RemoteIp}");
        }

        

    }
}
