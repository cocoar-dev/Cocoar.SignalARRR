using System;
using System.Collections.Generic;

namespace Cocoar.SignalARRR.Server.ExtensionMethods {
    public static class ClientManagerTypedExtensions {
        /// <summary>
        /// Get typed client methods proxy for a specific connection id.
        /// Throws InvalidOperationException when the client is not found.
        /// </summary>
        public static T GetTypedMethods<T>(this ClientManager clientManager, string connectionId) where T : class {
            if (clientManager == null) throw new ArgumentNullException(nameof(clientManager));
            if (string.IsNullOrWhiteSpace(connectionId)) throw new ArgumentNullException(nameof(connectionId));

            var ctx = clientManager.GetClientById(connectionId);
            if (ctx == null) throw new InvalidOperationException($"Client not found: {connectionId}");

            return ctx.GetTypedMethods<T>();
        }

        /// <summary>
        /// Enumerate all connected clients and their typed method proxies.
        /// </summary>
        public static IEnumerable<(ClientContext Context, T Methods)> GetAllTypedMethods<T>(this ClientManager clientManager) where T : class {
            if (clientManager == null) throw new ArgumentNullException(nameof(clientManager));

            foreach (var ctx in clientManager.GetAllClients()) {
                yield return (ctx, ctx.GetTypedMethods<T>());
            }
        }

        /// <summary>
        /// Enumerate connected clients for a specific hub type and their typed method proxies.
        /// </summary>
        public static IEnumerable<(ClientContext Context, T Methods)> GetTypedMethodsForHub<T, THub>(this ClientManager clientManager)
            where T : class
            where THub : HARRR {
            if (clientManager == null) throw new ArgumentNullException(nameof(clientManager));

            foreach (var ctx in clientManager.WithHub<THub>()) {
                yield return (ctx, ctx.GetTypedMethods<T>());
            }
        }
    }
}
