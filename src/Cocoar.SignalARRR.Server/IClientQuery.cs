using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Cocoar.SignalARRR.Server.ExtensionMethods;

namespace Cocoar.SignalARRR.Server {

    /// <summary>
    /// A set of clients selected on a hub, and the operations that can be performed on it.
    /// Obtained from <see cref="ClientManager.WithHub{THub}"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A query is a <em>description</em> of a target set, not a materialised list. With a backplane
    /// enabled, <see cref="SendAsync{T}"/> and the invoke methods resolve that description across
    /// every node in the cluster. The filters below are the ones the backplane can evaluate
    /// remotely, because they are answered from the distributed connection registry.
    /// </para>
    /// <para>
    /// <see cref="LocalClients"/> is deliberately the only way to get at
    /// <see cref="ClientContext"/> instances, and its name is the whole point: connections owned by
    /// other nodes have no <see cref="ClientContext"/> in this process, so anything you enumerate is
    /// this node's share of the query and nothing else.
    /// </para>
    /// </remarks>
    public interface IClientQuery {

        /// <summary>Narrows the query to clients in a SignalR group. Evaluated cluster-wide.</summary>
        IClientQuery WithGroup(string groupName);

        /// <summary>Narrows the query to a user's connections. Evaluated cluster-wide.</summary>
        IClientQuery WithUser(string userId);

        /// <summary>
        /// Narrows the query by connection attribute — presence of <paramref name="key"/>, or an exact
        /// match when <paramref name="value"/> is given. Evaluated cluster-wide.
        /// </summary>
        IClientQuery WithAttribute(string key, string? value = null);

        /// <summary>
        /// Narrows the query by an arbitrary predicate over <see cref="ClientContext"/>, and thereby
        /// restricts it to this node.
        /// </summary>
        /// <remarks>
        /// A predicate is a delegate over local objects: it cannot be shipped to another node, and the
        /// other nodes' <see cref="ClientContext"/> instances do not exist here to run it against. So
        /// this filter — and every operation after it — covers this node only, and says so in its name.
        /// To narrow a cluster-wide query, use <see cref="WithGroup"/>, <see cref="WithUser"/> or
        /// <see cref="WithAttribute"/>, which the connection registry can answer for every node.
        /// </remarks>
        IClientQuery WithLocalFilter(Func<ClientContext, bool> predicate);

        /// <summary>Fire-and-forget call to every client in the query, through a typed contract.</summary>
        Task SendAsync<T>(Action<T> action, CancellationToken cancellationToken = default) where T : class;

        /// <summary>Invokes every client individually and returns one result per client.</summary>
        Task<IEnumerable<ClientResult<TResult>>> InvokeAllAsync<TResult>(string method, object[] arguments, CancellationToken cancellationToken);

        /// <inheritdoc cref="InvokeAllAsync{TResult}(string, object[], CancellationToken)"/>
        Task<IEnumerable<ClientResult<TResult>>> InvokeAllAsync<TInterface, TResult>(Func<TInterface, TResult> action, CancellationToken cancellationToken = default) where TInterface : class;

        /// <inheritdoc cref="InvokeAllAsync{TResult}(string, object[], CancellationToken)"/>
        Task<IEnumerable<ClientResult<TResult>>> InvokeAllAsync<TInterface, TResult>(Func<TInterface, Task<TResult>> action, CancellationToken cancellationToken = default) where TInterface : class;

        /// <summary>Invokes clients one by one until one succeeds, and returns that result.</summary>
        Task<ClientResult<TResult>> InvokeOneAsync<TResult>(string method, object[] arguments, CancellationToken cancellationToken);

        /// <inheritdoc cref="InvokeOneAsync{TResult}(string, object[], CancellationToken)"/>
        Task<ClientResult<TResult>> InvokeOneAsync<TInterface, TResult>(Func<TInterface, TResult> action, CancellationToken cancellationToken = default) where TInterface : class;

        /// <inheritdoc cref="InvokeOneAsync{TResult}(string, object[], CancellationToken)"/>
        Task<ClientResult<TResult>> InvokeOneAsync<TInterface, TResult>(Func<TInterface, Task<TResult>> action, CancellationToken cancellationToken = default) where TInterface : class;

        /// <summary>
        /// The connections matching this query that are owned by <em>this</em> node. Never includes
        /// connections held by other nodes — they have no <see cref="ClientContext"/> in this process.
        /// </summary>
        IReadOnlyCollection<ClientContext> LocalClients();
    }
}
