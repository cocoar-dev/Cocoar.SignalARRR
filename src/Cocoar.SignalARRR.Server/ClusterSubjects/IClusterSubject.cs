using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Cocoar.SignalARRR.Server {
    /// <summary>
    /// An observable whose events reach subscribers on every node of the cluster, not just the
    /// one they were raised on. Register one per event type with
    /// <c>services.AddSignalARRRClusterSubject&lt;T&gt;("name")</c>, raise events with
    /// <see cref="OnNext"/>, and return it — filtered and mapped as you like — from a hub method
    /// that streams to clients.
    /// </summary>
    /// <remarks>
    /// The backplane routes the <i>target</i> of a send: a push to a connection, a group or a
    /// user finds the node that holds it. It never saw the <i>source</i> of a server stream: a
    /// hub method that returns an <see cref="IObservable{T}"/> fed by an in-process subject
    /// streams only what that process raised. A cluster subject closes that gap by relaying every
    /// event to the same subject on the other nodes over the backplane transport, so an
    /// application in the subscribe style is cluster-aware without a relay of its own.
    /// <para>
    /// Semantics: local subscribers see an event once, from <see cref="OnNext"/>; subscribers on
    /// other nodes see it once, from the relay; a received event is never relayed again. Events
    /// raised on one node arrive on the others in the order they were raised. Delivery is the
    /// backplane's — transient with Redis, replayed after a subscription drop with Postgres
    /// catch-up. Without a backplane the subject is a plain local one.
    /// </para>
    /// <para>
    /// The event type is fixed at registration and no type name travels on the wire: a node
    /// deserializes into <typeparamref name="T"/> or drops the event with a warning, which is
    /// what keeps a rolling update with mixed builds safe. Polymorphic payloads are the
    /// application's choice, through <see cref="ClusterSubjectOptions.SerializerOptions"/>.
    /// </para>
    /// </remarks>
    public interface IClusterSubject<T> : IObservable<T> {
        /// <summary>The cluster-wide name; events are matched to subjects by it.</summary>
        string Name { get; }

        /// <summary>
        /// Raises <paramref name="value"/> for local subscribers now and for the other nodes as
        /// soon as the relay gets to it. Does not wait for the network: a relay failure is logged,
        /// it does not fail the caller.
        /// </summary>
        void OnNext(T value);

        /// <summary>
        /// Like <see cref="OnNext"/>, but completes once the event has been handed to the
        /// backplane — or faults if it could not be. Local subscribers have it either way.
        /// </summary>
        Task PublishAsync(T value, CancellationToken cancellationToken = default);
    }

    /// <summary>Per-subject settings for <c>AddSignalARRRClusterSubject</c>.</summary>
    public sealed class ClusterSubjectOptions {
        /// <summary>
        /// How events are serialized for the other nodes. Defaults to
        /// <see cref="JsonSerializerDefaults.Web"/>. Set it to enable polymorphism, custom
        /// converters, or a source-generated context.
        /// </summary>
        public JsonSerializerOptions? SerializerOptions { get; set; }
    }
}
