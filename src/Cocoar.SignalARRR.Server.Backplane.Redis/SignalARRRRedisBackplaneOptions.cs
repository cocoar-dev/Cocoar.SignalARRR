using System;

namespace Cocoar.SignalARRR.Server {
    public sealed class SignalARRRRedisBackplaneOptions {
        public string ConnectionString { get; set; } = string.Empty;
        public string ChannelPrefix { get; set; } = "signalarrr";
        public string NodeId { get; set; } = $"{Environment.MachineName}-{Guid.NewGuid():N}";
        public TimeSpan InvokeTimeout { get; set; } = TimeSpan.FromSeconds(15);
        public TimeSpan HeartbeatInterval { get; set; } = TimeSpan.FromSeconds(5);
        public TimeSpan NodeTimeout { get; set; } = TimeSpan.FromSeconds(20);
    }

    public sealed class SignalARRRRedisBackplaneOptionsBuilder {
        private readonly SignalARRRRedisBackplaneOptions _options = new SignalARRRRedisBackplaneOptions();

        public SignalARRRRedisBackplaneOptionsBuilder WithConnectionString(string connectionString) {
            _options.ConnectionString = connectionString;
            return this;
        }

        public SignalARRRRedisBackplaneOptionsBuilder WithChannelPrefix(string channelPrefix) {
            _options.ChannelPrefix = channelPrefix;
            return this;
        }

        public SignalARRRRedisBackplaneOptionsBuilder WithNodeId(string nodeId) {
            _options.NodeId = nodeId;
            return this;
        }

        public SignalARRRRedisBackplaneOptionsBuilder WithInvokeTimeout(TimeSpan timeout) {
            _options.InvokeTimeout = timeout;
            return this;
        }

        public SignalARRRRedisBackplaneOptionsBuilder WithHeartbeatInterval(TimeSpan interval) {
            _options.HeartbeatInterval = interval;
            return this;
        }

        public SignalARRRRedisBackplaneOptionsBuilder WithNodeTimeout(TimeSpan timeout) {
            _options.NodeTimeout = timeout;
            return this;
        }

        internal SignalARRRRedisBackplaneOptions Build() => _options;
    }
}
