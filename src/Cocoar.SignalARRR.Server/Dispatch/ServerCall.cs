using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Cocoar.SignalARRR.Common.RemoteReferenceTypes;
using Cocoar.SignalARRR.ProxyGenerator;
using Cocoar.SignalARRR.Server.ExtensionMethods;
using Microsoft.Extensions.DependencyInjection;

namespace Cocoar.SignalARRR.Server {
    /// <summary>
    /// What a generated server method call needs from the invocation it runs in: the hub, the
    /// <see cref="ServerMethods{T}"/> instance, services, the cancellation token, uploads and streams.
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public sealed class ServerCall : CallContext {

        private readonly HARRR _hub;
        private readonly IServiceProvider _services;
        private readonly CancellationToken _uploadCancellation;

        internal ServerCall(HARRR hub, ClientContext clientContext, IServiceProvider services, ServerMethodDescriptor method,
            CancellationToken cancellation, CancellationToken uploadCancellation) {
            _hub = hub;
            ClientContext = clientContext;
            _services = services;
            Method = method;
            CallCancellation = cancellation;
            _uploadCancellation = uploadCancellation;
        }

        internal ClientContext ClientContext { get; }

        internal ServerMethodDescriptor Method { get; }

        /// <summary>
        /// The token bound to the method's <see cref="CancellationToken"/> parameters: the connection's
        /// end on the invoke paths, application shutdown on the send path (N-3).
        /// </summary>
        public override CancellationToken CallCancellation { get; }

        /// <summary>The hub instance SignalR created for this invocation.</summary>
        public THub Hub<THub>() where THub : HARRR => (THub)_hub;

        /// <summary>A <see cref="ServerMethods{T}"/> instance from the container, wired to this invocation.</summary>
        public T Instance<T>() where T : ServerMethods {
            var instance = _services.GetRequiredService<T>();
            instance.ClientContext = ClientContext;
            instance.Context = _hub.Context;
            instance.Clients = _hub.Clients;
            instance.Groups = _hub.Groups;
            instance.Logger = LoggerCache.For(_services, typeof(T));
            return instance;
        }

        /// <summary>A <c>[FromServices]</c> parameter.</summary>
        public T Service<T>() where T : notnull => _services.GetRequiredService<T>();

        /// <summary>
        /// Reads an argument. A value that does not convert fails as an argument binding error that
        /// names the parameter but not the value: a credential that fails to deserialize would
        /// otherwise land in the server log in clear text.
        /// </summary>
        public T Argument<T>(ArgumentReader arguments, int index, string parameterName) {
            try {
                return arguments.Read<T>(index);
            } catch (Exception ex) {
                var received = arguments.GetRaw(index)?.GetType().Name ?? "null";
                throw new ArgumentException(
                    $"Parameter '{parameterName}' (position {index}): failed to deserialize {received} to {typeof(T).Name}.", ex);
            }
        }

        /// <summary>A <see cref="Stream"/> argument: waits for the client's upload of the referenced slot.</summary>
        public async Task<Stream> StreamArgument(ArgumentReader arguments, int index, string parameterName) {
            return await ResolveStreamAsync(arguments, index).ConfigureAwait(false)
                ?? throw new ArgumentException($"Parameter '{parameterName}' (position {index}): expected a stream reference.");
        }

        public override CancellationToken ResolveCancellationToken(ArgumentReader reader, int index) => CallCancellation;

        public override async Task<Stream?> ResolveStreamAsync(ArgumentReader reader, int index) {
            if (index >= reader.Count || reader.GetRaw(index) == null) return null;

            StreamReference? reference;
            try {
                reference = reader.Read<StreamReference>(index);
            } catch (Exception) {
                return null;
            }

            if (reference == null || string.IsNullOrEmpty(reference.Uri)) return null;

            var streamManager = _services.GetRequiredService<ServerPushStreamManager>();
            var timeout = _services.GetService<SignalARRRServerOptions>()?.StreamUploadTimeout ?? TimeSpan.FromMinutes(2);

            // ClientContext.Id, not just the URL: the slot belongs to the connection that requested
            // it, so naming someone else's is a miss rather than a free read. The wait keeps the
            // connection's own token whatever the method's is bound to (V-2): waiting for an upload
            // from a client that already disconnected parked a thread-pool thread permanently.
            return await streamManager.WaitForUpload(reference.Uri, ClientContext.Id, timeout, _uploadCancellation).ConfigureAwait(false);
        }

        /// <summary>A streaming result; every item is authorized again before it goes out.</summary>
        public IAsyncEnumerable<object> Stream<T>(IAsyncEnumerable<T> source) => new StreamingResult<T>(source, ClientContext, Method.Authorization);

        /// <inheritdoc cref="Stream{T}(IAsyncEnumerable{T})"/>
        public IAsyncEnumerable<object> Stream<T>(ChannelReader<T> source) => new StreamingResult<T>(source, ClientContext, Method.Authorization);

        /// <inheritdoc cref="Stream{T}(IAsyncEnumerable{T})"/>
        public IAsyncEnumerable<object> Stream<T>(IObservable<T> source) =>
            new StreamingResult<T>(source.AsChannelReaderInternal(CallCancellation), ClientContext, Method.Authorization);
    }
}
