using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using SignalARRR.Server;

namespace TestServer.Controllers {

    [Route("api/clients")]
    public class ClientController: Controller {
        public ClientController(ClientManager clientManager) {
            ClientManager = clientManager;
        }

        private ClientManager ClientManager { get; }


        [HttpGet]
        public IActionResult GetConnectedClients() {

            var cls = ClientManager.GetAllClients();
            return Ok(cls);
        }
    }
}
