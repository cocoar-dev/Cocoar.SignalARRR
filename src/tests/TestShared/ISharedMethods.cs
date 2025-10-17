using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace TestShared {
    public interface ISharedMethods {

        //string Name { get; }

        //DateTime StartDateTime { get; set; }

        //IStringMethods MyStringMethods { get; }

        //IntegerMethods MyIntegerMethods { get; }

        DateTime GetCurrentDateTime();

        List<string> GetStrings();
    }


    public interface IStringMethods {

        string Reverse(string value);
    }

    public class IntegerMethods {
        public int Double(int value) {
            return value * 2;
        }
    }

    public class StringMethods : IStringMethods {
        public string Reverse(string value) {
            return new string(value.Reverse().ToArray());
        }
    }



    
}
