using System;
using System.Net;
using System.Threading.Tasks;
using Cocoar.Reflectensions.ExtensionMethods;
using Cocoar.Reflectensions.Helper;
using Cocoar.SignalARRR.Client.ExtensionMethods;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.SignalR.Protocol;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Cocoar.SignalARRR.Client {
    public class HARRRContext {
        private readonly IServiceProvider _serviceProvider;

        public Uri BaseUrl { get; }

        public HubProtocolType HubProtocolType { get; }
        internal Func<Task<string>> AccessTokenProvider { get; }

        public MessageHandler MessageHandler { get; }

        public HARRRContext(IServiceProvider serviceProvider, HARRRConnectionOptions options) {
            _serviceProvider = serviceProvider;

            BaseUrl = GetBaseUrl();
            HubProtocolType = Enum<HubProtocolType>.Find(_serviceProvider.GetRequiredService<IHubProtocol>().GetType().Name);
            AccessTokenProvider = GetHubConnection().GetAccessTokenProvider() ?? (() => Task.FromResult<string>(null!));
            var logger = serviceProvider.GetService<ILoggerFactory>()?.CreateLogger<MessageHandler>();
            MessageHandler = new MessageHandler(this, logger: logger);
        }

        private Uri GetBaseUrl() {
            var endPoint = _serviceProvider.GetRequiredService<EndPoint>();
            return endPoint.Reflect().GetPropertyValue<Uri>("Uri")!;
        }

        public HubConnection GetHubConnection() {
            return _serviceProvider.GetRequiredService<HubConnection>();
        }

    }
}
