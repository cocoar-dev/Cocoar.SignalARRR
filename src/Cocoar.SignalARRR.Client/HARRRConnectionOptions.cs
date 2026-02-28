namespace Cocoar.SignalARRR.Client {
    public class HARRRConnectionOptions {

        public bool HttpResponse { get; set; }
    }

    public class HARRRConnectionOptionsBuilder {

        private HARRRConnectionOptions Options { get; } = new HARRRConnectionOptions();


        public HARRRConnectionOptionsBuilder UseHttpResponse(bool value = true) {
            Options.HttpResponse = value;
            return this;
        }

        public static implicit operator HARRRConnectionOptions(HARRRConnectionOptionsBuilder builder) {
            return builder?.Options!;
        }
    }
}
