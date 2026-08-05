using System.Reflection;
using System.Threading;
using Cocoar.SignalARRR.Common;
using Microsoft.AspNetCore.Mvc;

namespace Cocoar.SignalARRR.Server {
    /// <summary>
    /// The slot rules for methods the server exposes to clients. Must mirror exactly what
    /// <c>MessageHandler.BuildExecuteMethodParametersAsync</c> skips when binding: a
    /// <see cref="CancellationToken"/> parameter is bound from the invocation and a
    /// <c>[FromServices]</c> parameter from the container — neither consumes a message argument.
    /// Trailing parameters with default values may be omitted by the caller; the binder fills them.
    /// </summary>
    internal static class ServerWireSlots {
        public static readonly WireSlotPolicy Policy = new WireSlotPolicy(
            parameter => parameter.ParameterType == typeof(CancellationToken)
                         || parameter.GetCustomAttribute<FromServicesAttribute>() != null,
            allowOmittedTrailingDefaults: true);
    }
}
