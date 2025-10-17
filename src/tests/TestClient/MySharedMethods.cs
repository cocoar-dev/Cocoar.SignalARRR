using System;
using System.Collections.Generic;
using System.Text;
using TestShared;

namespace TestClient {
    public class MySharedMethods: ISharedMethods {
        public string Name { get; }
        public DateTime StartDateTime { get; set; } = new DateTime(2020,1,1);
        public IStringMethods MyStringMethods { get; } = new StringMethods();
        public IntegerMethods MyIntegerMethods { get; } = new IntegerMethods();

        public MySharedMethods() {
            Name = "Bernhard Windisch";
        }

        public DateTime GetCurrentDateTime() {
            return DateTime.Now;
        }

        public List<string> GetStrings() {
            throw new NotImplementedException();
        }
    }
}
