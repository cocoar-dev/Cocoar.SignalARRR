using System;
using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.SignalR.Client;

namespace Cocoar.SignalARRR.Client.ExtensionMethods {
    internal static class HubConnectionExtensions {

        /// <summary>
        /// The service provider a <see cref="HubConnection"/> was built with.
        /// </summary>
        /// <remarks>
        /// SignalR offers no public way to reach it, and <c>Create(HubConnection)</c> only has the
        /// built connection to go on. <see cref="UnsafeAccessorAttribute"/> reads the private field
        /// without reflection, so it keeps working under trimming and NativeAOT, where
        /// <c>GetField</c> found nothing and <c>Create</c> crashed with a null provider. It is still
        /// bound to SignalR's field name: <c>HubConnectionServiceProviderTests</c> pins it for every
        /// target framework, so a SignalR update that renames it fails there first.
        /// </remarks>
        public static IServiceProvider GetServiceProvider(this HubConnection hubConnection)
            => ServiceProviderField(hubConnection);

        [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_serviceProvider")]
        private static extern ref IServiceProvider ServiceProviderField(HubConnection hubConnection);
    }
}
