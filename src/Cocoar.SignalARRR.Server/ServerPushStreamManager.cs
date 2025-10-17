using System;
using System.Collections.Concurrent;
using System.IO;

namespace Cocoar.SignalARRR.Server {
    internal class ServerPushStreamManager {


        private readonly ConcurrentDictionary<string, Stream> _pendingStreams = new ConcurrentDictionary<string, Stream>();


        public string StoreStreamForDownload(Stream stream, Uri baseUrl) {

            var uri = new Uri($"{baseUrl}/download/{Guid.NewGuid()}".ToLower());
            
            _pendingStreams.TryAdd(uri.ToString(), stream);
            return uri.ToString();

        }

        public Stream GetByIdentifier(string identifier) {
            if (_pendingStreams.TryGetValue(identifier, out var str)) {
                return str;
            }

            return null;
        }

        public void DisposeStream(string identifier) {
            if(_pendingStreams.TryRemove(identifier, out var stream))
            {
                stream?.Dispose();
            }
        }
    }
    
}
