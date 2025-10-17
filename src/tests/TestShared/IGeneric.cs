using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace TestShared {
    public interface IGeneric {

        Task<T> InvokeAsync<T>(string command, Dictionary<string, object> variables = null);
        T Invoke<T>(string command, Dictionary<string, object> variables = null);
    }
}
