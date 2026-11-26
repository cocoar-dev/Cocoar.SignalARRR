using System;
using System.Threading.Tasks;

namespace Cocoar.SignalARRR.Server {
    /// <summary>
    /// Blocks on an asynchronous client call for the synchronous methods of a generated proxy
    /// (<c>string GetName()</c> on a client contract). Runs the call on the thread pool so a caller
    /// on a synchronization context cannot deadlock against the continuation it is blocking.
    /// </summary>
    internal static class SyncOverAsync {

        public static T Run<T>(Func<Task<T>> call) => Task.Run(call).GetAwaiter().GetResult();

        public static void Run(Func<Task> call) => Task.Run(call).GetAwaiter().GetResult();
    }
}
