using System;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;

namespace Cocoar.SignalARRR.Server {
    /// <summary>
    /// Lets the structured errors of SignalARRR hubs reach the client under NativeAOT.
    /// </summary>
    /// <remarks>
    /// The hub methods throw <see cref="HARRRException"/>, a <see cref="HubException"/>, whose message
    /// SignalR passes to the caller: the error code and message clients branch on. Under NativeAOT,
    /// SignalR invokes hub methods through reflection, which wraps every exception in a
    /// <see cref="TargetInvocationException"/>; SignalR then no longer recognizes the
    /// <see cref="HubException"/> and sends "An unexpected error occurred" instead. Under JIT it
    /// invokes through compiled delegates and nothing is wrapped, so this filter changes nothing there.
    /// Remove it once SignalR unwraps the exception itself.
    /// </remarks>
    internal sealed class SignalARRRHubFilter : IHubFilter {

        public async ValueTask<object?> InvokeMethodAsync(HubInvocationContext invocationContext, Func<HubInvocationContext, ValueTask<object?>> next) {
            try {
                return await next(invocationContext).ConfigureAwait(false);
            } catch (TargetInvocationException wrapped) when (invocationContext.Hub is HARRR && wrapped.InnerException != null) {
                ExceptionDispatchInfo.Capture(wrapped.InnerException).Throw();
                throw; // unreachable
            }
        }
    }
}
