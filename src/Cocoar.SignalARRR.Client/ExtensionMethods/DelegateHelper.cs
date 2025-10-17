using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

namespace Cocoar.SignalARRR.Client.ExtensionMethods {
    internal static class DelegateHelper {

        public static Delegate CreateDelegate(MethodInfo methodInfo, object target) {


            var methodParameters = methodInfo.GetParameters();
            var arguments = new List<Type>(methodParameters.Select(p => p.ParameterType));
            arguments.Add(methodInfo.ReturnType);
            
            
            if (methodInfo.ReturnType == typeof(void)) {
                var action = Expression.GetActionType(arguments.ToArray());
                return Delegate.CreateDelegate(action, target, methodInfo);
            } else {

                var func = Expression.GetFuncType(arguments.ToArray());
                return Delegate.CreateDelegate(func, target, methodInfo);
            }

        }


    }
}
