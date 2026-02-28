using System;
using Cocoar.SignalARRR.Common;

namespace Cocoar.SignalARRR.Client {
    public class ServerRequestEventArgs : EventArgs {

        public ServerRequestMessage ServerRequestMessage { get; }

        public ServerRequestEventArgs(ServerRequestMessage serverRequestMessage) {
            ServerRequestMessage = serverRequestMessage;
        }
    }
}
