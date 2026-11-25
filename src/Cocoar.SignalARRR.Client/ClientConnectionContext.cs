using System;
using System.Text.Json;
using System.Threading.Tasks;
using Cocoar.SignalARRR.Common.Serialization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Cocoar.SignalARRR.Client {
    internal class ClientConnectionContext {
        private readonly IServiceProvider _serviceProvider;

        /// <summary>
        /// The credential SignalARRR sends — messages, challenge answers, file transfers. Configured
        /// through <see cref="HARRRConnectionOptions.MessageCredential"/>, never taken from SignalR.
        /// </summary>
        internal Func<Task<string>> AuthorizationProvider { get; }

        public MessageHandler MessageHandler { get; }

        /// <summary>
        /// The hub protocol's JSON options, the ones SignalR reads and writes every payload with.
        /// SignalARRR's own contracts (<see cref="SignalARRRJsonContext"/>) are appended here, once
        /// per connection, behind whatever the app configured — so an AOT app only has to declare
        /// its own types. A connection handed to <c>Create(HubConnection)</c> after it has already
        /// sent something has read-only options; those stay as they are.
        /// </summary>
        public JsonSerializerOptions JsonPayloadOptions { get; }

        internal ClientConnectionContext(IServiceProvider serviceProvider, HARRRConnectionOptions options) {
            _serviceProvider = serviceProvider;
            JsonPayloadOptions = serviceProvider.GetService<IOptions<JsonHubProtocolOptions>>()?.Value.PayloadSerializerOptions
                ?? new JsonSerializerOptions(JsonSerializerDefaults.Web);
            SignalARRRJson.AddTo(JsonPayloadOptions);

            AuthorizationProvider = options?.ResolveMessageCredential() ?? (() => Task.FromResult<string>(null!));
            var logger = serviceProvider.GetService<ILoggerFactory>()?.CreateLogger<MessageHandler>();
            MessageHandler = new MessageHandler(this, new JsonProtocolSerializer(JsonPayloadOptions), logger);
        }

        public HubConnection GetHubConnection() {
            return _serviceProvider.GetRequiredService<HubConnection>();
        }

    }
}
