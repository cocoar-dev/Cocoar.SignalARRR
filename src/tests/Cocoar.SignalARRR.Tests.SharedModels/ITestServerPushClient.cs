using System.Threading.Tasks;
using Cocoar.SignalARRR.Contracts;

namespace Cocoar.SignalARRR.Tests.SharedModels {
    /// <summary>
    /// Server-to-client contract for push notifications.
    /// Defined in a separate assembly to test the cross-assembly proxy generation scenario
    /// (the standard shared-contracts pattern used by real-world consumers).
    /// </summary>
    [SignalARRRContract]
    public interface ITestServerPushClient {

        /// <summary>
        /// Fire-and-forget push notification from server to client.
        /// </summary>
        void PushNotification(string message);

        /// <summary>
        /// Fire-and-forget push with a nullable first parameter and a second string parameter.
        /// Mirrors the exact ConfigHub pattern: void ConfigUpdated(string? path, string configJson).
        /// </summary>
        void ConfigUpdated(string? path, string configJson);

        /// <summary>
        /// Server invokes this on the client and awaits a response.
        /// </summary>
        Task<string> RequestClientInfo();
    }
}
