using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace Cocoar.SignalARRR.Server {
    internal interface IClusterClientQueryMetadata {
        Type HubType { get; }
        IServiceProvider ServiceProvider { get; }
        SignalARRRBackplaneTargetKind TargetKind { get; }
        string? GroupName { get; }
        string? UserId { get; }
        IReadOnlyList<SignalARRRConnectionAttributeFilter> AttributeFilters { get; }
        bool DistributedDispatchSupported { get; }
        bool CanUseDirectBackplaneDispatch { get; }
    }

    internal sealed class ClusterClientQuery : IEnumerable<ClientContext>, IClusterClientQueryMetadata {
        private readonly IEnumerable<ClientContext> _clients;

        public ClusterClientQuery(
            IEnumerable<ClientContext> clients,
            Type hubType,
            IServiceProvider serviceProvider,
            string? groupName = null,
            string? userId = null,
            IReadOnlyList<SignalARRRConnectionAttributeFilter>? attributeFilters = null,
            bool distributedDispatchSupported = true) {
            _clients = clients;
            HubType = hubType;
            ServiceProvider = serviceProvider;
            GroupName = groupName;
            UserId = userId;
            AttributeFilters = attributeFilters ?? Array.Empty<SignalARRRConnectionAttributeFilter>();
            DistributedDispatchSupported = distributedDispatchSupported;
        }

        public Type HubType { get; }

        public IServiceProvider ServiceProvider { get; }

        public SignalARRRBackplaneTargetKind TargetKind => GroupName != null
            ? SignalARRRBackplaneTargetKind.Group
            : UserId != null
                ? SignalARRRBackplaneTargetKind.User
                : SignalARRRBackplaneTargetKind.All;

        public string? GroupName { get; }

        public string? UserId { get; }

        public IReadOnlyList<SignalARRRConnectionAttributeFilter> AttributeFilters { get; }

        public bool DistributedDispatchSupported { get; }

        public bool CanUseDirectBackplaneDispatch =>
            DistributedDispatchSupported &&
            AttributeFilters.Count == 0 &&
            !(GroupName != null && UserId != null);

        public ClusterClientQuery WithGroup(string groupName) {
            if (!DistributedDispatchSupported || (GroupName != null && !string.Equals(GroupName, groupName, StringComparison.OrdinalIgnoreCase))) {
                return WithLocalFilter(c => c.Groups.Contains(groupName));
            }

            return new ClusterClientQuery(
                _clients.Where(c => c.Groups.Contains(groupName)),
                HubType,
                ServiceProvider,
                groupName,
                UserId,
                AttributeFilters,
                DistributedDispatchSupported);
        }

        public ClusterClientQuery WithUser(string userId) {
            if (!DistributedDispatchSupported || (UserId != null && !string.Equals(UserId, userId, StringComparison.Ordinal))) {
                return WithLocalFilter(c => string.Equals(c.UserIdentifier, userId, StringComparison.Ordinal));
            }

            return new ClusterClientQuery(
                _clients.Where(c => string.Equals(c.UserIdentifier, userId, StringComparison.Ordinal)),
                HubType,
                ServiceProvider,
                GroupName,
                userId: userId,
                AttributeFilters,
                DistributedDispatchSupported);
        }

        public ClusterClientQuery WithAttribute(string key, string? value = null) {
            if (!DistributedDispatchSupported) {
                return value == null
                    ? WithLocalFilter(c => c.Attributes.Has(key))
                    : WithLocalFilter(c => c.Attributes.Has(key, value));
            }

            var filters = AttributeFilters.Concat(new[] {
                new SignalARRRConnectionAttributeFilter {
                    Key = key,
                    Value = value
                }
            }).ToArray();

            return new ClusterClientQuery(
                value == null ? _clients.Where(c => c.Attributes.Has(key)) : _clients.Where(c => c.Attributes.Has(key, value)),
                HubType,
                ServiceProvider,
                GroupName,
                UserId,
                filters,
                DistributedDispatchSupported);
        }

        public ClusterClientQuery WithLocalFilter(Func<ClientContext, bool> predicate) {
            return new ClusterClientQuery(
                _clients.Where(predicate),
                HubType,
                ServiceProvider,
                GroupName,
                UserId,
                AttributeFilters,
                distributedDispatchSupported: false);
        }

        public IEnumerator<ClientContext> GetEnumerator() => _clients.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
