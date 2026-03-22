using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Cocoar.SignalARRR.Common.Attributes;
using Cocoar.SignalARRR.Server;
using Microsoft.AspNetCore.Mvc;
using TestShared;

namespace IntegrationTestServer {

    /// <summary>
    /// Second ServerMethods class on the same hub — tests multi-class organization,
    /// [MessageName] attribute, complex types, and various parameter/return type combinations.
    /// </summary>
    public class ExtraMethods : ServerMethods<TestHub> {

        // Basic multi-class tests
        public string Greet(string name) => $"Hello, {name}!";

        public Task<int> Add(int a, int b) => Task.FromResult(a + b);

        [MessageName("CustomEcho")]
        public string EchoWithCustomName(string input) => input;

        // Complex object round-trip
        public ComplexTestClass EchoComplex(ComplexTestClass input) => input;

        // DateTime serialization
        public string FormatDate(DateTime date) => date.ToString("yyyy-MM-dd");

        // Guid parameter
        public string GuidToString(Guid id) => id.ToString();

        // List return
        public List<string> GenerateItems(int count) {
            var items = new List<string>();
            for (int i = 0; i < count; i++) items.Add($"item-{i}");
            return items;
        }

        // Dictionary return
        public Dictionary<string, int> WordLengths(string sentence) {
            var result = new Dictionary<string, int>();
            foreach (var word in sentence.Split(' ')) {
                result[word] = word.Length;
            }
            return result;
        }

        // Multiple parameters of different types
        public string Combine(string text, int number, bool flag) =>
            $"{text}-{number}-{flag}";

        // [FromServices] injection — IServiceProvider is injected by DI, not by the client
        public string GetServiceInfo([FromServices] IServiceProvider sp) =>
            sp != null ? "ServiceProviderInjected" : "null";

        // Receives a Stream argument (uploaded via HTTP by the client)
        public string ReadStreamContent(System.IO.Stream data) {
            using var reader = new System.IO.StreamReader(data);
            return reader.ReadToEnd();
        }

        // Throws a specific exception for error handling tests
        public string ThrowArgumentException(string paramName) =>
            throw new ArgumentException("Invalid value provided", paramName);

        public string ThrowInvalidOperation() =>
            throw new InvalidOperationException("This operation is not allowed");
    }
}
