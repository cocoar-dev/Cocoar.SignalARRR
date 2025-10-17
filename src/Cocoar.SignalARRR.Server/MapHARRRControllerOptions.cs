using Microsoft.AspNetCore.Http.Connections;

namespace Cocoar.SignalARRR.Server {
    public class MapHARRRControllerOptions: HttpConnectionDispatcherOptions {

        public bool HttpResponse { get; set; }

        public bool HttpDownloadSource { get; set; }
    }
}
