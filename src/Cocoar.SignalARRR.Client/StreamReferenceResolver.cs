using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Cocoar.SignalARRR.Common.RemoteReferenceTypes;

namespace Cocoar.SignalARRR.Client {
    internal class StreamReferenceResolver {

        private readonly StreamReference _streamReference;
        private readonly ClientConnectionContext _connectionContext;

        public StreamReferenceResolver(StreamReference streamReference, ClientConnectionContext connectionContext) {
            _streamReference = streamReference;
            _connectionContext = connectionContext;
        }

        /// <summary>
        /// Resolve as a live Stream (not buffered) — for large files, data is read on demand.
        /// The caller is responsible for disposing the Stream.
        /// </summary>
        public async Task<Stream> ProcessStreamArgument() {
            var uri = ValidateUri();
            var httpClient = new HttpClient();
            var res = await httpClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead);
            return await res.Content.ReadAsStreamAsync();
        }

        /// <summary>
        /// Resolve as buffered byte array — loads entire content into memory.
        /// Use for small files or when random access is needed.
        /// </summary>
        public async Task<byte[]> ProcessStreamArgumentBuffered() {
            var uri = ValidateUri();
            var httpClient = new HttpClient();
            return await httpClient.GetByteArrayAsync(uri);
        }

        private Uri ValidateUri() {
            var uri = new Uri(_streamReference.Uri);
            var scheme = uri.Scheme.ToLower();
            if (scheme != "http" && scheme != "https") {
                throw new NotSupportedException($"StreamReference: unsupported URI scheme '{uri.Scheme}'");
            }
            return uri;
        }
    }
}
