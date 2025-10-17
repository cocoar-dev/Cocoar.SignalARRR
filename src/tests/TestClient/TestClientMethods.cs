using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using SignalARRR;
using SignalARRR.Attributes;
using SignalARRR.Client;
using TestShared;

namespace TestClient {

    [MessageName("ClientTest")]
    public class TestClientMethods: ITestClientMethods {


        public static int count = 0;
        public Task<DateTime> GetDate() {

            var dt = DateTime.Now;
            return Task.FromResult(dt);
        }

        public Task<Dictionary<string, object>> GetDictionary(DateTime dt) {
            
            var dict = new Dictionary<string, object>();
            dict.Add("Date", dt);
            dict.Add("Name", GetName());




            return Task.FromResult(dict);
        }

        public Task<T> InvokeAsync<T>(string command, Dictionary<string, object> variables = null) {
            Console.WriteLine(typeof(T));
            return default;
        }

        public T Invoke<T>(string command, Dictionary<string, object> variables = null) {


           

            Console.WriteLine(typeof(T));
            return default;
        }

        public void Nix() {
            throw new Exception("NIX Exception");
        }

        public List<string> GetContent(int count = 10) {


            Task.Delay(TimeSpan.FromSeconds(2)).GetAwaiter().GetResult();

            var chars = Enumerable.Range(0, char.MaxValue + 1)
                .Select(i => (char)i)
                .Where(c => !char.IsControl(c))
                .ToArray();

            var l = new List<string>();

            for (int i = 0; i < count; i++) {
                l.Add(string.Join("", chars));
            }

            return l;
        }

        public bool CreateObject(string className, Dictionary<string, object> properties) {

            return true;

        }

        public bool CreateObjectFromTemplate(string templateName, Dictionary<string, object> properties) {
            return true;
        }

        public long FileLength(string filename, Stream filestream) {

            if (File.Exists($"z:\\{filename}")) {
                File.Delete($"z:\\{filename}");
            }

            using var fs =  File.Create($"z:\\{filename}");
            filestream.CopyTo(fs);
            return filestream.Length;
        }

        public void Complex1(ComplexTestClass compl) {


            var c = compl;

        }

        public IncidentClass TestExpandableObject(IncidentClass expandableObject) {


            
            expandableObject["TestEnum"] = TestEnum.None;
            //expandableObject.TestEnum = TestEnum.Other;
            expandableObject["zusätzlich"] = DateTime.Now;

            return expandableObject;

        }

        public async Task<string> Wait(int seconds, CancellationToken cancellationToken) {

            try {
                await Task.Delay(seconds, cancellationToken);
                Console.WriteLine("OK");
                return "OK";
                
            } catch (Exception e) {
                Console.WriteLine(e.GetBaseException().Message);
                return e.GetBaseException().Message;
            }
            

            
        }

        public string GetByGenericId(Guid id) {
            Console.WriteLine(id.ToString());
            return id.ToString();
        }

        public string GetById(string id) {
            Console.WriteLine(id);
            return id;
        }

        public string GetName() {
            return $"{Environment.MachineName}\\{Environment.UserName}";
        }


        public void IchNICHT() {

        }
    }
}
