using System;
using Microsoft.AspNetCore.SignalR;

namespace Cocoar.SignalARRR.Server {
    public class HARRRException: HubException {

        public HARRRException(Exception exception): this(exception.GetBaseException().GetType().FullName, exception.GetBaseException().Message) {

        }

        public HARRRException(string type, string message): base($"[{type}] {message}") {

        }
    }
}
