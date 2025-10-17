using System;

namespace Cocoar.SignalARRR.Client {
    public partial class HARRRConnection {

        


        public void RegisterInterface<TInterface, TClass>() where TClass : class, TInterface { 
            _harrrContext.MessageHandler.RegisterInterface<TInterface, TClass>();
        }

       
        public void RegisterInterface<TInterface, TClass>(TClass instance) where TClass : class, TInterface {

            _harrrContext.MessageHandler.RegisterInterface<TInterface, TClass>(instance);
        }

        public void RegisterInterface<TInterface, TClass>(Func<IServiceProvider, TClass> factory)
            where TClass : class, TInterface {

            _harrrContext.MessageHandler.RegisterInterface<TInterface, TClass>(factory);
        }

        public void RegisterInterface(Type interfaceType, Type instanceType) {
            
            _harrrContext.MessageHandler.RegisterInterface(interfaceType, instanceType);
        }

        public void RegisterInterface(Type interfaceType, object instance) {
            _harrrContext.MessageHandler.RegisterInterface(interfaceType, instance);
        }

        public void RegisterInterface(Type interfaceType, Func<IServiceProvider, object> factory) {
            _harrrContext.MessageHandler.RegisterInterface(interfaceType, factory);
        }

       
    }
}
