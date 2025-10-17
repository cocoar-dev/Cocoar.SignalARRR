using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace TestServer {
    public class ConsoleWriter2 {

        public void WriteInfo(string text) {

            Console.WriteLine($"[{DateTime.Now}](2) {text}");
        }
    }
}
