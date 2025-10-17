using System;
using System.Reflection;

namespace Cocoar.SignalARRR.Common.Interfaces {
    public interface ISignalARRRInterfaceCollection {
        void RegisterInterface<TInterface, TClass>() where TClass : class, TInterface;
        void RegisterInterface<TInterface, TClass>(TClass instance) where TClass : class, TInterface;
        void RegisterInterface<TInterface, TClass>(Func<IServiceProvider, TClass> factory) where TClass : class, TInterface;

        void RegisterInterface(Type interfaceType, Type instanceType);
        void RegisterInterface(Type interfaceType, object instance);
        void RegisterInterface(Type interfaceType, Func<IServiceProvider, object> factory);

        (Delegate Factory, MethodInfo MethodInfo) GetInvokeInformation(string name);
    }
}
