using System;

namespace Cocoar.SignalARRR.Server.StreamingHelper {
    public class AutoStreamOptions {
        public int? MemoryThreshold { get; internal set; }

        public string TempDirectory { get; internal set; } = null!;

        public string FilePrefix { get; internal set; } = null!;


    }

    public class AutoStreamOptionsBuilder {
        private readonly AutoStreamOptions _autoStreamOptions = new AutoStreamOptions();

        public AutoStreamOptionsBuilder WithFilePrefix(string value) {
            _autoStreamOptions.FilePrefix = value;
            return this;
        }
        public AutoStreamOptionsBuilder WithTempDirectory(string value) {
            _autoStreamOptions.TempDirectory = value;
            return this;
        }

        public AutoStreamOptionsBuilder WithMemoryThreshold(int? value) {
            _autoStreamOptions.MemoryThreshold = value;
            return this;
        }

        public static implicit operator AutoStreamOptions(AutoStreamOptionsBuilder builder) {
            return builder._autoStreamOptions;
        }

        public static implicit operator AutoStreamOptionsBuilder(Action<AutoStreamOptionsBuilder> options) {
            var optsBuilder = new AutoStreamOptionsBuilder();
            options?.Invoke(optsBuilder);
            return optsBuilder;
        }
    }
}
