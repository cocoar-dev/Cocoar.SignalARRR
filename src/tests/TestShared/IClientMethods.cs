using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace TestShared {
    public interface ITestClientMethods {

        //string GetName();

        //Task<DateTime> GetDate();

        //Task<Dictionary<string, object>> GetDictionary(DateTime dt);

        //Task<T> InvokeAsync<T>(string command, Dictionary<string, object> variables = null);
        T Invoke<T>(string command, Dictionary<string, object> variables = null);

        void Nix();

        List<string> GetContent(int count);

        bool CreateObject(string className, Dictionary<string, object> properties);

        bool CreateObjectFromTemplate(string templateName, Dictionary<string, object> properties);

        long FileLength(string id, Stream filestream);

        void Complex1(ComplexTestClass compl);

        IncidentClass TestExpandableObject(IncidentClass expandableObject);

        Task<string> Wait(int seconds, CancellationToken cancellationToken);

        string GetByGenericId(Guid id);
        string GetById(string id);
    }
}
