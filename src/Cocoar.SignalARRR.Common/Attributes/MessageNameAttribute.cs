using System;

namespace Cocoar.SignalARRR.Common.Attributes {
    // Interface is here for contract types: on the interface path the wire name is
    // 'interface|method', and both halves have to be renameable independently of the C# identifiers
    // — moving an interface to another namespace breaks TypeScript and Swift clients exactly as a
    // method rename does.
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Interface | AttributeTargets.Method, AllowMultiple = false)]
    public class MessageNameAttribute : Attribute {
        public string Name { get; }

        public MessageNameAttribute(string @namespace) {
            Name = @namespace;
        }

    }
}
