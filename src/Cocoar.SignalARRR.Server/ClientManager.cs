using System;
using System.Collections.Generic;
using System.Linq;
using Cocoar.Reflectensions.ExtensionMethods;

namespace Cocoar.SignalARRR.Server
{
    public class ClientManager {

        private IHARRRClientManager HARRRClientManager { get; }

        internal ClientManager(IHARRRClientManager harrrClientManager) {
            HARRRClientManager = harrrClientManager;

        }

        public ClientContext GetClientById(string id) {
            return HARRRClientManager.GetClient(id);
        }

        public IEnumerable<ClientContext> GetAllClients() {
            return HARRRClientManager.GetClients();
        }

        public IEnumerable<ClientContext> GetAllClients(Func<ClientContext, bool> predicate) {
            return GetAllClients().Where(predicate);
        }

        public IEnumerable<ClientContext> GetHARRRClients<T>() {
            return HARRRClientManager.GetClients().Where(c => c.HARRRType.Equals<T>());
        }

        public IEnumerable<ClientContext> GetHARRRClients<T>(Func<ClientContext, bool> predicate) {
            return GetHARRRClients<T>().Where(predicate);
        }
    }
}
