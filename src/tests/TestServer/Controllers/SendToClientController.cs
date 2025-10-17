using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Newtonsoft.Json.Linq;
using SignalARRR;
using SignalARRR.Server;
using SignalARRR.Server.ExtensionMethods;
using TestShared;

namespace TestServer.Controllers {
    [Route("api/sendtoclient")]
    public class SendToClientController : Controller {


        private ClientManager ClientManager { get; }

        public SendToClientController(ClientManager clientManager) {
            ClientManager = clientManager;
        }

        [HttpPost]
        public async Task<IActionResult> SendToClient([FromBody] JToken query) {


            var result = await ClientManager
                .GetAllClients()
                .WithAttribute("Tag", "BPK")
                .InvokeOneAsync<object>("GetDate", new[] { query }, HttpContext.RequestAborted);

            return Ok(result);
        }
        [HttpPost("all")]
        public async Task<IActionResult> SendToAll([FromBody] JToken query) {


            var result = await ClientManager
                .GetAllClients()
                .WithAttribute("Tag", "BPK")
                .InvokeAllAsync<object>("scsm.GetDate", new[] { query }, HttpContext.RequestAborted);

            return Ok(result);
        }

        //[HttpPost("name")]
        //public async Task<IActionResult> GetName() {


        //    var cl = ClientManager
        //        .GetAllClients()
        //        .FirstOrDefault()?.GetTypedMethods<ITestClientMethods>("ClientTest");

        //    if (cl == null)
        //        return NotFound();




        //    return Ok(await cl.GetDictionary(DateTime.Now));
        //}


        [HttpGet("generic")]
        public async Task<IActionResult> GetGeneric() {


            var cl = ClientManager
                .GetAllClients()
                .FirstOrDefault()?.GetTypedMethods<ITestClientMethods>();

            if (cl == null)
                return NotFound();

            var res = cl.Invoke<DateTime>("test");


            return Ok(res);
        }


        [HttpPost("{className}")]
        public async Task<IActionResult> CreateObject(string className,
            [FromBody] Dictionary<string, object> properties) {

            var cl1 = ClientManager.GetAllClients().FirstOrDefault();

            if (cl1 == null)
                throw new Exception("No client found!");



            var res = cl1.GetTypedMethods<ITestClientMethods>().CreateObject(className, properties);
            //InvokeScsmProxyClient(methods => methods.CreateObject(className, properties));
            return Ok(res);
        }

        [HttpGet("complex1")]
        public async Task<IActionResult> ComplexType1() {

            var cl1 = ClientManager.GetAllClients().FirstOrDefault();

            if (cl1 == null)
                throw new Exception("No client found!");


            var ct = new ComplexTestClass();
            ct.Name = "Bernhard";
            ct.Age = 99;
            ct.Ok = true;
            ct.Timestamp = DateTime.Now;
            ct.Properties = new Dictionary<string, object> {
                ["Dog1"] = "Maggi",
                ["Dog2"] = "Wilson",
                ["Wife"] = true,
                ["Child"] = 1
            };

            cl1.GetTypedMethods<ITestClientMethods>().Complex1(ct);
            //InvokeScsmProxyClient(methods => methods.CreateObject(className, properties));
            return Ok();
        }

        [HttpGet("expandable")]
        public async Task<IActionResult> ExpandableObject1() {

            
           
            var ct = new IncidentClass();
            ct.Id = Guid.NewGuid().ToString();
            ct.TestEnum = TestEnum.Last;

            ct["Dog1"] = "Maggi";
            ct["Dog2"] = "Wilson";

            var dict = new Dictionary<string, object>(ct.GetProperties());


            var cl1 = ClientManager.GetAllClients().FirstOrDefault();

            if (cl1 == null)
                throw new Exception("No client found!");


            var res = cl1.GetTypedMethods<ITestClientMethods>().TestExpandableObject(ct);
            //InvokeScsmProxyClient(methods => methods.CreateObject(className, properties));
            return Ok(res);
        }

    }
}
