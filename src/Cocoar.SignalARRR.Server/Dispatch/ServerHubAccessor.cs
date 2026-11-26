using System;
using System.ComponentModel;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Cocoar.SignalARRR.Server {
    /// <summary>
    /// Reaches a hub's <see cref="IHubContext{THub}"/> for a hub known only as a <see cref="Type"/>.
    /// </summary>
    /// <remarks>
    /// A client knows its hub as <see cref="ClientContext.HARRRType"/>. Getting from there to the
    /// hub context took <c>MakeGenericType(typeof(IHubContext&lt;&gt;))</c> and reflection over its
    /// <c>Clients</c> property, which NativeAOT cannot run. The source generator registers one
    /// accessor per hub instead, closed over the hub type at compile time.
    /// </remarks>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public abstract class ServerHubAccessor {

        public abstract Type HubType { get; }

        internal abstract IHubClients Clients(IServiceProvider services);

        internal abstract IGroupManager Groups(IServiceProvider services);

        internal abstract IClientContextDispatcher Dispatcher(IServiceProvider services);
    }

    /// <inheritdoc />
    [EditorBrowsable(EditorBrowsableState.Never)]
    public sealed class ServerHubAccessor<THub> : ServerHubAccessor where THub : HARRR {

        public override Type HubType => typeof(THub);

        internal override IHubClients Clients(IServiceProvider services) => services.GetRequiredService<IHubContext<THub>>().Clients;

        internal override IGroupManager Groups(IServiceProvider services) => services.GetRequiredService<IHubContext<THub>>().Groups;

        internal override IClientContextDispatcher Dispatcher(IServiceProvider services) =>
            new ClientContextDispatcher<THub>(
                services.GetRequiredService<IHubContext<THub>>(),
                services.GetService<ILoggerFactory>()?.CreateLogger<ClientContextDispatcher<THub>>() ?? NullLogger<ClientContextDispatcher<THub>>.Instance);
    }
}
