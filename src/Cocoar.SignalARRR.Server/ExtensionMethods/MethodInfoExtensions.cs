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

                    // Inherit [Authorize] from the SignalR Hub if the ServerMethods class has none
                    if (!authorizeData.Any()) {
                        if (declaringType.BaseType is { IsGenericType: true } baseType
                            && baseType.GetGenericTypeDefinition() == typeof(ServerMethods<>)) {
                            var harrType = baseType.GenericTypeArguments.FirstOrDefault();
                            if (harrType != null && typeof(HARRR).IsAssignableFrom(harrType)) {
                                authorizeData = harrType.GetCustomAttributes<AuthorizeAttribute>().ToList();
                            }
                        }
                    }
                }

            }

            return authorizeData;
        }

    }
}
