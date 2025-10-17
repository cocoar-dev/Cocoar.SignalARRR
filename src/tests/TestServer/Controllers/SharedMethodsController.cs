using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using SignalARRR.Server;
using TestShared;

namespace TestServer.Controllers {
    [Route("api/shared")]
    public class SharedMethodsController : Controller {


        private ClientManager ClientManager { get; }

        public SharedMethodsController(ClientManager clientManager) {
            ClientManager = clientManager;
        }

        
        [HttpGet]
        public async Task<IActionResult> Test1() {

           
            var cl = ClientManager.GetAllClients().FirstOrDefault()?.GetTypedMethods<ISharedMethods>();

            if (cl == null)
                return BadRequest("No client");
            
            
            return Ok(cl.GetCurrentDateTime());
        }

    }
    
}
