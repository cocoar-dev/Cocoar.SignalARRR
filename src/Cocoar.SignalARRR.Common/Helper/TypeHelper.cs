using System;
using System.Collections.Generic;

namespace Cocoar.SignalARRR.Common.Helper {
    public class TypeHelper {

        private static Dictionary<string, Type> TypeFromString { get; } = new Dictionary<string, Type>();
        private static object TypeFromStringLock { get; } = new object();

        public static Type FindType(string typeName) {

            if (string.IsNullOrWhiteSpace(typeName))
                return typeof(void);


            lock (TypeFromStringLock) {
                if (TypeFromString.ContainsKey(typeName))
                    return TypeFromString[typeName];

                Type foundType = null;

                if (!typeName.Contains(".")) {
                    foundType = Type.GetType($"System.{typeName}", false, true);
                }

                if (foundType == null) {
                    var assemblies = AppDomain.CurrentDomain.GetAssemblies();

                    foreach (var assembly in assemblies) {

                        foundType = assembly.GetType(typeName, false, false);
                        if (foundType != null) {
                            break;
                        }

                    }

                    if (foundType == null) {
                        foreach (var assembly in assemblies) {
                            foundType = assembly.GetType(typeName, false, true);
                            if (foundType != null) {
                                break;
                            }
                        }
                    }
                }

                TypeFromString.Add(typeName, foundType);

                return foundType;
            }

        }

    }
}
