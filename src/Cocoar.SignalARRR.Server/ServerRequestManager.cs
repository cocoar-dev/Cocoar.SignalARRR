//using System;
//using System.Collections.Concurrent;
//using System.Threading.Tasks;
//using Microsoft.AspNetCore.Http;
//using Newtonsoft.Json.Linq;

//namespace Cocoar.SignalARRR.Server {
//    internal class ServerRequestManager {


//        private readonly ConcurrentDictionary<Guid, RequestContext> _pendingMethodCalls = new ConcurrentDictionary<Guid, RequestContext>();


//        public TaskCompletionSource<JToken> AddRequest(Guid id) {

            
//            var requestContext = new RequestContext();
//            requestContext.RequestType = RequestType.Default;
//            requestContext.TaskCompletionSource = new TaskCompletionSource<JToken>();
//            if (_pendingMethodCalls.TryAdd(id, requestContext)) {
//                return requestContext.TaskCompletionSource;
//            }

//            throw new Exception("Can't add Request to Manager");
//        }

//        public TaskCompletionSource<JToken> AddProxyRequest(Guid id, HttpContext httpContext) {

//            var requestContext = new RequestContext();
//            requestContext.RequestType = RequestType.Proxy;
//            requestContext.TaskCompletionSource = new TaskCompletionSource<JToken>();
//            requestContext.HttpContext = httpContext;
//            if (_pendingMethodCalls.TryAdd(id, requestContext)) {
//                return requestContext.TaskCompletionSource;
//            }

//            throw new Exception("Can't add Request to Manager");
//        }


//        public void CompleteRequest(Guid id, JToken payload, string error) {

            
//            if (_pendingMethodCalls.TryRemove(id, out var requestContext)) {

//                if (!string.IsNullOrEmpty(error)) {
//                    requestContext.TaskCompletionSource.SetException(new Exception(error));
//                } else {
//                    requestContext.TaskCompletionSource.SetResult(payload);
//                }
                
//            }
//        }

//        public void CompleteProxyRequest(Guid id) {

//            if (_pendingMethodCalls.TryRemove(id, out var requestContext)) {
//                requestContext.TaskCompletionSource.SetResult(null);
//            }
//        }

//        public void CancelRequest(Guid id) {
//            if (_pendingMethodCalls.TryRemove(id, out var requestContext)) {
//                requestContext.TaskCompletionSource.SetCanceled();
//            }
//        }

//        public RequestType GetResponseType(Guid id) {
//            if (_pendingMethodCalls.TryGetValue(id, out var requestContext)) {
//                return requestContext.RequestType;
//            }

//            return RequestType.Invalid;

//        }

//        public HttpContext GetHttpContext(Guid id) {
//            if (_pendingMethodCalls.TryGetValue(id, out var requestContext)) {
//                return requestContext.HttpContext;
//            }

//            return null;
//        }
//    }

//    public class RequestContext {
//        public RequestType RequestType { get; set; }

//        public HttpContext HttpContext { get; set; }

//        public TaskCompletionSource<JToken> TaskCompletionSource { get; set; }
//    }

//    public enum RequestType {
        
//        Invalid,
//        Default,
//        Proxy
//    }
//}
