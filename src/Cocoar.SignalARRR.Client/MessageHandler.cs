using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Cocoar.SignalARRR.Client.ExtensionMethods;
using Cocoar.SignalARRR.Common;
using Cocoar.SignalARRR.Common.Constants;
using Cocoar.SignalARRR.Common.Exceptions;
using Cocoar.SignalARRR.Common.RemoteReferenceTypes;
using Cocoar.SignalARRR.ProxyGenerator;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.SignalR.Protocol;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Cocoar.SignalARRR.Client {
    internal class MessageHandler {
        private readonly ClientConnectionContext _connectionContext;
        private readonly ILogger _logger;

        public MessageHandler(ClientConnectionContext connectionContext, ILogger? logger = null) {
            _connectionContext = connectionContext;
            _logger = logger ?? NullLogger.Instance;
        }

        public async Task<string?> ChallengeAuthentication(ServerRequestMessage message) {
            return await _connectionContext.AuthorizationProvider();
        }

        /// <summary>
        /// Everything logged while a server-to-client call runs carries the method and the server's
        /// invocation id, so a client log line can be matched to the server line that caused it.
        /// </summary>
        private IDisposable? BeginServerRequestLogScope(ServerRequestMessage message) =>
            _logger.BeginScope(new Dictionary<string, object?> {
                ["SignalARRRMethod"] = message.Method,
                ["SignalARRRInvocationId"] = message.Id,
            });

        public async Task<object?> InvokeServerRequest(ServerRequestMessage message) {
            using var logScope = BeginServerRequestLogScope(message);
            using var activity = SignalARRRClientTelemetry.StartIncomingCall(message);
            try {
                var result = await InvokeAsync(message);

                // If the result is a Stream, upload it to the server and return a StreamReference
                if (result is Stream stream) {
                    return await UploadStreamAndReturnReference(stream);
                }

                return result;
            } catch (Exception ex) {
                SignalARRRClientTelemetry.RecordFailure(activity, ex);
                throw;
            }
        }

        private async Task<StreamReference> UploadStreamAndReturnReference(Stream stream) {
            var hubConnection = _connectionContext.GetHubConnection();

            // Ask server for an upload URL
            var uploadUrl = await hubConnection.InvokeCoreAsync<string>("RequestUploadSlot", Array.Empty<object>(), default);

            // Upload the stream via HTTP POST
            using var httpClient = new HttpClient();
            using var content = new StreamContent(stream);
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
            using var request = await FileTransferHttp.AuthorizeAsync(
                new HttpRequestMessage(HttpMethod.Post, uploadUrl) { Content = content },
                _connectionContext.AuthorizationProvider);
            var response = await httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();

            return new StreamReference { Uri = uploadUrl };
        }

        private Task _fireAndForgetChain = Task.CompletedTask;
        private readonly object _fireAndForgetChainLock = new object();

        /// <summary>
        /// Runs a fire-and-forget server message without occupying SignalR's receive loop.
        /// </summary>
        /// <remarks>
        /// The receive loop awaits an async handler before it processes the next message. With the
        /// method executed inline, one long-running fire-and-forget call therefore blocked every
        /// later server-to-client message on the connection — including <c>CancelTokenFromServer</c>
        /// for its own token, so exactly the calls one wants to cancel were the ones that could
        /// not be. (The invoke path never had the problem: SignalR dispatches client-result
        /// invocations off the loop.) Messages are chained, not parallelized: fire-and-forget
        /// calls keep executing in arrival order, matching SignalR's own sequential default.
        /// </remarks>
        internal void QueueServerMessage(ServerRequestMessage message) {
            lock (_fireAndForgetChainLock) {
                _fireAndForgetChain = RunAfter(_fireAndForgetChain, message);
            }

            async Task RunAfter(Task previous, ServerRequestMessage next) {
                await previous.ConfigureAwait(false);
                // InvokeServerMessage catches everything (fire-and-forget errors are logged, not
                // propagated), so the chain cannot fault and stall later messages.
                await InvokeServerMessage(next).ConfigureAwait(false);
            }
        }

        public async Task InvokeServerMessage(ServerRequestMessage message) {

            try {
                    using var logScope = BeginServerRequestLogScope(message);
                using var activity = SignalARRRClientTelemetry.StartIncomingCall(message);
                try {
                    if (message.StreamId.HasValue) {
                        await InvokeAndStreamBackAsync(message);
                    } else {
                        await InvokeAsync(message);
                    }
                } catch (Exception ex) {
                    SignalARRRClientTelemetry.RecordFailure(activity, ex);
                    throw;
                }
            } catch (Exception ex) {
                // Fire-and-forget methods don't propagate errors to the server,
                // but log them so developers can diagnose failed server-to-client pushes.
                _logger.LogError(ex, "Failed to handle server message '{Method}'", message.Method);
            }
        }

        private async Task InvokeAndStreamBackAsync(ServerRequestMessage message) {
            var streamId = message.StreamId!.Value;
            var hubConnection = _connectionContext.GetHubConnection();
            try {
                var result = await InvokeAsync(message);
                await StreamResultToServer(hubConnection, streamId, result);
                await hubConnection.SendCoreAsync(MethodNames.StreamCompleteToServer, new object?[] { streamId, (string?)null });
            } catch (Exception ex) {
                try {
                    await hubConnection.SendCoreAsync(MethodNames.StreamCompleteToServer, new object[] { streamId, ex.GetBaseException().Message });
                } catch {
                    // Best effort — connection may be gone
                }
            }
        }

        private static async Task StreamResultToServer(HubConnection hubConnection, Guid streamId, object? result) {
            if (result == null) return;

            if (result is IStreamingResult streaming) {
                // The generated dispatcher knows the element type; no reflection to find it.
                await streaming.ForEachAsync(
                    item => hubConnection.SendCoreAsync(MethodNames.StreamItemToServer, new object?[] { streamId, item }),
                    CancellationToken.None);
            } else {
                // Single result — send as one item
                await hubConnection.SendCoreAsync(MethodNames.StreamItemToServer, new object[] { streamId, result });
            }
        }

        /// <summary>One registered contract: its generated dispatcher and where the implementation comes from.</summary>
        private sealed class Registration {
            public Registration(ContractDispatcher dispatcher, Func<IServiceProvider, object> factory) {
                Dispatcher = dispatcher;
                Factory = factory;
            }

            public ContractDispatcher Dispatcher { get; }
            public Func<IServiceProvider, object> Factory { get; }
        }

        /// <summary>
        /// The registered contracts by the names that may appear on the wire. This index is the
        /// allow-list: a call to anything not registered here fails as not found.
        /// </summary>
        private readonly ConcurrentDictionary<string, Registration> _registrations =
            new ConcurrentDictionary<string, Registration>(StringComparer.Ordinal);

        public void RegisterInterface<TInterface>(Func<IServiceProvider, TInterface> factory) where TInterface : class {
            if (factory == null) throw new ArgumentNullException(nameof(factory));

            var dispatcher = DispatcherRegistry.Find<TInterface>()
                ?? throw new InvalidOperationException(
                    $"'{typeof(TInterface).FullName}' has no generated dispatcher, so the client cannot receive calls on it. " +
                    "Mark the interface with [SignalARRRContract] and make sure the project that declares or uses it references Cocoar.SignalARRR.Client (or the source generator).");

            if (dispatcher.UnsupportedReason is { } reason) {
                throw new InvalidOperationException(reason);
            }

            var registration = new Registration(dispatcher, sp => factory(sp));
            foreach (var wireName in GetWireNames(dispatcher)) {
                // Re-registering the same interface (e.g. with a new factory) replaces it; a
                // different interface under the same wire name would silently take over every call
                // of the first one, so that fails here instead.
                if (_registrations.TryGetValue(wireName, out var existing) && existing.Dispatcher.ContractType != dispatcher.ContractType) {
                    throw new InvalidOperationException(
                        $"Cannot register interface '{dispatcher.ContractType.AssemblyQualifiedName}': " +
                        $"'{existing.Dispatcher.ContractType.AssemblyQualifiedName}' is already registered under the wire name '{wireName}'.");
                }

                _registrations[wireName] = registration;
            }
        }

        private static IEnumerable<string> GetWireNames(ContractDispatcher dispatcher) {
            yield return dispatcher.WireName;

            // A declared [MessageName] replaces the type's identity on the wire. Without one, the
            // assembly-qualified name is accepted as well, as it always was.
            if (dispatcher.WireName == dispatcher.ContractType.FullName && dispatcher.ContractType.AssemblyQualifiedName is { } assemblyQualifiedName) {
                yield return assemblyQualifiedName;
            }
        }

        /// <summary>
        /// Fails when the hub protocol's JSON options cannot serialize a type a registered contract
        /// sends or receives, naming every one of them.
        /// </summary>
        /// <remarks>
        /// Only where reflection-based JSON is off (NativeAOT, or an app that switched it off):
        /// otherwise reflection covers whatever the app did not declare. Checked at start so a
        /// missing type surfaces at once, not as a lost push or a failed call much later.
        /// </remarks>
        internal void EnsureWireTypesSerializable() {
            if (JsonSerializer.IsReflectionEnabledByDefault) return;

            var protocol = _connectionContext.GetService<IHubProtocol>();
            if (protocol != null && protocol.Name != "json") return;

            var options = _connectionContext.JsonPayloadOptions;
            var missing = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var registration in new HashSet<Registration>(_registrations.Values)) {
                foreach (var type in registration.Dispatcher.WireTypes) {
                    if (!options.TryGetTypeInfo(type, out _)) {
                        missing.Add($"{type.FullName} (used by {registration.Dispatcher.ContractType.FullName})");
                    }
                }
            }

            if (missing.Count == 0) return;

            var message = new StringBuilder("These types cross the wire but the hub protocol's JSON options have no contract for them, and reflection-based JSON is off:");
            foreach (var entry in missing) {
                message.Append(Environment.NewLine).Append("  - ").Append(entry);
            }
            message.Append(Environment.NewLine)
                .Append("Add them to your JsonSerializerContext ([JsonSerializable(typeof(...))]) and insert it into PayloadSerializerOptions.TypeInfoResolverChain in AddJsonProtocol.");
            throw new InvalidOperationException(message.ToString());
        }

        private async Task<object?> InvokeAsync(ServerRequestMessage message) {

            var separator = message.Method.IndexOf(WireName.Separator);
            if (separator < 0) {
                throw new MethodResolutionException(HARRRErrorCodes.MethodNotFound, $"Method '{message.Method}' not found!");
            }

            var interfaceName = message.Method.Substring(0, separator);
            var methodName = message.Method.Substring(separator + 1);

            if (!_registrations.TryGetValue(interfaceName, out var registration)) {
                throw new MethodResolutionException(HARRRErrorCodes.MethodNotFound, $"Interface '{interfaceName}' is not registered.");
            }

            if (message.GenericArguments is { Length: > 0 }) {
                // Older servers can still send these; resolving a type argument by name needs reflection.
                throw new MethodResolutionException(HARRRErrorCodes.MethodNotFound,
                    $"Method '{methodName}' was called with generic arguments, which a client does not support.");
            }

            var instance = registration.Factory(_connectionContext.GetHubConnection().GetServiceProvider());

            // Every linked source this invocation creates, so the finally can let go of all of
            // them: the call-level one below, plus one per CancellationToken parameter.
            var ownedTokenIds = new List<Guid>(1);
            var ownershipTransferred = false;

            try {
                // Connection lifetime rather than None: the handler notices its caller's world ending
                // even when the server never sends an explicit cancellation.
                CancellationToken cancellationToken = _connectionLifetime.Token;
                if (message.CancellationGuid.HasValue) {
                    var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_connectionLifetime.Token);
                    cancellationTokenSources.TryAdd(message.CancellationGuid.Value, cancellation);
                    ownedTokenIds.Add(message.CancellationGuid.Value);
                    cancellationToken = cancellation.Token;
                }

                var context = new ClientCallContext(this, cancellationToken, ownedTokenIds);
                var arguments = new JsonArgumentReader(message.Arguments, _connectionContext.JsonPayloadOptions, context);

                object? result;
                try {
                    result = await registration.Dispatcher.InvokeUntypedAsync(instance, methodName, arguments);
                } catch (DispatchException dispatchException) {
                    throw new MethodResolutionException(dispatchException.Code, dispatchException.Message);
                }

                if (result is IStreamingResult) {
                    // The stream is consumed after this method returns, so its token has to outlive
                    // us. Cleaning up here would hand the caller an already-cancelled enumeration.
                    // These sources are released when the connection ends.
                    ownershipTransferred = true;
                }

                return result;
            }
            finally {
                // Dispose, not merely remove. CreateLinkedTokenSource registers a callback on the
                // parent, and the parent here is the connection lifetime — so a source that is
                // dropped without being disposed leaves that registration attached for as long as
                // the connection lives. On a long-lived connection taking a server push every few
                // seconds, that grows without bound and nothing fails while it happens.
                if (!ownershipTransferred) {
                    foreach (var id in ownedTokenIds) {
                        if (cancellationTokenSources.TryRemove(id, out var cts)) {
                            cts.Dispose();
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Resolves the slots of one call that the connection, not JSON, has to fill: tokens the
        /// server can cancel, and streams that travel through file transfer.
        /// </summary>
        private sealed class ClientCallContext : CallContext {
            private readonly MessageHandler _handler;
            private readonly ICollection<Guid> _ownedTokenIds;

            public ClientCallContext(MessageHandler handler, CancellationToken callCancellation, ICollection<Guid> ownedTokenIds) {
                _handler = handler;
                CallCancellation = callCancellation;
                _ownedTokenIds = ownedTokenIds;
            }

            public override CancellationToken CallCancellation { get; }

            public override CancellationToken ResolveCancellationToken(ArgumentReader reader, int index) {
                var reference = index < reader.Count ? TryRead<CancellationTokenReference>(reader, index) : null;
                if (reference == null || reference.Id == Guid.Empty) {
                    return CallCancellation;
                }

                // Linked to the connection lifetime: a per-token cancellation the server can no longer
                // deliver (the connection is gone) must still fire.
                var cts = CancellationTokenSource.CreateLinkedTokenSource(_handler._connectionLifetime.Token);
                _handler.cancellationTokenSources.TryAdd(reference.Id, cts);
                _ownedTokenIds.Add(reference.Id);
                return cts.Token;
            }

            public override async Task<Stream?> ResolveStreamAsync(ArgumentReader reader, int index) {
                if (index >= reader.Count) return null;
                if (reader.GetRaw(index) is Stream stream) return stream;

                var reference = TryRead<StreamReference>(reader, index);
                if (reference == null || string.IsNullOrEmpty(reference.Uri)) return null;

                return await new StreamReferenceResolver(reference, _handler._connectionContext).ProcessStreamArgument();
            }

            private static T? TryRead<T>(ArgumentReader reader, int index) where T : class {
                try {
                    return reader.Read<T>(index);
                } catch (Exception ex) when (ex is JsonException || ex is InvalidOperationException || ex is NotSupportedException) {
                    // Not a reference: an ordinary value in a slot the contract declares as token or stream.
                    return null;
                }
            }
        }

        private ConcurrentDictionary<Guid, CancellationTokenSource> cancellationTokenSources = new ConcurrentDictionary<Guid, CancellationTokenSource>();

        /// <summary>
        /// How many linked cancellation sources are still held. Test seam: a leak here is invisible
        /// from the outside — nothing fails, the process just grows — so the regression test needs
        /// a way to see that finished invocations let go of theirs.
        /// </summary>
        internal int TrackedCancellationSourceCount => cancellationTokenSources.Count;

        /// <summary>
        /// Fires when the underlying connection closes. Every token bound into a client method
        /// links to it — without this, a handler with a <see cref="CancellationToken"/> parameter
        /// got <c>CancellationToken.None</c> and kept running into the void after the connection
        /// died (N-2), while a server method in the same situation observed
        /// <c>ConnectionAborted</c>. No own abort source: SignalR's Closed event *is* the signal,
        /// and Stateful Reconnect extends exactly that lifetime.
        /// </summary>
        private CancellationTokenSource _connectionLifetime = new CancellationTokenSource();

        internal void OnConnectionClosed() {
            // A fresh lifetime begins for a potential restart of the same connection object.
            var previous = Interlocked.Exchange(ref _connectionLifetime, new CancellationTokenSource());
            previous.Cancel();
            previous.Dispose();
        }

        public void CancelTokenFromServer(ServerRequestMessage requestMessage) {

            if (requestMessage.CancellationGuid.HasValue) {
                // Look up rather than remove: the invocation that created this source owns it and
                // disposes it when it finishes. Taking it out here would leave two parties each
                // believing they hold the last reference — and one of them disposing it while the
                // handler still reads the token.
                if (cancellationTokenSources.TryGetValue(requestMessage.CancellationGuid.Value, out var token)) {
                    try {
                        token.Cancel();
                    }
                    catch (ObjectDisposedException) {
                        // The invocation finished between the lookup and the cancel. There is
                        // nothing left to cancel, and the source is already gone.
                    }
                }
            }

        }
    }
}