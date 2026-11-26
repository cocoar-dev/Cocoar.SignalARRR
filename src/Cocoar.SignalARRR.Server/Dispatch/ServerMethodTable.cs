using System;
using System.Collections.Generic;
using System.Linq;
using Cocoar.SignalARRR.Common;
using Cocoar.SignalARRR.Common.Exceptions;

namespace Cocoar.SignalARRR.Server {
    /// <summary>
    /// The methods clients may call on one hub, by name. This table <em>is</em> the allow-list: a
    /// name that is not here fails as not found before anything else happens.
    /// </summary>
    internal sealed class ServerMethodTable {

        public static readonly ServerMethodTable Empty = new ServerMethodTable();

        private readonly Dictionary<string, List<ServerMethodDescriptor>> _byName = new Dictionary<string, List<ServerMethodDescriptor>>(StringComparer.Ordinal);
        private readonly HashSet<string> _interfaces = new HashSet<string>(StringComparer.Ordinal);

        public void Add(ServerMethodDescriptor descriptor) {
            Add(descriptor.Name, descriptor);
            if (descriptor.AlternateName != null) {
                Add(descriptor.AlternateName, descriptor);
            }
        }

        private void Add(string name, ServerMethodDescriptor descriptor) {
            if (!_byName.TryGetValue(name, out var candidates)) {
                candidates = new List<ServerMethodDescriptor>();
                _byName[name] = candidates;
            }

            // The generator rejects a clash within one compilation (SARRR002); this catches one
            // across assemblies, which only meet here. The wire carries no parameter types, so two
            // methods under one name must differ in argument count.
            var clash = candidates.FirstOrDefault(c => c.Overlaps(descriptor));
            if (clash != null) {
                throw new InvalidOperationException(
                    $"{clash.Declaration} and {descriptor.Declaration} would both be reachable as '{name}' with the same argument count. " +
                    "The wire carries no parameter types, so methods sharing a name must differ in argument count — rename one of them.");
            }

            candidates.Add(descriptor);

            var separator = name.IndexOf(WireName.Separator);
            if (separator > 0) {
                _interfaces.Add(name.Substring(0, separator));
            }
        }

        public ServerMethodDescriptor Resolve(string name, int argumentCount) {
            if (_byName.TryGetValue(name, out var candidates)) {
                foreach (var candidate in candidates) {
                    if (candidate.Accepts(argumentCount)) return candidate;
                }

                var accepted = candidates
                    .SelectMany(c => Enumerable.Range(c.MinArguments, c.MaxArguments - c.MinArguments + 1))
                    .OrderBy(c => c);
                throw new MethodResolutionException(HARRRErrorCodes.InvalidArgumentCount,
                    $"Method '{MethodPart(name)}' cannot be called with {argumentCount} argument(s). " +
                    $"Registered argument count(s): {string.Join(", ", accepted)}.");
            }

            var separator = name.IndexOf(WireName.Separator);
            if (separator > 0 && !_interfaces.Contains(name.Substring(0, separator))) {
                throw new MethodResolutionException(HARRRErrorCodes.MethodNotFound, $"Interface '{name.Substring(0, separator)}' is not registered.");
            }

            throw new MethodResolutionException(HARRRErrorCodes.MethodNotFound, $"Method '{MethodPart(name)}' not found!");
        }

        private static string MethodPart(string name) {
            var separator = name.IndexOf(WireName.Separator);
            return separator < 0 ? name : name.Substring(separator + 1);
        }
    }

    /// <summary>The method tables of all hubs, built once by <c>AddSignalARRR</c>.</summary>
    internal sealed class ServerMethodTables {
        private readonly Dictionary<Type, ServerMethodTable> _tables;

        public ServerMethodTables(Dictionary<Type, ServerMethodTable> tables) => _tables = tables;

        public ServerMethodTable For(Type hubType) => _tables.TryGetValue(hubType, out var table) ? table : ServerMethodTable.Empty;
    }
}
