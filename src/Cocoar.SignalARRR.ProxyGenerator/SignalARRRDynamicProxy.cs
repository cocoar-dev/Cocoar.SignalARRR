using System;
using System.Collections.Generic;
using System.Dynamic;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Cocoar.Reflectensions.ExtensionMethods;
using TaskExtensions = Cocoar.Reflectensions.ExtensionMethods.TaskExtensions;

namespace Cocoar.SignalARRR.ProxyGenerator {
    public class SignalARRRDynamicProxy<T> : DynamicObject {
        private readonly ProxyCreatorHelper _classCreatorHelper;


        public SignalARRRDynamicProxy(ProxyCreatorHelper classCreatorHelper) {
            _classCreatorHelper = classCreatorHelper;
        }


        public override bool TryInvokeMember(InvokeMemberBinder binder, object?[]? args, out object? result) {


            var argumentTypes = binder.Reflect().GetPropertyValue<Type[]>("TypeArguments") ?? Array.Empty<Type>();
            var parameterTypes = args?.Where(a => a != null).Select(a => a!.GetType()).ToArray() ?? Array.Empty<Type>();
            var methods = typeof(T).GetMethods().ToList();
            methods = methods.WithName(binder.Name).ToList();
            methods = methods.Where(m => m.HasGenericArgumentsLengthOf(argumentTypes.Length)).ToList();
            

            var methodsList = methods.ToList();
            var methodCount = methodsList.Count();
            if (methodCount == 0) {
                throw new Exception($"No matching Methods with Name '{binder.Name}' found!");
            }
            if (methodCount > 1) {

                throw new Exception($"Multiple matching Methods with Name '{binder.Name}' found!");

            }

            var methodInfo = methods.First();
            var methodName = $"{typeof(T).FullName}|{methodInfo.Name}";
            var isVoid = methodInfo.ReturnType == typeof(void);
            var isTask = methodInfo.ReturnType == typeof(Task);
            var isTaskOfT = methodInfo.ReturnType.IsGenericTypeOf(typeof(Task<>));
            var methodParameters = methodInfo.GetParameters().WithoutAttribute("FromServicesAttribute");
            var cancellationToken = args?.Where(a => a is CancellationToken).Cast<CancellationToken>().FirstOrDefault() ?? default;
            var isStreamingMethod = IsStreamingType(methodInfo.ReturnType);
            var genericArguments = argumentTypes.Select(arg => arg.FullName ?? string.Empty).Where(n => !string.IsNullOrEmpty(n)).ToArray();


            if (isVoid) {
                _classCreatorHelper.Send(methodName, args!, genericArguments, cancellationToken);
                result = null;
                return true;
            } else if (isTask) {
                result = _classCreatorHelper.SendAsync(methodName, args!, genericArguments, cancellationToken);
                return true;
            } else if (isStreamingMethod.IsStreamingType) {

                var stream = _classCreatorHelper.StreamAsync<T>(methodName, args!, genericArguments, cancellationToken);
                switch (isStreamingMethod.StreamingType) {
                    case StreamingType.ChannelReader: {
                            result = _classCreatorHelper.ToChannelReader(stream, cancellationToken);
                            return true;
                        }
                    case StreamingType.AsyncEnumerable: {
                            result = stream;
                            return true;
                        }
                    case StreamingType.Observable: {
                            result = AsyncEnumerable.ToObservable(stream);
                            return true;
                        }
                }
            } else {

                if (isTaskOfT) {

                    var returnType = methodInfo.ReturnType.GetGenericArguments()[0];

                    var genericInvokeMethodInfo = _classCreatorHelper.GetType().GetMethod("InvokeAsync")!.MakeGenericMethod(returnType);
                    var task = (Task?)genericInvokeMethodInfo.Invoke(_classCreatorHelper, new object[] { methodName, args!, genericArguments, cancellationToken });
                    var genericInvokeMethodInfo2 = (typeof(TaskExtensions)).GetMethod("CastToTaskOf")!.MakeGenericMethod(returnType);

                    result = task != null ? genericInvokeMethodInfo2.Invoke(null, new object[] { task }) : null;
                    return true;
                } else {
                    var returnType = methodInfo.ReturnType;
                    var genericInvokeMethodInfo = _classCreatorHelper.GetType().GetMethod("Invoke")!.MakeGenericMethod(returnType);

                    result = genericInvokeMethodInfo.Invoke(_classCreatorHelper,
                        new object[] { methodName, args!, genericArguments, cancellationToken });
                    return true;
                }
            }
            

            result = null;
            return false;

        }

        public static object CastToTaskOf(Task task, Type type) =>
            task.ContinueWith(t =>
                t.Reflect().GetPropertyValue("Result")?.Reflect().To(type)
            );


        private static (bool IsStreamingType, StreamingType StreamingType) IsStreamingType(Type type) {


            if (type.IsGenericTypeOf(typeof(IObservable<>))) {
                return (true, StreamingType.Observable);
            }

            if (type.IsGenericTypeOf(typeof(ChannelReader<>))) {
                return (true, StreamingType.ChannelReader);
            }

            if (type.IsGenericTypeOf(typeof(IAsyncEnumerable<>))) {
                return (true, StreamingType.AsyncEnumerable);
            }

            return (false, StreamingType.None);

        }

    }
}
