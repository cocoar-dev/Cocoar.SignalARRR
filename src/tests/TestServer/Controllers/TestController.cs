using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Cocoar.Reflectensions.Common;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Newtonsoft.Json.Linq;
using SignalARRR;
using SignalARRR.Server;
using SignalARRR.Server.ExtensionMethods;
using TestShared;

namespace TestServer.Controllers {
    [Route("api/test")]
    public class TestController : Controller {


        private ClientManager ClientManager { get; }

        public TestController(ClientManager clientManager) {
            ClientManager = clientManager;
        }


        private void ProxyScsmClientMethod(Action<ITestClientMethods> action) {
            var cl1 = ClientManager.GetAllClients().FirstOrDefault();

            if (cl1 == null)
                throw new Exception("No client found!");

            HttpContext.ProxyFromHARRRClient(cl1, action);
        }
        

        [HttpGet("guid/{id}")]
        public async Task GetChangeRequestById(string id) {
            if (id.IsGuid()) {
                var _id = id.ToGuid();
                ProxyScsmClientMethod(methods => methods.GetByGenericId(_id));
            } else {
                ProxyScsmClientMethod(methods => methods.GetById(id));
            }


        }

    }
}
