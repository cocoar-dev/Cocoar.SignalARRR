using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;

namespace Cocoar.SignalARRR.Server {
    /// <summary>
    /// Fails the host's start when the hub protocol's JSON options cannot serialize a type a server
    /// method sends or receives, naming every one of them.
    /// </summary>
    /// <remarks>
    /// Only where reflection-based JSON is off (NativeAOT, or an app that switched it off): otherwise
    /// reflection covers whatever the app did not declare. Without the check a missing type surfaced
    /// only at the first call that needed it, as an argument binding error on one method.
    /// </remarks>
    internal sealed class ServerWireTypeCheck : IHostedService {

        private readonly ServerMethodTables _tables;
        private readonly ServerPayloadOptions _payloadOptions;

        public ServerWireTypeCheck(ServerMethodTables tables, ServerPayloadOptions payloadOptions) {
            _tables = tables;
            _payloadOptions = payloadOptions;
        }

        public Task StartAsync(CancellationToken cancellationToken) {
            if (JsonSerializer.IsReflectionEnabledByDefault) return Task.CompletedTask;

            var options = _payloadOptions.Options;
            var missing = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var method in _tables.AllMethods) {
                foreach (var type in method.WireTypes) {
                    if (!options.TryGetTypeInfo(type, out _)) {
                        missing.Add($"{type.FullName} (used by {method.Declaration})");
                    }
                }
            }

            if (missing.Count == 0) return Task.CompletedTask;

            var message = new StringBuilder("These types cross the wire but the hub protocol's JSON options have no contract for them, and reflection-based JSON is off:");
            foreach (var entry in missing) {
                message.Append(Environment.NewLine).Append("  - ").Append(entry);
            }
            message.Append(Environment.NewLine)
                .Append("Add them to your JsonSerializerContext ([JsonSerializable(typeof(...))]) and insert it into PayloadSerializerOptions.TypeInfoResolverChain in AddJsonProtocol.");
            throw new InvalidOperationException(message.ToString());
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
