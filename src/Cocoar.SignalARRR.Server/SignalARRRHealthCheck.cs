using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Cocoar.SignalARRR.Server {
    /// <summary>
    /// Reports the health of the SignalARRR runtime: stream/upload bookkeeping and — when a
    /// backplane is configured — store reachability, heartbeat freshness and whether the
    /// heartbeat loop is still alive at all (O-8). Register via
    /// <c>services.AddSignalARRRHealthChecks()</c>.
    /// </summary>
    internal sealed class SignalARRRHealthCheck : IHealthCheck {
        private readonly ISignalARRRBackplane _backplane;
        private readonly ServerStreamManager _streamManager;
        private readonly ServerPushStreamManager _pushStreamManager;

        public SignalARRRHealthCheck(
            ISignalARRRBackplane backplane,
            ServerStreamManager streamManager,
            ServerPushStreamManager pushStreamManager) {
            _backplane = backplane;
            _streamManager = streamManager;
            _pushStreamManager = pushStreamManager;
        }

        public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) {
            var data = new Dictionary<string, object> {
                ["activeStreams"] = _streamManager.ActiveStreamCount,
                ["pendingDownloads"] = _pushStreamManager.PendingDownloadCount,
                ["pendingUploadSlots"] = _pushStreamManager.PendingUploadSlotCount,
            };

            if (!_backplane.IsEnabled) {
                data["backplane"] = "disabled";
                return HealthCheckResult.Healthy("Single node; no backplane configured.", data);
            }

            data["nodeId"] = _backplane.NodeId;

            if (_backplane is ISignalARRRBackplaneHealth health) {
                if (health.HeartbeatLoopFaulted) {
                    // Nothing keeps this node registered anymore: once the TTL key expires, the
                    // cluster declares it dead and wipes its registrations while it keeps serving.
                    return HealthCheckResult.Unhealthy("The backplane heartbeat loop has faulted.", data: data);
                }

                var ping = await health.PingAsync(cancellationToken).ConfigureAwait(false);
                if (ping == null) {
                    return HealthCheckResult.Unhealthy("The backplane store is unreachable.", data: data);
                }

                data["pingMs"] = ping.Value.TotalMilliseconds;
                data["lastHeartbeatUtc"] = health.LastSuccessfulHeartbeatUtc?.ToString("O") ?? "never";

                var nodes = await _backplane.GetActiveNodesAsync(cancellationToken).ConfigureAwait(false);
                data["activeNodes"] = string.Join(",", nodes);
                data["activeNodeCount"] = nodes.Count;

                // Stale = more than two intervals behind: one missed write is routine under load,
                // a second one means the loop is limping toward the cluster declaring us dead.
                var last = health.LastSuccessfulHeartbeatUtc;
                if (last == null || DateTime.UtcNow - last > 2 * health.HeartbeatInterval) {
                    return HealthCheckResult.Degraded(
                        "The backplane heartbeat is stale; the cluster may declare this node dead.", data: data);
                }
            }

            return HealthCheckResult.Healthy("Backplane connected.", data);
        }
    }
}
