using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace TestServer {
    public class ConsoleWriter {

        public void WriteInfo(string text) {

            Console.WriteLine($"[{DateTime.Now}] {text}");
        }
    }
}
