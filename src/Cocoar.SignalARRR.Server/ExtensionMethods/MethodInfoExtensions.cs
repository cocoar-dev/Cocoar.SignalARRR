using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.AspNetCore.Authorization;

namespace Cocoar.SignalARRR.Server.ExtensionMethods {
    public static class MethodInfoExtensions {

        public static List<AuthorizeAttribute> GetAuthorizeData(this MethodInfo methodInfo) {

            var authorizeData = methodInfo.GetCustomAttributes<AuthorizeAttribute>().ToList();

            if (!authorizeData.Any()) {
                var declaringType = methodInfo.DeclaringType;
                if (declaringType != null) {
                    authorizeData = declaringType.GetCustomAttributes<AuthorizeAttribute>().ToList();
                }


                /// Currently disable - would use Authorize Attributes from the Signalr Hub, if no Attribute at DeclaringType exists
                //if (!authorizeData.Any()) {
                //    if (declaringType.InheritFromClass(typeof(ServerMethods<>), false, false)) {
                //        var harrType = declaringType.BaseType.GenericTypeArguments.FirstOrDefault();
                //        if (harrType.InheritFromClass<HARRR>()) {
                //            authorizeData = harrType.GetCustomAttributes<AuthorizeAttribute>().ToList();
                //        }
                //    }
                //}

            }

            return authorizeData;
        }

    }
}
