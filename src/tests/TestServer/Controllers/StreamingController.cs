using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using SignalARRR.Server;
using SignalARRR.Server.ExtensionMethods;
using TestShared;

namespace TestServer.Controllers {

    [Route("api/stream")]
    public class StreamingController : Controller {

        private ClientManager ClientManager { get; }
        public StreamingController(ClientManager clientManager) {
            ClientManager = clientManager;
        }



        [HttpGet("{count}")]
        public async Task Stream(int count) {


            HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>().MaxRequestBodySize = null;
            //var cl2 = ClientManager
            //    .GetAllClients()
            //    .FirstOrDefault()?.GetTypedMethods<ITestClientMethods>("ClientTest");

            var cl = ClientManager
                .GetAllClients()
                .FirstOrDefault();

            if (cl == null)
                await this.HttpContext.NotFound();

            var dict = new Dictionary<string, object>();
            dict["test"] = "TestValue";

            HttpContext.ProxyFromHARRRClient<ITestClientMethods>(cl, methods => methods.Invoke<DateTime>("comm", dict));

        }

        [HttpPost("push/{filename}")]
        public async Task<IActionResult> Push(string filename) {

            //var cl2 = ClientManager
            //    .GetAllClients()
            //    .FirstOrDefault()?.GetTypedMethods<ITestClientMethods>("ClientTest");

            var cl = ClientManager
                .GetAllClients()
                .FirstOrDefault();

            if (cl == null)
                await this.HttpContext.NotFound();

            var length = cl.GetTypedMethods<ITestClientMethods>().FileLength(filename, HttpContext.Request.Body);

            return Ok(length);
        }

        [HttpPost("wait/{seconds}")]
        public async Task<IActionResult> Wait(int seconds) {

            //var cl2 = ClientManager
            //    .GetAllClients()
            //    .FirstOrDefault()?.GetTypedMethods<ITestClientMethods>("ClientTest");

            var cl = ClientManager
                .GetAllClients()
                .FirstOrDefault();

            if (cl == null)
                await this.HttpContext.NotFound();

            var length = await cl.GetTypedMethods<ITestClientMethods>().Wait(seconds, HttpContext.RequestAborted);

            return Ok(length);
        }
    }

}
