namespace Cocoar.SignalARRR.Client {
    public class HARRRConnectionOptions {
    }

    public class HARRRConnectionOptionsBuilder {

        private HARRRConnectionOptions Options { get; } = new HARRRConnectionOptions();

        public static implicit operator HARRRConnectionOptions(HARRRConnectionOptionsBuilder builder) {
            return builder?.Options!;
        }
    }
}
