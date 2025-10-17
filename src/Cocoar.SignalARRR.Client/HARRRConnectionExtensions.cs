// Decompiled with JetBrains decompiler
// Type: Microsoft.AspNetCore.SignalR.Client.HubConnectionExtensions
// Assembly: Microsoft.AspNetCore.SignalR.Client.Core, Version=3.0.0.0, Culture=neutral, PublicKeyToken=adb9793829ddae60
// MVID: D8128E0F-26C6-406A-AD81-7E068067389C
// Assembly location: C:\Users\bwindisc\.nuget\packages\microsoft.aspnetcore.signalr.client.core\3.0.0\lib\netstandard2.1\Microsoft.AspNetCore.SignalR.Client.Core.dll

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Cocoar.SignalARRR.Client {

    public static class HARRRConnectionExtensions {
        private static IDisposable On(
          this HARRRConnection harrrConnection,
          string methodName,
          Type[] parameterTypes,
          Action<object[]> handler) {
            return harrrConnection.On(methodName, parameterTypes, (Func<object[], object, Task>)((parameters, state) =>
            {
                ((Action<object[]>)state)(parameters);
                return Task.CompletedTask;
            }), (object)handler);
        }

        /// <summary>
        /// Registers a handler that will be invoked when the hub method with the specified method name is invoked.
        /// </summary>
        /// <param name="harrrConnection">The hub connection.</param>
        /// <param name="methodName">The name of the hub method to define.</param>
        /// <param name="handler">The handler that will be raised when the hub method is invoked.</param>
        /// <returns>A subscription that can be disposed to unsubscribe from the hub method.</returns>
        public static IDisposable On(
          this HARRRConnection harrrConnection,
          string methodName,
          Action handler) {
            if (harrrConnection == null)
                throw new ArgumentNullException(nameof(harrrConnection));
            return harrrConnection.On(methodName, Type.EmptyTypes, (Action<object[]>)(args => handler()));
        }

        /// <summary>
        /// Registers a handler that will be invoked when the hub method with the specified method name is invoked.
        /// </summary>
        /// <typeparam name="T1">The first argument type.</typeparam>
        /// <param name="harrrConnection">The hub connection.</param>
        /// <param name="methodName">The name of the hub method to define.</param>
        /// <param name="handler">The handler that will be raised when the hub method is invoked.</param>
        /// <returns>A subscription that can be disposed to unsubscribe from the hub method.</returns>
        public static IDisposable On<T1>(
          this HARRRConnection harrrConnection,
          string methodName,
          Action<T1> handler) {
            if (harrrConnection == null)
                throw new ArgumentNullException(nameof(harrrConnection));
            return harrrConnection.On(methodName, new Type[1]
            {
        typeof (T1)
            }, (Action<object[]>)(args => handler((T1)args[0])));
        }

        /// <summary>
        /// Registers a handler that will be invoked when the hub method with the specified method name is invoked.
        /// </summary>
        /// <typeparam name="T1">The first argument type.</typeparam>
        /// <typeparam name="T2">The second argument type.</typeparam>
        /// <param name="harrrConnection">The hub connection.</param>
        /// <param name="methodName">The name of the hub method to define.</param>
        /// <param name="handler">The handler that will be raised when the hub method is invoked.</param>
        /// <returns>A subscription that can be disposed to unsubscribe from the hub method.</returns>
        public static IDisposable On<T1, T2>(
          this HARRRConnection harrrConnection,
          string methodName,
          Action<T1, T2> handler) {
            if (harrrConnection == null)
                throw new ArgumentNullException(nameof(harrrConnection));
            return harrrConnection.On(methodName, new Type[2]
            {
        typeof (T1),
        typeof (T2)
            }, (Action<object[]>)(args => handler((T1)args[0], (T2)args[1])));
        }

        /// <summary>
        /// Registers a handler that will be invoked when the hub method with the specified method name is invoked.
        /// </summary>
        /// <typeparam name="T1">The first argument type.</typeparam>
        /// <typeparam name="T2">The second argument type.</typeparam>
        /// <typeparam name="T3">The third argument type.</typeparam>
        /// <param name="harrrConnection">The hub connection.</param>
        /// <param name="methodName">The name of the hub method to define.</param>
        /// <param name="handler">The handler that will be raised when the hub method is invoked.</param>
        /// <returns>A subscription that can be disposed to unsubscribe from the hub method.</returns>
        public static IDisposable On<T1, T2, T3>(
          this HARRRConnection harrrConnection,
          string methodName,
          Action<T1, T2, T3> handler) {
            if (harrrConnection == null)
                throw new ArgumentNullException(nameof(harrrConnection));
            return harrrConnection.On(methodName, new Type[3]
            {
        typeof (T1),
        typeof (T2),
        typeof (T3)
            }, (Action<object[]>)(args => handler((T1)args[0], (T2)args[1], (T3)args[2])));
        }

        /// <summary>
        /// Registers a handler that will be invoked when the hub method with the specified method name is invoked.
        /// </summary>
        /// <typeparam name="T1">The first argument type.</typeparam>
        /// <typeparam name="T2">The second argument type.</typeparam>
        /// <typeparam name="T3">The third argument type.</typeparam>
        /// <typeparam name="T4">The fourth argument type.</typeparam>
        /// <param name="harrrConnection">The hub connection.</param>
        /// <param name="methodName">The name of the hub method to define.</param>
        /// <param name="handler">The handler that will be raised when the hub method is invoked.</param>
        /// <returns>A subscription that can be disposed to unsubscribe from the hub method.</returns>
        public static IDisposable On<T1, T2, T3, T4>(
          this HARRRConnection harrrConnection,
          string methodName,
          Action<T1, T2, T3, T4> handler) {
            if (harrrConnection == null)
                throw new ArgumentNullException(nameof(harrrConnection));
            return harrrConnection.On(methodName, new Type[4]
            {
        typeof (T1),
        typeof (T2),
        typeof (T3),
        typeof (T4)
            }, (Action<object[]>)(args => handler((T1)args[0], (T2)args[1], (T3)args[2], (T4)args[3])));
        }

        /// <summary>
        /// Registers a handler that will be invoked when the hub method with the specified method name is invoked.
        /// </summary>
        /// <typeparam name="T1">The first argument type.</typeparam>
        /// <typeparam name="T2">The second argument type.</typeparam>
        /// <typeparam name="T3">The third argument type.</typeparam>
        /// <typeparam name="T4">The fourth argument type.</typeparam>
        /// <typeparam name="T5">The fifth argument type.</typeparam>
        /// <param name="harrrConnection">The hub connection.</param>
        /// <param name="methodName">The name of the hub method to define.</param>
        /// <param name="handler">The handler that will be raised when the hub method is invoked.</param>
        /// <returns>A subscription that can be disposed to unsubscribe from the hub method.</returns>
        public static IDisposable On<T1, T2, T3, T4, T5>(
          this HARRRConnection harrrConnection,
          string methodName,
          Action<T1, T2, T3, T4, T5> handler) {
            if (harrrConnection == null)
                throw new ArgumentNullException(nameof(harrrConnection));
            return harrrConnection.On(methodName, new Type[5]
            {
        typeof (T1),
        typeof (T2),
        typeof (T3),
        typeof (T4),
        typeof (T5)
            }, (Action<object[]>)(args => handler((T1)args[0], (T2)args[1], (T3)args[2], (T4)args[3], (T5)args[4])));
        }

        /// <summary>
        /// Registers a handler that will be invoked when the hub method with the specified method name is invoked.
        /// </summary>
        /// <typeparam name="T1">The first argument type.</typeparam>
        /// <typeparam name="T2">The second argument type.</typeparam>
        /// <typeparam name="T3">The third argument type.</typeparam>
        /// <typeparam name="T4">The fourth argument type.</typeparam>
        /// <typeparam name="T5">The fifth argument type.</typeparam>
        /// <typeparam name="T6">The sixth argument type.</typeparam>
        /// <param name="harrrConnection">The hub connection.</param>
        /// <param name="methodName">The name of the hub method to define.</param>
        /// <param name="handler">The handler that will be raised when the hub method is invoked.</param>
        /// <returns>A subscription that can be disposed to unsubscribe from the hub method.</returns>
        public static IDisposable On<T1, T2, T3, T4, T5, T6>(
          this HARRRConnection harrrConnection,
          string methodName,
          Action<T1, T2, T3, T4, T5, T6> handler) {
            if (harrrConnection == null)
                throw new ArgumentNullException(nameof(harrrConnection));
            return harrrConnection.On(methodName, new Type[6]
            {
        typeof (T1),
        typeof (T2),
        typeof (T3),
        typeof (T4),
        typeof (T5),
        typeof (T6)
            }, (Action<object[]>)(args => handler((T1)args[0], (T2)args[1], (T3)args[2], (T4)args[3], (T5)args[4], (T6)args[5])));
        }

        /// <summary>
        /// Registers a handler that will be invoked when the hub method with the specified method name is invoked.
        /// </summary>
        /// <typeparam name="T1">The first argument type.</typeparam>
        /// <typeparam name="T2">The second argument type.</typeparam>
        /// <typeparam name="T3">The third argument type.</typeparam>
        /// <typeparam name="T4">The fourth argument type.</typeparam>
        /// <typeparam name="T5">The fifth argument type.</typeparam>
        /// <typeparam name="T6">The sixth argument type.</typeparam>
        /// <typeparam name="T7">The seventh argument type.</typeparam>
        /// <param name="harrrConnection">The hub connection.</param>
        /// <param name="methodName">The name of the hub method to define.</param>
        /// <param name="handler">The handler that will be raised when the hub method is invoked.</param>
        /// <returns>A subscription that can be disposed to unsubscribe from the hub method.</returns>
        public static IDisposable On<T1, T2, T3, T4, T5, T6, T7>(
          this HARRRConnection harrrConnection,
          string methodName,
          Action<T1, T2, T3, T4, T5, T6, T7> handler) {
            if (harrrConnection == null)
                throw new ArgumentNullException(nameof(harrrConnection));
            return harrrConnection.On(methodName, new Type[7]
            {
        typeof (T1),
        typeof (T2),
        typeof (T3),
        typeof (T4),
        typeof (T5),
        typeof (T6),
        typeof (T7)
            }, (Action<object[]>)(args => handler((T1)args[0], (T2)args[1], (T3)args[2], (T4)args[3], (T5)args[4], (T6)args[5], (T7)args[6])));
        }

        /// <summary>
        /// Registers a handler that will be invoked when the hub method with the specified method name is invoked.
        /// </summary>
        /// <typeparam name="T1">The first argument type.</typeparam>
        /// <typeparam name="T2">The second argument type.</typeparam>
        /// <typeparam name="T3">The third argument type.</typeparam>
        /// <typeparam name="T4">The fourth argument type.</typeparam>
        /// <typeparam name="T5">The fifth argument type.</typeparam>
        /// <typeparam name="T6">The sixth argument type.</typeparam>
        /// <typeparam name="T7">The seventh argument type.</typeparam>
        /// <typeparam name="T8">The eighth argument type.</typeparam>
        /// <param name="harrrConnection">The hub connection.</param>
        /// <param name="methodName">The name of the hub method to define.</param>
        /// <param name="handler">The handler that will be raised when the hub method is invoked.</param>
        /// <returns>A subscription that can be disposed to unsubscribe from the hub method.</returns>
        public static IDisposable On<T1, T2, T3, T4, T5, T6, T7, T8>(
          this HARRRConnection harrrConnection,
          string methodName,
          Action<T1, T2, T3, T4, T5, T6, T7, T8> handler) {
            if (harrrConnection == null)
                throw new ArgumentNullException(nameof(harrrConnection));
            return harrrConnection.On(methodName, new Type[8]
            {
        typeof (T1),
        typeof (T2),
        typeof (T3),
        typeof (T4),
        typeof (T5),
        typeof (T6),
        typeof (T7),
        typeof (T8)
            }, (Action<object[]>)(args => handler((T1)args[0], (T2)args[1], (T3)args[2], (T4)args[3], (T5)args[4], (T6)args[5], (T7)args[6], (T8)args[7])));
        }

        /// <summary>
        /// Registers a handler that will be invoked when the hub method with the specified method name is invoked.
        /// </summary>
        /// <param name="harrrConnection">The hub connection.</param>
        /// <param name="methodName">The name of the hub method to define.</param>
        /// <param name="parameterTypes">The parameters types expected by the hub method.</param>
        /// <param name="handler">The handler that will be raised when the hub method is invoked.</param>
        /// <returns>A subscription that can be disposed to unsubscribe from the hub method.</returns>
        public static IDisposable On(
          this HARRRConnection harrrConnection,
          string methodName,
          Type[] parameterTypes,
          Func<object[], Task> handler) {
            return harrrConnection.On(methodName, parameterTypes, (Func<object[], object, Task>)((parameters, state) => ((Func<object[], Task>)state)(parameters)), (object)handler);
        }

        /// <summary>
        /// Registers a handler that will be invoked when the hub method with the specified method name is invoked.
        /// </summary>
        /// <param name="harrrConnection">The hub connection.</param>
        /// <param name="methodName">The name of the hub method to define.</param>
        /// <param name="handler">The handler that will be raised when the hub method is invoked.</param>
        /// <returns>A subscription that can be disposed to unsubscribe from the hub method.</returns>
        public static IDisposable On(
          this HARRRConnection harrrConnection,
          string methodName,
          Func<Task> handler) {
            if (harrrConnection == null)
                throw new ArgumentNullException(nameof(harrrConnection));
            return harrrConnection.On(methodName, Type.EmptyTypes, (Func<object[], Task>)(args => handler()));
        }

        /// <summary>
        /// Registers a handler that will be invoked when the hub method with the specified method name is invoked.
        /// </summary>
        /// <typeparam name="T1">The first argument type.</typeparam>
        /// <param name="harrrConnection">The hub connection.</param>
        /// <param name="methodName">The name of the hub method to define.</param>
        /// <param name="handler">The handler that will be raised when the hub method is invoked.</param>
        /// <returns>A subscription that can be disposed to unsubscribe from the hub method.</returns>
        public static IDisposable On<T1>(
          this HARRRConnection harrrConnection,
          string methodName,
          Func<T1, Task> handler) {
            if (harrrConnection == null)
                throw new ArgumentNullException(nameof(harrrConnection));
            return harrrConnection.On(methodName, new Type[1]
            {
        typeof (T1)
            }, (Func<object[], Task>)(args => handler((T1)args[0])));
        }

        /// <summary>
        /// Registers a handler that will be invoked when the hub method with the specified method name is invoked.
        /// </summary>
        /// <typeparam name="T1">The first argument type.</typeparam>
        /// <typeparam name="T2">The second argument type.</typeparam>
        /// <param name="harrrConnection">The hub connection.</param>
        /// <param name="methodName">The name of the hub method to define.</param>
        /// <param name="handler">The handler that will be raised when the hub method is invoked.</param>
        /// <returns>A subscription that can be disposed to unsubscribe from the hub method.</returns>
        public static IDisposable On<T1, T2>(
          this HARRRConnection harrrConnection,
          string methodName,
          Func<T1, T2, Task> handler) {
            if (harrrConnection == null)
                throw new ArgumentNullException(nameof(harrrConnection));
            return harrrConnection.On(methodName, new Type[2]
            {
        typeof (T1),
        typeof (T2)
            }, (Func<object[], Task>)(args => handler((T1)args[0], (T2)args[1])));
        }

        /// <summary>
        /// Registers a handler that will be invoked when the hub method with the specified method name is invoked.
        /// </summary>
        /// <typeparam name="T1">The first argument type.</typeparam>
        /// <typeparam name="T2">The second argument type.</typeparam>
        /// <typeparam name="T3">The third argument type.</typeparam>
        /// <param name="harrrConnection">The hub connection.</param>
        /// <param name="methodName">The name of the hub method to define.</param>
        /// <param name="handler">The handler that will be raised when the hub method is invoked.</param>
        /// <returns>A subscription that can be disposed to unsubscribe from the hub method.</returns>
        public static IDisposable On<T1, T2, T3>(
          this HARRRConnection harrrConnection,
          string methodName,
          Func<T1, T2, T3, Task> handler) {
            if (harrrConnection == null)
                throw new ArgumentNullException(nameof(harrrConnection));
            return harrrConnection.On(methodName, new Type[3]
            {
        typeof (T1),
        typeof (T2),
        typeof (T3)
            }, (Func<object[], Task>)(args => handler((T1)args[0], (T2)args[1], (T3)args[2])));
        }

        /// <summary>
        /// Registers a handler that will be invoked when the hub method with the specified method name is invoked.
        /// </summary>
        /// <typeparam name="T1">The first argument type.</typeparam>
        /// <typeparam name="T2">The second argument type.</typeparam>
        /// <typeparam name="T3">The third argument type.</typeparam>
        /// <typeparam name="T4">The fourth argument type.</typeparam>
        /// <param name="harrrConnection">The hub connection.</param>
        /// <param name="methodName">The name of the hub method to define.</param>
        /// <param name="handler">The handler that will be raised when the hub method is invoked.</param>
        /// <returns>A subscription that can be disposed to unsubscribe from the hub method.</returns>
        public static IDisposable On<T1, T2, T3, T4>(
          this HARRRConnection harrrConnection,
          string methodName,
          Func<T1, T2, T3, T4, Task> handler) {
            if (harrrConnection == null)
                throw new ArgumentNullException(nameof(harrrConnection));
            return harrrConnection.On(methodName, new Type[4]
            {
        typeof (T1),
        typeof (T2),
        typeof (T3),
        typeof (T4)
            }, (Func<object[], Task>)(args => handler((T1)args[0], (T2)args[1], (T3)args[2], (T4)args[3])));
        }

        /// <summary>
        /// Registers a handler that will be invoked when the hub method with the specified method name is invoked.
        /// </summary>
        /// <typeparam name="T1">The first argument type.</typeparam>
        /// <typeparam name="T2">The second argument type.</typeparam>
        /// <typeparam name="T3">The third argument type.</typeparam>
        /// <typeparam name="T4">The fourth argument type.</typeparam>
        /// <typeparam name="T5">The fifth argument type.</typeparam>
        /// <param name="harrrConnection">The hub connection.</param>
        /// <param name="methodName">The name of the hub method to define.</param>
        /// <param name="handler">The handler that will be raised when the hub method is invoked.</param>
        /// <returns>A subscription that can be disposed to unsubscribe from the hub method.</returns>
        public static IDisposable On<T1, T2, T3, T4, T5>(
          this HARRRConnection harrrConnection,
          string methodName,
          Func<T1, T2, T3, T4, T5, Task> handler) {
            if (harrrConnection == null)
                throw new ArgumentNullException(nameof(harrrConnection));
            return harrrConnection.On(methodName, new Type[5]
            {
        typeof (T1),
        typeof (T2),
        typeof (T3),
        typeof (T4),
        typeof (T5)
            }, (Func<object[], Task>)(args => handler((T1)args[0], (T2)args[1], (T3)args[2], (T4)args[3], (T5)args[4])));
        }

        /// <summary>
        /// Registers a handler that will be invoked when the hub method with the specified method name is invoked.
        /// </summary>
        /// <typeparam name="T1">The first argument type.</typeparam>
        /// <typeparam name="T2">The second argument type.</typeparam>
        /// <typeparam name="T3">The third argument type.</typeparam>
        /// <typeparam name="T4">The fourth argument type.</typeparam>
        /// <typeparam name="T5">The fifth argument type.</typeparam>
        /// <typeparam name="T6">The sixth argument type.</typeparam>
        /// <param name="harrrConnection">The hub connection.</param>
        /// <param name="methodName">The name of the hub method to define.</param>
        /// <param name="handler">The handler that will be raised when the hub method is invoked.</param>
        /// <returns>A subscription that can be disposed to unsubscribe from the hub method.</returns>
        public static IDisposable On<T1, T2, T3, T4, T5, T6>(
          this HARRRConnection harrrConnection,
          string methodName,
          Func<T1, T2, T3, T4, T5, T6, Task> handler) {
            if (harrrConnection == null)
                throw new ArgumentNullException(nameof(harrrConnection));
            return harrrConnection.On(methodName, new Type[6]
            {
        typeof (T1),
        typeof (T2),
        typeof (T3),
        typeof (T4),
        typeof (T5),
        typeof (T6)
            }, (Func<object[], Task>)(args => handler((T1)args[0], (T2)args[1], (T3)args[2], (T4)args[3], (T5)args[4], (T6)args[5])));
        }

        /// <summary>
        /// Registers a handler that will be invoked when the hub method with the specified method name is invoked.
        /// </summary>
        /// <typeparam name="T1">The first argument type.</typeparam>
        /// <typeparam name="T2">The second argument type.</typeparam>
        /// <typeparam name="T3">The third argument type.</typeparam>
        /// <typeparam name="T4">The fourth argument type.</typeparam>
        /// <typeparam name="T5">The fifth argument type.</typeparam>
        /// <typeparam name="T6">The sixth argument type.</typeparam>
        /// <typeparam name="T7">The seventh argument type.</typeparam>
        /// <param name="harrrConnection">The hub connection.</param>
        /// <param name="methodName">The name of the hub method to define.</param>
        /// <param name="handler">The handler that will be raised when the hub method is invoked.</param>
        /// <returns>A subscription that can be disposed to unsubscribe from the hub method.</returns>
        public static IDisposable On<T1, T2, T3, T4, T5, T6, T7>(
          this HARRRConnection harrrConnection,
          string methodName,
          Func<T1, T2, T3, T4, T5, T6, T7, Task> handler) {
            if (harrrConnection == null)
                throw new ArgumentNullException(nameof(harrrConnection));
            return harrrConnection.On(methodName, new Type[7]
            {
        typeof (T1),
        typeof (T2),
        typeof (T3),
        typeof (T4),
        typeof (T5),
        typeof (T6),
        typeof (T7)
            }, (Func<object[], Task>)(args => handler((T1)args[0], (T2)args[1], (T3)args[2], (T4)args[3], (T5)args[4], (T6)args[5], (T7)args[6])));
        }

        /// <summary>
        /// Registers a handler that will be invoked when the hub method with the specified method name is invoked.
        /// </summary>
        /// <typeparam name="T1">The first argument type.</typeparam>
        /// <typeparam name="T2">The second argument type.</typeparam>
        /// <typeparam name="T3">The third argument type.</typeparam>
        /// <typeparam name="T4">The fourth argument type.</typeparam>
        /// <typeparam name="T5">The fifth argument type.</typeparam>
        /// <typeparam name="T6">The sixth argument type.</typeparam>
        /// <typeparam name="T7">The seventh argument type.</typeparam>
        /// <typeparam name="T8">The eighth argument type.</typeparam>
        /// <param name="harrrConnection">The hub connection.</param>
        /// <param name="methodName">The name of the hub method to define.</param>
        /// <param name="handler">The handler that will be raised when the hub method is invoked.</param>
        /// <returns>A subscription that can be disposed to unsubscribe from the hub method.</returns>
        public static IDisposable On<T1, T2, T3, T4, T5, T6, T7, T8>(
          this HARRRConnection harrrConnection,
          string methodName,
          Func<T1, T2, T3, T4, T5, T6, T7, T8, Task> handler) {
            if (harrrConnection == null)
                throw new ArgumentNullException(nameof(harrrConnection));
            return harrrConnection.On(methodName, new Type[8]
            {
        typeof (T1),
        typeof (T2),
        typeof (T3),
        typeof (T4),
        typeof (T5),
        typeof (T6),
        typeof (T7),
        typeof (T8)
            }, (Func<object[], Task>)(args => handler((T1)args[0], (T2)args[1], (T3)args[2], (T4)args[3], (T5)args[4], (T6)args[5], (T7)args[6], (T8)args[7])));
        }

        
        
        /// <summary>
        /// Invokes a hub method on the server using the specified method name.
        /// </summary>
        /// <param name="harrrConnection">The hub connection.</param>
        /// <param name="methodName">The name of the server method to invoke.</param>
        /// <param name="cancellationToken">The token to monitor for cancellation requests. The default value is <see cref="P:System.Threading.CancellationToken.None" />.</param>
        /// <returns>A <see cref="T:System.Threading.Tasks.Task`1" /> that represents the asynchronous invoke.</returns>
        public static Task InvokeAsync(
          this HARRRConnection harrrConnection,
          string methodName,
          CancellationToken cancellationToken = default(CancellationToken)) {
            return harrrConnection.InvokeCoreAsync(methodName, Array.Empty<object>(), cancellationToken);
        }

        /// <summary>
        /// Invokes a hub method on the server using the specified method name and argument.
        /// </summary>
        /// <param name="harrrConnection">The hub connection.</param>
        /// <param name="methodName">The name of the server method to invoke.</param>
        /// <param name="arg1">The first argument.</param>
        /// <param name="cancellationToken">The token to monitor for cancellation requests. The default value is <see cref="P:System.Threading.CancellationToken.None" />.</param>
        /// <returns>A <see cref="T:System.Threading.Tasks.Task" /> that represents the asynchronous invoke.</returns>
        public static Task InvokeAsync(
          this HARRRConnection harrrConnection,
          string methodName,
          object arg1,
          CancellationToken cancellationToken = default(CancellationToken)) {
            return harrrConnection.InvokeCoreAsync(methodName, new object[1]
            {
        arg1
            }, cancellationToken);
        }

        /// <summary>
        /// Invokes a hub method on the server using the specified method name and arguments.
        /// </summary>
        /// <param name="harrrConnection">The hub connection.</param>
        /// <param name="methodName">The name of the server method to invoke.</param>
        /// <param name="arg1">The first argument.</param>
        /// <param name="arg2">The second argument.</param>
        /// <param name="cancellationToken">The token to monitor for cancellation requests. The default value is <see cref="P:System.Threading.CancellationToken.None" />.</param>
        /// <returns>A <see cref="T:System.Threading.Tasks.Task" /> that represents the asynchronous invoke.</returns>
        public static Task InvokeAsync(
          this HARRRConnection harrrConnection,
          string methodName,
          object arg1,
          object arg2,
          CancellationToken cancellationToken = default(CancellationToken)) {
            return harrrConnection.InvokeCoreAsync(methodName, new object[2]
            {
        arg1,
        arg2
            }, cancellationToken);
        }

        /// <summary>
        /// Invokes a hub method on the server using the specified method name and arguments.
        /// </summary>
        /// <param name="harrrConnection">The hub connection.</param>
        /// <param name="methodName">The name of the server method to invoke.</param>
        /// <param name="arg1">The first argument.</param>
        /// <param name="arg2">The second argument.</param>
        /// <param name="arg3">The third argument.</param>
        /// <param name="cancellationToken">The token to monitor for cancellation requests. The default value is <see cref="P:System.Threading.CancellationToken.None" />.</param>
        /// <returns>A <see cref="T:System.Threading.Tasks.Task" /> that represents the asynchronous invoke.</returns>
        public static Task InvokeAsync(
          this HARRRConnection harrrConnection,
          string methodName,
          object arg1,
          object arg2,
          object arg3,
          CancellationToken cancellationToken = default(CancellationToken)) {
            return harrrConnection.InvokeCoreAsync(methodName, new object[3]
            {
        arg1,
        arg2,
        arg3
            }, cancellationToken);
        }

        /// <summary>
        /// Invokes a hub method on the server using the specified method name and arguments.
        /// </summary>
        /// <param name="harrrConnection">The hub connection.</param>
        /// <param name="methodName">The name of the server method to invoke.</param>
        /// <param name="arg1">The first argument.</param>
        /// <param name="arg2">The second argument.</param>
        /// <param name="arg3">The third argument.</param>
        /// <param name="arg4">The fourth argument.</param>
        /// <param name="cancellationToken">The token to monitor for cancellation requests. The default value is <see cref="P:System.Threading.CancellationToken.None" />.</param>
        /// <returns>A <see cref="T:System.Threading.Tasks.Task" /> that represents the asynchronous invoke.</returns>
        public static Task InvokeAsync(
          this HARRRConnection harrrConnection,
          string methodName,
          object arg1,
          object arg2,
          object arg3,
          object arg4,
          CancellationToken cancellationToken = default(CancellationToken)) {
            return harrrConnection.InvokeCoreAsync(methodName, new object[4]
            {
        arg1,
        arg2,
        arg3,
        arg4
            }, cancellationToken);
        }

        /// <summary>
        /// Invokes a hub method on the server using the specified method name and arguments.
        /// </summary>
        /// <param name="harrrConnection">The hub connection.</param>
        /// <param name="methodName">The name of the server method to invoke.</param>
        /// <param name="arg1">The first argument.</param>
        /// <param name="arg2">The second argument.</param>
        /// <param name="arg3">The third argument.</param>
        /// <param name="arg4">The fourth argument.</param>
        /// <param name="arg5">The fifth argument.</param>
        /// <param name="cancellationToken">The token to monitor for cancellation requests. The default value is <see cref="P:System.Threading.CancellationToken.None" />.</param>
        /// <returns>A <see cref="T:System.Threading.Tasks.Task" /> that represents the asynchronous invoke.</returns>
        public static Task InvokeAsync(
          this HARRRConnection harrrConnection,
          string methodName,
          object arg1,
          object arg2,
          object arg3,
          object arg4,
          object arg5,
          CancellationToken cancellationToken = default(CancellationToken)) {
            return harrrConnection.InvokeCoreAsync(methodName, new object[5]
            {
        arg1,
        arg2,
        arg3,
        arg4,
        arg5
            }, cancellationToken);
        }

        /// <summary>
        /// Invokes a hub method on the server using the specified method name and arguments.
        /// </summary>
        /// <param name="harrrConnection">The hub connection.</param>
        /// <param name="methodName">The name of the server method to invoke.</param>
        /// <param name="arg1">The first argument.</param>
        /// <param name="arg2">The second argument.</param>
        /// <param name="arg3">The third argument.</param>
        /// <param name="arg4">The fourth argument.</param>
        /// <param name="arg5">The fifth argument.</param>
        /// <param name="arg6">The sixth argument.</param>
        /// <param name="cancellationToken">The token to monitor for cancellation requests. The default value is <see cref="P:System.Threading.CancellationToken.None" />.</param>
        /// <returns>A <see cref="T:System.Threading.Tasks.Task" /> that represents the asynchronous invoke.</returns>
        public static Task InvokeAsync(
          this HARRRConnection harrrConnection,
          string methodName,
          object arg1,
          object arg2,
          object arg3,
          object arg4,
          object arg5,
          object arg6,
          CancellationToken cancellationToken = default(CancellationToken)) {
            return harrrConnection.InvokeCoreAsync(methodName, new object[6]
            {
        arg1,
        arg2,
        arg3,
        arg4,
        arg5,
        arg6
            }, cancellationToken);
        }

        /// <summary>
        /// Invokes a hub method on the server using the specified method name and arguments.
        /// </summary>
        /// <param name="harrrConnection">The hub connection.</param>
        /// <param name="methodName">The name of the server method to invoke.</param>
        /// <param name="arg1">The first argument.</param>
        /// <param name="arg2">The second argument.</param>
        /// <param name="arg3">The third argument.</param>
        /// <param name="arg4">The fourth argument.</param>
        /// <param name="arg5">The fifth argument.</param>
        /// <param name="arg6">The sixth argument.</param>
        /// <param name="arg7">The seventh argument.</param>
        /// <param name="cancellationToken">The token to monitor for cancellation requests. The default value is <see cref="P:System.Threading.CancellationToken.None" />.</param>
        /// <returns>A <see cref="T:System.Threading.Tasks.Task" /> that represents the asynchronous invoke.</returns>
        public static Task InvokeAsync(
          this HARRRConnection harrrConnection,
          string methodName,
          object arg1,
          object arg2,
          object arg3,
          object arg4,
          object arg5,
          object arg6,
          object arg7,
          CancellationToken cancellationToken = default(CancellationToken)) {
            return harrrConnection.InvokeCoreAsync(methodName, new object[7]
            {
        arg1,
        arg2,
        arg3,
        arg4,
        arg5,
        arg6,
        arg7
            }, cancellationToken);
        }

        /// <summary>
        /// Invokes a hub method on the server using the specified method name and arguments.
        /// </summary>
        /// <param name="harrrConnection">The hub connection.</param>
        /// <param name="methodName">The name of the server method to invoke.</param>
        /// <param name="arg1">The first argument.</param>
        /// <param name="arg2">The second argument.</param>
        /// <param name="arg3">The third argument.</param>
        /// <param name="arg4">The fourth argument.</param>
        /// <param name="arg5">The fifth argument.</param>
        /// <param name="arg6">The sixth argument.</param>
        /// <param name="arg7">The seventh argument.</param>
        /// <param name="arg8">The eighth argument.</param>
        /// <param name="cancellationToken">The token to monitor for cancellation requests. The default value is <see cref="P:System.Threading.CancellationToken.None" />.</param>
        /// <returns>A <see cref="T:System.Threading.Tasks.Task" /> that represents the asynchronous invoke.</returns>
        public static Task InvokeAsync(
          this HARRRConnection harrrConnection,
          string methodName,
          object arg1,
          object arg2,
          object arg3,
          object arg4,
          object arg5,
          object arg6,
          object arg7,
          object arg8,
          CancellationToken cancellationToken = default(CancellationToken)) {
            return harrrConnection.InvokeCoreAsync(methodName, new object[8]
            {
        arg1,
        arg2,
        arg3,
        arg4,
        arg5,
        arg6,
        arg7,
        arg8
            }, cancellationToken);
        }

        /// <summary>
        /// Invokes a hub method on the server using the specified method name and arguments.
        /// </summary>
        /// <param name="harrrConnection">The hub connection.</param>
        /// <param name="methodName">The name of the server method to invoke.</param>
        /// <param name="arg1">The first argument.</param>
        /// <param name="arg2">The second argument.</param>
        /// <param name="arg3">The third argument.</param>
        /// <param name="arg4">The fourth argument.</param>
        /// <param name="arg5">The fifth argument.</param>
        /// <param name="arg6">The sixth argument.</param>
        /// <param name="arg7">The seventh argument.</param>
        /// <param name="arg8">The eighth argument.</param>
        /// <param name="arg9">The ninth argument.</param>
        /// <param name="cancellationToken">The token to monitor for cancellation requests. The default value is <see cref="P:System.Threading.CancellationToken.None" />.</param>
        /// <returns>A <see cref="T:System.Threading.Tasks.Task" /> that represents the asynchronous invoke.</returns>
        public static Task InvokeAsync(
          this HARRRConnection harrrConnection,
          string methodName,
          object arg1,
          object arg2,
          object arg3,
          object arg4,
          object arg5,
          object arg6,
          object arg7,
          object arg8,
          object arg9,
          CancellationToken cancellationToken = default(CancellationToken)) {
            return harrrConnection.InvokeCoreAsync(methodName, new object[9]
            {
        arg1,
        arg2,
        arg3,
        arg4,
        arg5,
        arg6,
        arg7,
        arg8,
        arg9
            }, cancellationToken);
        }

        /// <summary>
        /// Invokes a hub method on the server using the specified method name and arguments.
        /// </summary>
        /// <param name="harrrConnection">The hub connection.</param>
        /// <param name="methodName">The name of the server method to invoke.</param>
        /// <param name="arg1">The first argument.</param>
        /// <param name="arg2">The second argument.</param>
        /// <param name="arg3">The third argument.</param>
        /// <param name="arg4">The fourth argument.</param>
        /// <param name="arg5">The fifth argument.</param>
        /// <param name="arg6">The sixth argument.</param>
        /// <param name="arg7">The seventh argument.</param>
        /// <param name="arg8">The eighth argument.</param>
        /// <param name="arg9">The ninth argument.</param>
        /// <param name="arg10">The tenth argument.</param>
        /// <param name="cancellationToken">The token to monitor for cancellation requests. The default value is <see cref="P:System.Threading.CancellationToken.None" />.</param>
        /// <returns>A <see cref="T:System.Threading.Tasks.Task" /> that represents the asynchronous invoke.</returns>
        public static Task InvokeAsync(
          this HARRRConnection harrrConnection,
          string methodName,
          object arg1,
          object arg2,
          object arg3,
          object arg4,
          object arg5,
          object arg6,
          object arg7,
          object arg8,
          object arg9,
          object arg10,
          CancellationToken cancellationToken = default(CancellationToken)) {
            return harrrConnection.InvokeCoreAsync(methodName, new object[10]
            {
        arg1,
        arg2,
        arg3,
        arg4,
        arg5,
        arg6,
        arg7,
        arg8,
        arg9,
        arg10
            }, cancellationToken);
        }

        /// <summary>
        /// Invokes a hub method on the server using the specified method name and arguments.
        /// </summary>
        /// <param name="harrrConnection">The hub connection.</param>
        /// <param name="methodName">The name of the server method to invoke.</param>
        /// <param name="args">The arguments used to invoke the server method.</param>
        /// <param name="cancellationToken">The token to monitor for cancellation requests. The default value is <see cref="P:System.Threading.CancellationToken.None" />.</param>
        /// <returns>A <see cref="T:System.Threading.Tasks.Task" /> that represents the asynchronous invoke.</returns>
        public static Task InvokeCoreAsync(
          this HARRRConnection harrrConnection,
          string methodName,
          object[] args,
          CancellationToken cancellationToken = default(CancellationToken)) {
            if (harrrConnection == null)
                throw new ArgumentNullException(nameof(harrrConnection));
            return (Task)harrrConnection.InvokeCoreAsync(methodName, typeof(object), args, cancellationToken);
        }

        /// <summary>
        /// Invokes a hub method on the server using the specified method name.
        /// </summary>
        /// <typeparam name="TResult">The return type of the server method.</typeparam>
        /// <param name="harrrConnection">The hub connection.</param>
        /// <param name="methodName">The name of the server method to invoke.</param>
        /// <param name="cancellationToken">The token to monitor for cancellation requests. The default value is <see cref="P:System.Threading.CancellationToken.None" />.</param>
        /// <returns>
        /// A <see cref="T:System.Threading.Tasks.Task`1" /> that represents the asynchronous invoke.
        /// The <see cref="P:System.Threading.Tasks.Task`1.Result" /> property returns a <typeparamref name="TResult" /> for the hub method return value.
        /// </returns>
        public static Task<TResult> InvokeAsync<TResult>(
          this HARRRConnection harrrConnection,
          string methodName,
          CancellationToken cancellationToken = default(CancellationToken)) {
            return harrrConnection.InvokeCoreAsync<TResult>(methodName, Array.Empty<object>(), cancellationToken);
        }

        /// <summary>
        /// Invokes a hub method on the server using the specified method name and argument.
        /// </summary>
        /// <typeparam name="TResult">The return type of the server method.</typeparam>
        /// <param name="harrrConnection">The hub connection.</param>
        /// <param name="methodName">The name of the server method to invoke.</param>
        /// <param name="arg1">The first argument.</param>
        /// <param name="cancellationToken">The token to monitor for cancellation requests. The default value is <see cref="P:System.Threading.CancellationToken.None" />.</param>
        /// <returns>
        /// A <see cref="T:System.Threading.Tasks.Task`1" /> that represents the asynchronous invoke.
        /// The <see cref="P:System.Threading.Tasks.Task`1.Result" /> property returns a <typeparamref name="TResult" /> for the hub method return value.
        /// </returns>
        public static Task<TResult> InvokeAsync<TResult>(
          this HARRRConnection harrrConnection,
          string methodName,
          object arg1,
          CancellationToken cancellationToken = default(CancellationToken)) {
            return harrrConnection.InvokeCoreAsync<TResult>(methodName, new object[1]
            {
        arg1
            }, cancellationToken);
        }

        /// <summary>
        /// Invokes a hub method on the server using the specified method name and arguments.
        /// </summary>
        /// <typeparam name="TResult">The return type of the server method.</typeparam>
        /// <param name="harrrConnection">The hub connection.</param>
        /// <param name="methodName">The name of the server method to invoke.</param>
        /// <param name="arg1">The first argument.</param>
        /// <param name="arg2">The second argument.</param>
        /// <param name="cancellationToken">The token to monitor for cancellation requests. The default value is <see cref="P:System.Threading.CancellationToken.None" />.</param>
        /// <returns>
        /// A <see cref="T:System.Threading.Tasks.Task`1" /> that represents the asynchronous invoke.
        /// The <see cref="P:System.Threading.Tasks.Task`1.Result" /> property returns a <typeparamref name="TResult" /> for the hub method return value.
        /// </returns>
        public static Task<TResult> InvokeAsync<TResult>(
          this HARRRConnection harrrConnection,
          string methodName,
          object arg1,
          object arg2,
          CancellationToken cancellationToken = default(CancellationToken)) {
            return harrrConnection.InvokeCoreAsync<TResult>(methodName, new object[2]
            {
        arg1,
        arg2
            }, cancellationToken);
        }

        /// <summary>
        /// Invokes a hub method on the server using the specified method name and arguments.
        /// </summary>
        /// <typeparam name="TResult">The return type of the server method.</typeparam>
        /// <param name="harrrConnection">The hub connection.</param>
        /// <param name="methodName">The name of the server method to invoke.</param>
        /// <param name="arg1">The first argument.</param>
        /// <param name="arg2">The second argument.</param>
        /// <param name="arg3">The third argument.</param>
        /// <param name="cancellationToken">The token to monitor for cancellation requests. The default value is <see cref="P:System.Threading.CancellationToken.None" />.</param>
        /// <returns>
        /// A <see cref="T:System.Threading.Tasks.Task`1" /> that represents the asynchronous invoke.
        /// The <see cref="P:System.Threading.Tasks.Task`1.Result" /> property returns a <typeparamref name="TResult" /> for the hub method return value.
        /// </returns>
        public static Task<TResult> InvokeAsync<TResult>(
          this HARRRConnection harrrConnection,
          string methodName,
          object arg1,
          object arg2,
          object arg3,
          CancellationToken cancellationToken = default(CancellationToken)) {
            return harrrConnection.InvokeCoreAsync<TResult>(methodName, new object[3]
            {
        arg1,
        arg2,
        arg3
            }, cancellationToken);
        }

        /// <summary>
        /// Invokes a hub method on the server using the specified method name and arguments.
        /// </summary>
        /// <typeparam name="TResult">The return type of the server method.</typeparam>
        /// <param name="harrrConnection">The hub connection.</param>
        /// <param name="methodName">The name of the server method to invoke.</param>
        /// <param name="arg1">The first argument.</param>
        /// <param name="arg2">The second argument.</param>
        /// <param name="arg3">The third argument.</param>
        /// <param name="arg4">The fourth argument.</param>
        /// <param name="cancellationToken">The token to monitor for cancellation requests. The default value is <see cref="P:System.Threading.CancellationToken.None" />.</param>
        /// <returns>
        /// A <see cref="T:System.Threading.Tasks.Task`1" /> that represents the asynchronous invoke.
        /// The <see cref="P:System.Threading.Tasks.Task`1.Result" /> property returns a <typeparamref name="TResult" /> for the hub method return value.
        /// </returns>
        public static Task<TResult> InvokeAsync<TResult>(
          this HARRRConnection harrrConnection,
          string methodName,
          object arg1,
          object arg2,
          object arg3,
          object arg4,
          CancellationToken cancellationToken = default(CancellationToken)) {
            return harrrConnection.InvokeCoreAsync<TResult>(methodName, new object[4]
            {
        arg1,
        arg2,
        arg3,
        arg4
            }, cancellationToken);
        }

        /// <summary>
        /// Invokes a hub method on the server using the specified method name and arguments.
        /// </summary>
        /// <typeparam name="TResult">The return type of the server method.</typeparam>
        /// <param name="harrrConnection">The hub connection.</param>
        /// <param name="methodName">The name of the server method to invoke.</param>
        /// <param name="arg1">The first argument.</param>
        /// <param name="arg2">The second argument.</param>
        /// <param name="arg3">The third argument.</param>
        /// <param name="arg4">The fourth argument.</param>
        /// <param name="arg5">The fifth argument.</param>
        /// <param name="cancellationToken">The token to monitor for cancellation requests. The default value is <see cref="P:System.Threading.CancellationToken.None" />.</param>
        /// <returns>
        /// A <see cref="T:System.Threading.Tasks.Task`1" /> that represents the asynchronous invoke.
        /// The <see cref="P:System.Threading.Tasks.Task`1.Result" /> property returns a <typeparamref name="TResult" /> for the hub method return value.
        /// </returns>
        public static Task<TResult> InvokeAsync<TResult>(
          this HARRRConnection harrrConnection,
          string methodName,
          object arg1,
          object arg2,
          object arg3,
          object arg4,
          object arg5,
          CancellationToken cancellationToken = default(CancellationToken)) {
            return harrrConnection.InvokeCoreAsync<TResult>(methodName, new object[5]
            {
        arg1,
        arg2,
        arg3,
        arg4,
        arg5
            }, cancellationToken);
        }

        /// <summary>
        /// Invokes a hub method on the server using the specified method name and arguments.
        /// </summary>
        /// <typeparam name="TResult">The return type of the server method.</typeparam>
        /// <param name="harrrConnection">The hub connection.</param>
        /// <param name="methodName">The name of the server method to invoke.</param>
        /// <param name="arg1">The first argument.</param>
        /// <param name="arg2">The second argument.</param>
        /// <param name="arg3">The third argument.</param>
        /// <param name="arg4">The fourth argument.</param>
        /// <param name="arg5">The fifth argument.</param>
        /// <param name="arg6">The sixth argument.</param>
        /// <param name="cancellationToken">The token to monitor for cancellation requests. The default value is <see cref="P:System.Threading.CancellationToken.None" />.</param>
        /// <returns>
        /// A <see cref="T:System.Threading.Tasks.Task`1" /> that represents the asynchronous invoke.
        /// The <see cref="P:System.Threading.Tasks.Task`1.Result" /> property returns a <typeparamref name="TResult" /> for the hub method return value.
        /// </returns>
        public static Task<TResult> InvokeAsync<TResult>(
          this HARRRConnection harrrConnection,
          string methodName,
          object arg1,
          object arg2,
          object arg3,
          object arg4,
          object arg5,
          object arg6,
          CancellationToken cancellationToken = default(CancellationToken)) {
            return harrrConnection.InvokeCoreAsync<TResult>(methodName, new object[6]
            {
        arg1,
        arg2,
        arg3,
        arg4,
        arg5,
        arg6
            }, cancellationToken);
        }

        /// <summary>
        /// Invokes a hub method on the server using the specified method name and arguments.
        /// </summary>
        /// <typeparam name="TResult">The return type of the server method.</typeparam>
        /// <param name="harrrConnection">The hub connection.</param>
        /// <param name="methodName">The name of the server method to invoke.</param>
        /// <param name="arg1">The first argument.</param>
        /// <param name="arg2">The second argument.</param>
        /// <param name="arg3">The third argument.</param>
        /// <param name="arg4">The fourth argument.</param>
        /// <param name="arg5">The fifth argument.</param>
        /// <param name="arg6">The sixth argument.</param>
        /// <param name="arg7">The seventh argument.</param>
        /// <param name="cancellationToken">The token to monitor for cancellation requests. The default value is <see cref="P:System.Threading.CancellationToken.None" />.</param>
        /// <returns>
        /// A <see cref="T:System.Threading.Tasks.Task`1" /> that represents the asynchronous invoke.
        /// The <see cref="P:System.Threading.Tasks.Task`1.Result" /> property returns a <typeparamref name="TResult" /> for the hub method return value.
        /// </returns>
        public static Task<TResult> InvokeAsync<TResult>(
          this HARRRConnection harrrConnection,
          string methodName,
          object arg1,
          object arg2,
          object arg3,
          object arg4,
          object arg5,
          object arg6,
          object arg7,
          CancellationToken cancellationToken = default(CancellationToken)) {
            return harrrConnection.InvokeCoreAsync<TResult>(methodName, new object[7]
            {
        arg1,
        arg2,
        arg3,
        arg4,
        arg5,
        arg6,
        arg7
            }, cancellationToken);
        }

        /// <summary>
        /// Invokes a hub method on the server using the specified method name and arguments.
        /// </summary>
        /// <typeparam name="TResult">The return type of the server method.</typeparam>
        /// <param name="harrrConnection">The hub connection.</param>
        /// <param name="methodName">The name of the server method to invoke.</param>
        /// <param name="arg1">The first argument.</param>
        /// <param name="arg2">The second argument.</param>
        /// <param name="arg3">The third argument.</param>
        /// <param name="arg4">The fourth argument.</param>
        /// <param name="arg5">The fifth argument.</param>
        /// <param name="arg6">The sixth argument.</param>
        /// <param name="arg7">The seventh argument.</param>
        /// <param name="arg8">The eighth argument.</param>
        /// <param name="cancellationToken">The token to monitor for cancellation requests. The default value is <see cref="P:System.Threading.CancellationToken.None" />.</param>
        /// <returns>
        /// A <see cref="T:System.Threading.Tasks.Task`1" /> that represents the asynchronous invoke.
        /// The <see cref="P:System.Threading.Tasks.Task`1.Result" /> property returns a <typeparamref name="TResult" /> for the hub method return value.
        /// </returns>
        public static Task<TResult> InvokeAsync<TResult>(
          this HARRRConnection harrrConnection,
          string methodName,
          object arg1,
          object arg2,
          object arg3,
          object arg4,
          object arg5,
          object arg6,
          object arg7,
          object arg8,
          CancellationToken cancellationToken = default(CancellationToken)) {
            return harrrConnection.InvokeCoreAsync<TResult>(methodName, new object[8]
            {
        arg1,
        arg2,
        arg3,
        arg4,
        arg5,
        arg6,
        arg7,
        arg8
            }, cancellationToken);
        }

        /// <summary>
        /// Invokes a hub method on the server using the specified method name and arguments.
        /// </summary>
        /// <typeparam name="TResult">The return type of the server method.</typeparam>
        /// <param name="harrrConnection">The hub connection.</param>
        /// <param name="methodName">The name of the server method to invoke.</param>
        /// <param name="arg1">The first argument.</param>
        /// <param name="arg2">The second argument.</param>
        /// <param name="arg3">The third argument.</param>
        /// <param name="arg4">The fourth argument.</param>
        /// <param name="arg5">The fifth argument.</param>
        /// <param name="arg6">The sixth argument.</param>
        /// <param name="arg7">The seventh argument.</param>
        /// <param name="arg8">The eighth argument.</param>
        /// <param name="arg9">The ninth argument.</param>
        /// <param name="cancellationToken">The token to monitor for cancellation requests. The default value is <see cref="P:System.Threading.CancellationToken.None" />.</param>
        /// <returns>
        /// A <see cref="T:System.Threading.Tasks.Task`1" /> that represents the asynchronous invoke.
        /// The <see cref="P:System.Threading.Tasks.Task`1.Result" /> property returns a <typeparamref name="TResult" /> for the hub method return value.
        /// </returns>
        public static Task<TResult> InvokeAsync<TResult>(
          this HARRRConnection harrrConnection,
          string methodName,
          object arg1,
          object arg2,
          object arg3,
          object arg4,
          object arg5,
          object arg6,
          object arg7,
          object arg8,
          object arg9,
          CancellationToken cancellationToken = default(CancellationToken)) {
            return harrrConnection.InvokeCoreAsync<TResult>(methodName, new object[9]
            {
        arg1,
        arg2,
        arg3,
        arg4,
        arg5,
        arg6,
        arg7,
        arg8,
        arg9
            }, cancellationToken);
        }

        /// <summary>
        /// Invokes a hub method on the server using the specified method name and arguments.
        /// </summary>
        /// <typeparam name="TResult">The return type of the server method.</typeparam>
        /// <param name="harrrConnection">The hub connection.</param>
        /// <param name="methodName">The name of the server method to invoke.</param>
        /// <param name="arg1">The first argument.</param>
        /// <param name="arg2">The second argument.</param>
        /// <param name="arg3">The third argument.</param>
        /// <param name="arg4">The fourth argument.</param>
        /// <param name="arg5">The fifth argument.</param>
        /// <param name="arg6">The sixth argument.</param>
        /// <param name="arg7">The seventh argument.</param>
        /// <param name="arg8">The eighth argument.</param>
        /// <param name="arg9">The ninth argument.</param>
        /// <param name="arg10">The tenth argument.</param>
        /// <param name="cancellationToken">The token to monitor for cancellation requests. The default value is <see cref="P:System.Threading.CancellationToken.None" />.</param>
        /// <returns>
        /// A <see cref="T:System.Threading.Tasks.Task`1" /> that represents the asynchronous invoke.
        /// The <see cref="P:System.Threading.Tasks.Task`1.Result" /> property returns a <typeparamref name="TResult" /> for the hub method return value.
        /// </returns>
        public static Task<TResult> InvokeAsync<TResult>(
          this HARRRConnection harrrConnection,
          string methodName,
          object arg1,
          object arg2,
          object arg3,
          object arg4,
          object arg5,
          object arg6,
          object arg7,
          object arg8,
          object arg9,
          object arg10,
          CancellationToken cancellationToken = default(CancellationToken)) {
            return harrrConnection.InvokeCoreAsync<TResult>(methodName, new object[10]
            {
        arg1,
        arg2,
        arg3,
        arg4,
        arg5,
        arg6,
        arg7,
        arg8,
        arg9,
        arg10
            }, cancellationToken);
        }

        /// <summary>
        /// Invokes a hub method on the server using the specified method name and arguments.
        /// </summary>
        /// <typeparam name="TResult">The return type of the server method.</typeparam>
        /// <param name="harrrConnection">The hub connection.</param>
        /// <param name="methodName">The name of the server method to invoke.</param>
        /// <param name="args">The arguments used to invoke the server method.</param>
        /// <param name="cancellationToken">The token to monitor for cancellation requests. The default value is <see cref="P:System.Threading.CancellationToken.None" />.</param>
        /// <returns>
        /// A <see cref="T:System.Threading.Tasks.Task`1" /> that represents the asynchronous invoke.
        /// The <see cref="P:System.Threading.Tasks.Task`1.Result" /> property returns a <typeparamref name="TResult" /> for the hub method return value.
        /// </returns>
        public static async Task<TResult> InvokeCoreAsync<TResult>(
          this HARRRConnection harrrConnection,
          string methodName,
          object[] args,
          CancellationToken cancellationToken = default(CancellationToken)) {
            if (harrrConnection == null)
                throw new ArgumentNullException(nameof(harrrConnection));
            return (TResult)await harrrConnection.InvokeCoreAsync(methodName, typeof(TResult), args, cancellationToken);
        }

        /// <summary>
        /// Invokes a hub method on the server using the specified method name.
        /// Does not wait for a response from the receiver.
        /// </summary>
        /// <param name="harrrConnection">The hub connection.</param>
        /// <param name="methodName">The name of the server method to invoke.</param>
        /// <param name="cancellationToken">The token to monitor for cancellation requests. The default value is <see cref="P:System.Threading.CancellationToken.None" />.</param>
        /// <returns>A <see cref="T:System.Threading.Tasks.Task`1" /> that represents the asynchronous invoke.</returns>
        public static Task SendAsync(
          this HARRRConnection harrrConnection,
          string methodName,
          CancellationToken cancellationToken = default(CancellationToken)) {
            return harrrConnection.SendCoreAsync(methodName, Array.Empty<object>(), cancellationToken);
        }

        /// <summary>
        /// Invokes a hub method on the server using the specified method name and argument.
        /// Does not wait for a response from the receiver.
        /// </summary>
        /// <param name="harrrConnection">The hub connection.</param>
        /// <param name="methodName">The name of the server method to invoke.</param>
        /// <param name="arg1">The first argument.</param>
        /// <param name="cancellationToken">The token to monitor for cancellation requests. The default value is <see cref="P:System.Threading.CancellationToken.None" />.</param>
        /// <returns>A <see cref="T:System.Threading.Tasks.Task`1" /> that represents the asynchronous invoke.</returns>
        public static Task SendAsync(
          this HARRRConnection harrrConnection,
          string methodName,
          object arg1,
          CancellationToken cancellationToken = default(CancellationToken)) {
            return harrrConnection.SendCoreAsync(methodName, new object[1]
            {
        arg1
            }, cancellationToken);
        }

        /// <summary>
        /// Invokes a hub method on the server using the specified method name and arguments.
        /// Does not wait for a response from the receiver.
        /// </summary>
        /// <param name="harrrConnection">The hub connection.</param>
        /// <param name="methodName">The name of the server method to invoke.</param>
        /// <param name="arg1">The first argument.</param>
        /// <param name="arg2">The second argument.</param>
        /// <param name="cancellationToken">The token to monitor for cancellation requests. The default value is <see cref="P:System.Threading.CancellationToken.None" />.</param>
        /// <returns>A <see cref="T:System.Threading.Tasks.Task`1" /> that represents the asynchronous invoke.</returns>
        public static Task SendAsync(
          this HARRRConnection harrrConnection,
          string methodName,
          object arg1,
          object arg2,
          CancellationToken cancellationToken = default(CancellationToken)) {
            return harrrConnection.SendCoreAsync(methodName, new object[2]
            {
        arg1,
        arg2
            }, cancellationToken);
        }

        /// <summary>
        /// Invokes a hub method on the server using the specified method name and arguments.
        /// Does not wait for a response from the receiver.
        /// </summary>
        /// <param name="harrrConnection">The hub connection.</param>
        /// <param name="methodName">The name of the server method to invoke.</param>
        /// <param name="arg1">The first argument.</param>
        /// <param name="arg2">The second argument.</param>
        /// <param name="arg3">The third argument.</param>
        /// <param name="cancellationToken">The token to monitor for cancellation requests. The default value is <see cref="P:System.Threading.CancellationToken.None" />.</param>
        /// <returns>A <see cref="T:System.Threading.Tasks.Task`1" /> that represents the asynchronous invoke.</returns>
        public static Task SendAsync(
          this HARRRConnection harrrConnection,
          string methodName,
          object arg1,
          object arg2,
          object arg3,
          CancellationToken cancellationToken = default(CancellationToken)) {
            return harrrConnection.SendCoreAsync(methodName, new object[3]
            {
        arg1,
        arg2,
        arg3
            }, cancellationToken);
        }

        /// <summary>
        /// Invokes a hub method on the server using the specified method name and arguments.
        /// Does not wait for a response from the receiver.
        /// </summary>
        /// <param name="harrrConnection">The hub connection.</param>
        /// <param name="methodName">The name of the server method to invoke.</param>
        /// <param name="arg1">The first argument.</param>
        /// <param name="arg2">The second argument.</param>
        /// <param name="arg3">The third argument.</param>
        /// <param name="arg4">The fourth argument.</param>
        /// <param name="cancellationToken">The token to monitor for cancellation requests. The default value is <see cref="P:System.Threading.CancellationToken.None" />.</param>
        /// <returns>A <see cref="T:System.Threading.Tasks.Task`1" /> that represents the asynchronous invoke.</returns>
        public static Task SendAsync(
          this HARRRConnection harrrConnection,
          string methodName,
          object arg1,
          object arg2,
          object arg3,
          object arg4,
          CancellationToken cancellationToken = default(CancellationToken)) {
            return harrrConnection.SendCoreAsync(methodName, new object[4]
            {
        arg1,
        arg2,
        arg3,
        arg4
            }, cancellationToken);
        }

        /// <summary>
        /// Invokes a hub method on the server using the specified method name and arguments.
        /// Does not wait for a response from the receiver.
        /// </summary>
        /// <param name="harrrConnection">The hub connection.</param>
        /// <param name="methodName">The name of the server method to invoke.</param>
        /// <param name="arg1">The first argument.</param>
        /// <param name="arg2">The second argument.</param>
        /// <param name="arg3">The third argument.</param>
        /// <param name="arg4">The fourth argument.</param>
        /// <param name="arg5">The fifth argument.</param>
        /// <param name="cancellationToken">The token to monitor for cancellation requests. The default value is <see cref="P:System.Threading.CancellationToken.None" />.</param>
        /// <returns>A <see cref="T:System.Threading.Tasks.Task`1" /> that represents the asynchronous invoke.</returns>
        public static Task SendAsync(
          this HARRRConnection harrrConnection,
          string methodName,
          object arg1,
          object arg2,
          object arg3,
          object arg4,
          object arg5,
          CancellationToken cancellationToken = default(CancellationToken)) {
            return harrrConnection.SendCoreAsync(methodName, new object[5]
            {
        arg1,
        arg2,
        arg3,
        arg4,
        arg5
            }, cancellationToken);
        }

        /// <summary>
        /// Invokes a hub method on the server using the specified method name and arguments.
        /// Does not wait for a response from the receiver.
        /// </summary>
        /// <param name="harrrConnection">The hub connection.</param>
        /// <param name="methodName">The name of the server method to invoke.</param>
        /// <param name="arg1">The first argument.</param>
        /// <param name="arg2">The second argument.</param>
        /// <param name="arg3">The third argument.</param>
        /// <param name="arg4">The fourth argument.</param>
        /// <param name="arg5">The fifth argument.</param>
        /// <param name="arg6">The sixth argument.</param>
        /// <param name="cancellationToken">The token to monitor for cancellation requests. The default value is <see cref="P:System.Threading.CancellationToken.None" />.</param>
        /// <returns>A <see cref="T:System.Threading.Tasks.Task`1" /> that represents the asynchronous invoke.</returns>
        public static Task SendAsync(
          this HARRRConnection harrrConnection,
          string methodName,
          object arg1,
          object arg2,
          object arg3,
          object arg4,
          object arg5,
          object arg6,
          CancellationToken cancellationToken = default(CancellationToken)) {
            return harrrConnection.SendCoreAsync(methodName, new object[6]
            {
        arg1,
        arg2,
        arg3,
        arg4,
        arg5,
        arg6
            }, cancellationToken);
        }

        /// <summary>
        /// Invokes a hub method on the server using the specified method name and arguments.
        /// Does not wait for a response from the receiver.
        /// </summary>
        /// <param name="harrrConnection">The hub connection.</param>
        /// <param name="methodName">The name of the server method to invoke.</param>
        /// <param name="arg1">The first argument.</param>
        /// <param name="arg2">The second argument.</param>
        /// <param name="arg3">The third argument.</param>
        /// <param name="arg4">The fourth argument.</param>
        /// <param name="arg5">The fifth argument.</param>
        /// <param name="arg6">The sixth argument.</param>
        /// <param name="arg7">The seventh argument.</param>
        /// <param name="cancellationToken">The token to monitor for cancellation requests. The default value is <see cref="P:System.Threading.CancellationToken.None" />.</param>
        /// <returns>A <see cref="T:System.Threading.Tasks.Task`1" /> that represents the asynchronous invoke.</returns>
        public static Task SendAsync(
          this HARRRConnection harrrConnection,
          string methodName,
          object arg1,
          object arg2,
          object arg3,
          object arg4,
          object arg5,
          object arg6,
          object arg7,
          CancellationToken cancellationToken = default(CancellationToken)) {
            return harrrConnection.SendCoreAsync(methodName, new object[7]
            {
        arg1,
        arg2,
        arg3,
        arg4,
        arg5,
        arg6,
        arg7
            }, cancellationToken);
        }

        /// <summary>
        /// Invokes a hub method on the server using the specified method name and arguments.
        /// Does not wait for a response from the receiver.
        /// </summary>
        /// <param name="harrrConnection">The hub connection.</param>
        /// <param name="methodName">The name of the server method to invoke.</param>
        /// <param name="arg1">The first argument.</param>
        /// <param name="arg2">The second argument.</param>
        /// <param name="arg3">The third argument.</param>
        /// <param name="arg4">The fourth argument.</param>
        /// <param name="arg5">The fifth argument.</param>
        /// <param name="arg6">The sixth argument.</param>
        /// <param name="arg7">The seventh argument.</param>
        /// <param name="arg8">The eighth argument.</param>
        /// <param name="cancellationToken">The token to monitor for cancellation requests. The default value is <see cref="P:System.Threading.CancellationToken.None" />.</param>
        /// <returns>A <see cref="T:System.Threading.Tasks.Task`1" /> that represents the asynchronous invoke.</returns>
        public static Task SendAsync(
          this HARRRConnection harrrConnection,
          string methodName,
          object arg1,
          object arg2,
          object arg3,
          object arg4,
          object arg5,
          object arg6,
          object arg7,
          object arg8,
          CancellationToken cancellationToken = default(CancellationToken)) {
            return harrrConnection.SendCoreAsync(methodName, new object[8]
            {
        arg1,
        arg2,
        arg3,
        arg4,
        arg5,
        arg6,
        arg7,
        arg8
            }, cancellationToken);
        }

        /// <summary>
        /// Invokes a hub method on the server using the specified method name and arguments.
        /// Does not wait for a response from the receiver.
        /// </summary>
        /// <param name="harrrConnection">The hub connection.</param>
        /// <param name="methodName">The name of the server method to invoke.</param>
        /// <param name="arg1">The first argument.</param>
        /// <param name="arg2">The second argument.</param>
        /// <param name="arg3">The third argument.</param>
        /// <param name="arg4">The fourth argument.</param>
        /// <param name="arg5">The fifth argument.</param>
        /// <param name="arg6">The sixth argument.</param>
        /// <param name="arg7">The seventh argument.</param>
        /// <param name="arg8">The eighth argument.</param>
        /// <param name="arg9">The ninth argument.</param>
        /// <param name="cancellationToken">The token to monitor for cancellation requests. The default value is <see cref="P:System.Threading.CancellationToken.None" />.</param>
        /// <returns>A <see cref="T:System.Threading.Tasks.Task`1" /> that represents the asynchronous invoke.</returns>
        public static Task SendAsync(
          this HARRRConnection harrrConnection,
          string methodName,
          object arg1,
          object arg2,
          object arg3,
          object arg4,
          object arg5,
          object arg6,
          object arg7,
          object arg8,
          object arg9,
          CancellationToken cancellationToken = default(CancellationToken)) {
            return harrrConnection.SendCoreAsync(methodName, new object[9]
            {
        arg1,
        arg2,
        arg3,
        arg4,
        arg5,
        arg6,
        arg7,
        arg8,
        arg9
            }, cancellationToken);
        }

        /// <summary>
        /// Invokes a hub method on the server using the specified method name and arguments.
        /// Does not wait for a response from the receiver.
        /// </summary>
        /// <param name="harrrConnection">The hub connection.</param>
        /// <param name="methodName">The name of the server method to invoke.</param>
        /// <param name="arg1">The first argument.</param>
        /// <param name="arg2">The second argument.</param>
        /// <param name="arg3">The third argument.</param>
        /// <param name="arg4">The fourth argument.</param>
        /// <param name="arg5">The fifth argument.</param>
        /// <param name="arg6">The sixth argument.</param>
        /// <param name="arg7">The seventh argument.</param>
        /// <param name="arg8">The eighth argument.</param>
        /// <param name="arg9">The ninth argument.</param>
        /// <param name="arg10">The tenth argument.</param>
        /// <param name="cancellationToken">The token to monitor for cancellation requests. The default value is <see cref="P:System.Threading.CancellationToken.None" />.</param>
        /// <returns>A <see cref="T:System.Threading.Tasks.Task`1" /> that represents the asynchronous invoke.</returns>
        public static Task SendAsync(
          this HARRRConnection harrrConnection,
          string methodName,
          object arg1,
          object arg2,
          object arg3,
          object arg4,
          object arg5,
          object arg6,
          object arg7,
          object arg8,
          object arg9,
          object arg10,
          CancellationToken cancellationToken = default(CancellationToken)) {
            return harrrConnection.SendCoreAsync(methodName, new object[10]
            {
        arg1,
        arg2,
        arg3,
        arg4,
        arg5,
        arg6,
        arg7,
        arg8,
        arg9,
        arg10
            }, cancellationToken);
        }

        /// <summary>
        /// Invokes a streaming hub method on the server using the specified method name and return type.
        /// </summary>
        /// <typeparam name="TResult">The return type of the streaming server method.</typeparam>
        /// <param name="harrrConnection">The hub connection.</param>
        /// <param name="methodName">The name of the server method to invoke.</param>
        /// <param name="cancellationToken">The token to monitor for cancellation requests. The default value is <see cref="P:System.Threading.CancellationToken.None" />.</param>
        /// <returns>
        /// A <see cref="T:System.Threading.Tasks.Task`1" /> that represents the asynchronous invoke.
        /// The <see cref="P:System.Threading.Tasks.Task`1.Result" /> property returns a <see cref="T:System.Threading.Channels.ChannelReader`1" /> for the streamed hub method values.
        /// </returns>
        public static Task<ChannelReader<TResult>> StreamAsChannelAsync<TResult>(
          this HARRRConnection harrrConnection,
          string methodName,
          CancellationToken cancellationToken = default(CancellationToken)) {
            return harrrConnection.StreamAsChannelCoreAsync<TResult>(methodName, Array.Empty<object>(), cancellationToken);
        }

        /// <summary>
        /// Invokes a streaming hub method on the server using the specified method name, return type and argument.
        /// </summary>
        /// <typeparam name="TResult">The return type of the streaming server method.</typeparam>
        /// <param name="harrrConnection">The hub connection.</param>
        /// <param name="methodName">The name of the server method to invoke.</param>
        /// <param name="arg1">The first argument.</param>
        /// <param name="cancellationToken">The token to monitor for cancellation requests. The default value is <see cref="P:System.Threading.CancellationToken.None" />.</param>
        /// <returns>
        /// A <see cref="T:System.Threading.Tasks.Task`1" /> that represents the asynchronous invoke.
        /// The <see cref="P:System.Threading.Tasks.Task`1.Result" /> property returns a <see cref="T:System.Threading.Channels.ChannelReader`1" /> for the streamed hub method values.
        /// </returns>
        public static Task<ChannelReader<TResult>> StreamAsChannelAsync<TResult>(
          this HARRRConnection harrrConnection,
          string methodName,
          object arg1,
          CancellationToken cancellationToken = default(CancellationToken)) {
            return harrrConnection.StreamAsChannelCoreAsync<TResult>(methodName, new object[1]
            {
        arg1
            }, cancellationToken);
        }

        /// <summary>
        /// Invokes a streaming hub method on the server using the specified method name, return type and arguments.
        /// </summary>
        /// <typeparam name="TResult">The return type of the streaming server method.</typeparam>
        /// <param name="harrrConnection">The hub connection.</param>
        /// <param name="methodName">The name of the server method to invoke.</param>
        /// <param name="arg1">The first argument.</param>
        /// <param name="arg2">The second argument.</param>
        /// <param name="cancellationToken">The token to monitor for cancellation requests. The default value is <see cref="P:System.Threading.CancellationToken.None" />.</param>
        /// <returns>
        /// A <see cref="T:System.Threading.Tasks.Task`1" /> that represents the asynchronous invoke.
        /// The <see cref="P:System.Threading.Tasks.Task`1.Result" /> property returns a <see cref="T:System.Threading.Channels.ChannelReader`1" /> for the streamed hub method values.
        /// </returns>
        public static Task<ChannelReader<TResult>> StreamAsChannelAsync<TResult>(
          this HARRRConnection harrrConnection,
          string methodName,
          object arg1,
          object arg2,
          CancellationToken cancellationToken = default(CancellationToken)) {
            return harrrConnection.StreamAsChannelCoreAsync<TResult>(methodName, new object[2]
            {
        arg1,
        arg2
            }, cancellationToken);
        }

        /// <summary>
        /// Invokes a streaming hub method on the server using the specified method name, return type and arguments.
        /// </summary>
        /// <typeparam name="TResult">The return type of the streaming server method.</typeparam>
        /// <param name="harrrConnection">The hub connection.</param>
        /// <param name="methodName">The name of the server method to invoke.</param>
        /// <param name="arg1">The first argument.</param>
        /// <param name="arg2">The second argument.</param>
        /// <param name="arg3">The third argument.</param>
        /// <param name="cancellationToken">The token to monitor for cancellation requests. The default value is <see cref="P:System.Threading.CancellationToken.None" />.</param>
        /// <returns>
        /// A <see cref="T:System.Threading.Tasks.Task`1" /> that represents the asynchronous invoke.
        /// The <see cref="P:System.Threading.Tasks.Task`1.Result" /> property returns a <see cref="T:System.Threading.Channels.ChannelReader`1" /> for the streamed hub method values.
        /// </returns>
        public static Task<ChannelReader<TResult>> StreamAsChannelAsync<TResult>(
          this HARRRConnection harrrConnection,
          string methodName,
          object arg1,
          object arg2,
          object arg3,
          CancellationToken cancellationToken = default(CancellationToken)) {
            return harrrConnection.StreamAsChannelCoreAsync<TResult>(methodName, new object[3]
            {
        arg1,
        arg2,
        arg3
            }, cancellationToken);
        }

        /// <summary>
        /// Invokes a streaming hub method on the server using the specified method name, return type and arguments.
        /// </summary>
        /// <typeparam name="TResult">The return type of the streaming server method.</typeparam>
        /// <param name="harrrConnection">The hub connection.</param>
        /// <param name="methodName">The name of the server method to invoke.</param>
        /// <param name="arg1">The first argument.</param>
        /// <param name="arg2">The second argument.</param>
        /// <param name="arg3">The third argument.</param>
        /// <param name="arg4">The fourth argument.</param>
        /// <param name="cancellationToken">The token to monitor for cancellation requests. The default value is <see cref="P:System.Threading.CancellationToken.None" />.</param>
        /// <returns>
        /// A <see cref="T:System.Threading.Tasks.Task`1" /> that represents the asynchronous invoke.
        /// The <see cref="P:System.Threading.Tasks.Task`1.Result" /> property returns a <see cref="T:System.Threading.Channels.ChannelReader`1" /> for the streamed hub method values.
        /// </returns>
        public static Task<ChannelReader<TResult>> StreamAsChannelAsync<TResult>(
          this HARRRConnection harrrConnection,
          string methodName,
          object arg1,
          object arg2,
          object arg3,
          object arg4,
          CancellationToken cancellationToken = default(CancellationToken)) {
            return harrrConnection.StreamAsChannelCoreAsync<TResult>(methodName, new object[4]
            {
        arg1,
        arg2,
        arg3,
        arg4
            }, cancellationToken);
        }

        /// <summary>
        /// Invokes a streaming hub method on the server using the specified method name, return type and arguments.
        /// </summary>
        /// <typeparam name="TResult">The return type of the streaming server method.</typeparam>
        /// <param name="harrrConnection">The hub connection.</param>
        /// <param name="methodName">The name of the server method to invoke.</param>
        /// <param name="arg1">The first argument.</param>
        /// <param name="arg2">The second argument.</param>
        /// <param name="arg3">The third argument.</param>
        /// <param name="arg4">The fourth argument.</param>
        /// <param name="arg5">The fifth argument.</param>
        /// <param name="cancellationToken">The token to monitor for cancellation requests. The default value is <see cref="P:System.Threading.CancellationToken.None" />.</param>
        /// <returns>
        /// A <see cref="T:System.Threading.Tasks.Task`1" /> that represents the asynchronous invoke.
        /// The <see cref="P:System.Threading.Tasks.Task`1.Result" /> property returns a <see cref="T:System.Threading.Channels.ChannelReader`1" /> for the streamed hub method values.
        /// </returns>
        public static Task<ChannelReader<TResult>> StreamAsChannelAsync<TResult>(
          this HARRRConnection harrrConnection,
          string methodName,
          object arg1,
          object arg2,
          object arg3,
          object arg4,
          object arg5,
          CancellationToken cancellationToken = default(CancellationToken)) {
            return harrrConnection.StreamAsChannelCoreAsync<TResult>(methodName, new object[5]
            {
        arg1,
        arg2,
        arg3,
        arg4,
        arg5
            }, cancellationToken);
        }

        /// <summary>
        /// Invokes a streaming hub method on the server using the specified method name, return type and arguments.
        /// </summary>
        /// <typeparam name="TResult">The return type of the streaming server method.</typeparam>
        /// <param name="harrrConnection">The hub connection.</param>
        /// <param name="methodName">The name of the server method to invoke.</param>
        /// <param name="arg1">The first argument.</param>
        /// <param name="arg2">The second argument.</param>
        /// <param name="arg3">The third argument.</param>
        /// <param name="arg4">The fourth argument.</param>
        /// <param name="arg5">The fifth argument.</param>
        /// <param name="arg6">The sixth argument.</param>
        /// <param name="cancellationToken">The token to monitor for cancellation requests. The default value is <see cref="P:System.Threading.CancellationToken.None" />.</param>
        /// <returns>
        /// A <see cref="T:System.Threading.Tasks.Task`1" /> that represents the asynchronous invoke.
        /// The <see cref="P:System.Threading.Tasks.Task`1.Result" /> property returns a <see cref="T:System.Threading.Channels.ChannelReader`1" /> for the streamed hub method values.
        /// </returns>
        public static Task<ChannelReader<TResult>> StreamAsChannelAsync<TResult>(
          this HARRRConnection harrrConnection,
          string methodName,
          object arg1,
          object arg2,
          object arg3,
          object arg4,
          object arg5,
          object arg6,
          CancellationToken cancellationToken = default(CancellationToken)) {
            return harrrConnection.StreamAsChannelCoreAsync<TResult>(methodName, new object[6]
            {
        arg1,
        arg2,
        arg3,
        arg4,
        arg5,
        arg6
            }, cancellationToken);
        }

        /// <summary>
        /// Invokes a streaming hub method on the server using the specified method name, return type and arguments.
        /// </summary>
        /// <typeparam name="TResult">The return type of the streaming server method.</typeparam>
        /// <param name="harrrConnection">The hub connection.</param>
        /// <param name="methodName">The name of the server method to invoke.</param>
        /// <param name="arg1">The first argument.</param>
        /// <param name="arg2">The second argument.</param>
        /// <param name="arg3">The third argument.</param>
        /// <param name="arg4">The fourth argument.</param>
        /// <param name="arg5">The fifth argument.</param>
        /// <param name="arg6">The sixth argument.</param>
        /// <param name="arg7">The seventh argument.</param>
        /// <param name="cancellationToken">The token to monitor for cancellation requests. The default value is <see cref="P:System.Threading.CancellationToken.None" />.</param>
        /// <returns>
        /// A <see cref="T:System.Threading.Tasks.Task`1" /> that represents the asynchronous invoke.
        /// The <see cref="P:System.Threading.Tasks.Task`1.Result" /> property returns a <see cref="T:System.Threading.Channels.ChannelReader`1" /> for the streamed hub method values.
        /// </returns>
        public static Task<ChannelReader<TResult>> StreamAsChannelAsync<TResult>(
          this HARRRConnection harrrConnection,
          string methodName,
          object arg1,
          object arg2,
          object arg3,
          object arg4,
          object arg5,
          object arg6,
          object arg7,
          CancellationToken cancellationToken = default(CancellationToken)) {
            return harrrConnection.StreamAsChannelCoreAsync<TResult>(methodName, new object[7]
            {
        arg1,
        arg2,
        arg3,
        arg4,
        arg5,
        arg6,
        arg7
            }, cancellationToken);
        }

        /// <summary>
        /// Invokes a streaming hub method on the server using the specified method name, return type and arguments.
        /// </summary>
        /// <typeparam name="TResult">The return type of the streaming server method.</typeparam>
        /// <param name="harrrConnection">The hub connection.</param>
        /// <param name="methodName">The name of the server method to invoke.</param>
        /// <param name="arg1">The first argument.</param>
        /// <param name="arg2">The second argument.</param>
        /// <param name="arg3">The third argument.</param>
        /// <param name="arg4">The fourth argument.</param>
        /// <param name="arg5">The fifth argument.</param>
        /// <param name="arg6">The sixth argument.</param>
        /// <param name="arg7">The seventh argument.</param>
        /// <param name="arg8">The eighth argument.</param>
        /// <param name="cancellationToken">The token to monitor for cancellation requests. The default value is <see cref="P:System.Threading.CancellationToken.None" />.</param>
        /// <returns>
        /// A <see cref="T:System.Threading.Tasks.Task`1" /> that represents the asynchronous invoke.
        /// The <see cref="P:System.Threading.Tasks.Task`1.Result" /> property returns a <see cref="T:System.Threading.Channels.ChannelReader`1" /> for the streamed hub method values.
        /// </returns>
        public static Task<ChannelReader<TResult>> StreamAsChannelAsync<TResult>(
          this HARRRConnection harrrConnection,
          string methodName,
          object arg1,
          object arg2,
          object arg3,
          object arg4,
          object arg5,
          object arg6,
          object arg7,
          object arg8,
          CancellationToken cancellationToken = default(CancellationToken)) {
            return harrrConnection.StreamAsChannelCoreAsync<TResult>(methodName, new object[8]
            {
        arg1,
        arg2,
        arg3,
        arg4,
        arg5,
        arg6,
        arg7,
        arg8
            }, cancellationToken);
        }

        /// <summary>
        /// Invokes a streaming hub method on the server using the specified method name, return type and arguments.
        /// </summary>
        /// <typeparam name="TResult">The return type of the streaming server method.</typeparam>
        /// <param name="harrrConnection">The hub connection.</param>
        /// <param name="methodName">The name of the server method to invoke.</param>
        /// <param name="arg1">The first argument.</param>
        /// <param name="arg2">The second argument.</param>
        /// <param name="arg3">The third argument.</param>
        /// <param name="arg4">The fourth argument.</param>
        /// <param name="arg5">The fifth argument.</param>
        /// <param name="arg6">The sixth argument.</param>
        /// <param name="arg7">The seventh argument.</param>
        /// <param name="arg8">The eighth argument.</param>
        /// <param name="arg9">The ninth argument.</param>
        /// <param name="cancellationToken">The token to monitor for cancellation requests. The default value is <see cref="P:System.Threading.CancellationToken.None" />.</param>
        /// <returns>
        /// A <see cref="T:System.Threading.Tasks.Task`1" /> that represents the asynchronous invoke.
        /// The <see cref="P:System.Threading.Tasks.Task`1.Result" /> property returns a <see cref="T:System.Threading.Channels.ChannelReader`1" /> for the streamed hub method values.
        /// </returns>
        public static Task<ChannelReader<TResult>> StreamAsChannelAsync<TResult>(
          this HARRRConnection harrrConnection,
          string methodName,
          object arg1,
          object arg2,
          object arg3,
          object arg4,
          object arg5,
          object arg6,
          object arg7,
          object arg8,
          object arg9,
          CancellationToken cancellationToken = default(CancellationToken)) {
            return harrrConnection.StreamAsChannelCoreAsync<TResult>(methodName, new object[9]
            {
        arg1,
        arg2,
        arg3,
        arg4,
        arg5,
        arg6,
        arg7,
        arg8,
        arg9
            }, cancellationToken);
        }

        /// <summary>
        /// Invokes a streaming hub method on the server using the specified method name, return type and arguments.
        /// </summary>
        /// <typeparam name="TResult">The return type of the streaming server method.</typeparam>
        /// <param name="harrrConnection">The hub connection.</param>
        /// <param name="methodName">The name of the server method to invoke.</param>
        /// <param name="arg1">The first argument.</param>
        /// <param name="arg2">The second argument.</param>
        /// <param name="arg3">The third argument.</param>
        /// <param name="arg4">The fourth argument.</param>
        /// <param name="arg5">The fifth argument.</param>
        /// <param name="arg6">The sixth argument.</param>
        /// <param name="arg7">The seventh argument.</param>
        /// <param name="arg8">The eighth argument.</param>
        /// <param name="arg9">The ninth argument.</param>
        /// <param name="arg10">The tenth argument.</param>
        /// <param name="cancellationToken">The token to monitor for cancellation requests. The default value is <see cref="P:System.Threading.CancellationToken.None" />.</param>
        /// <returns>
        /// A <see cref="T:System.Threading.Tasks.Task`1" /> that represents the asynchronous invoke.
        /// The <see cref="P:System.Threading.Tasks.Task`1.Result" /> property returns a <see cref="T:System.Threading.Channels.ChannelReader`1" /> for the streamed hub method values.
        /// </returns>
        public static Task<ChannelReader<TResult>> StreamAsChannelAsync<TResult>(
          this HARRRConnection harrrConnection,
          string methodName,
          object arg1,
          object arg2,
          object arg3,
          object arg4,
          object arg5,
          object arg6,
          object arg7,
          object arg8,
          object arg9,
          object arg10,
          CancellationToken cancellationToken = default(CancellationToken)) {
            return harrrConnection.StreamAsChannelCoreAsync<TResult>(methodName, new object[10]
            {
        arg1,
        arg2,
        arg3,
        arg4,
        arg5,
        arg6,
        arg7,
        arg8,
        arg9,
        arg10
            }, cancellationToken);
        }

        /// <summary>
        /// Invokes a streaming hub method on the server using the specified method name, return type and arguments.
        /// </summary>
        /// <typeparam name="TResult">The return type of the streaming server method.</typeparam>
        /// <param name="harrrConnection">The hub connection.</param>
        /// <param name="methodName">The name of the server method to invoke.</param>
        /// <param name="args">The arguments used to invoke the server method.</param>
        /// <param name="cancellationToken">The token to monitor for cancellation requests. The default value is <see cref="P:System.Threading.CancellationToken.None" />.</param>
        /// <returns>
        /// A <see cref="T:System.Threading.Tasks.Task`1" /> that represents the asynchronous invoke.
        /// The <see cref="P:System.Threading.Tasks.Task`1.Result" /> property returns a <see cref="T:System.Threading.Channels.ChannelReader`1" /> for the streamed hub method values.
        /// </returns>
        public static async Task<ChannelReader<TResult>> StreamAsChannelCoreAsync<TResult>(
          this HARRRConnection harrrConnection,
          string methodName,
          object[] args,
          CancellationToken cancellationToken = default(CancellationToken)) {
            if (harrrConnection == null)
                throw new ArgumentNullException(nameof(harrrConnection));
            ChannelReader<object> inputChannel = await harrrConnection.StreamAsChannelCoreAsync(methodName, typeof(TResult), args, cancellationToken);
            Channel<TResult> outputChannel = Channel.CreateUnbounded<TResult>();
#pragma warning disable 4014
            RunChannel();
#pragma warning restore 4014
            return outputChannel.Reader;

            async Task RunChannel() {
                try {
                label_6:
                    if (await inputChannel.WaitToReadAsync(new CancellationToken())) {
                    label_2:
                        object item;
                        if (inputChannel.TryRead(out item)) {
                            while (!outputChannel.Writer.TryWrite((TResult)item)) {
                                if (!await outputChannel.Writer.WaitToWriteAsync(new CancellationToken()))
                                    return;
                            }
                            goto label_2;
                        } else
                            goto label_6;
                    } else
                        await inputChannel.Completion;
                } catch (Exception ex) {
                    outputChannel.Writer.TryComplete(ex);
                } finally {
                    outputChannel.Writer.TryComplete((Exception)null);
                }
            }
        }

        /// <summary>
        /// Invokes a streaming hub method on the server using the specified method name and return type.
        /// </summary>
        /// <typeparam name="TResult">The return type of the streaming server method.</typeparam>
        /// <param name="harrrConnection">The hub connection.</param>
        /// <param name="methodName">The name of the server method to invoke.</param>
        /// <param name="cancellationToken">The token to monitor for cancellation requests. The default value is <see cref="P:System.Threading.CancellationToken.None" />.</param>
        /// <returns>
        /// A <see cref="T:System.Collections.Generic.IAsyncEnumerable`1" /> that represents the stream.
        /// </returns>
        public static IAsyncEnumerable<TResult> StreamAsync<TResult>(
          this HARRRConnection harrrConnection,
          string methodName,
          CancellationToken cancellationToken = default(CancellationToken)) {
            return harrrConnection.StreamAsyncCore<TResult>(methodName, Array.Empty<object>(), cancellationToken);
        }

        /// <summary>
        /// Invokes a streaming hub method on the server using the specified method name, return type and argument.
        /// </summary>
        /// <typeparam name="TResult">The return type of the streaming server method.</typeparam>
        /// <param name="harrrConnection">The hub connection.</param>
        /// <param name="methodName">The name of the server method to invoke.</param>
        /// <param name="arg1">The first argument.</param>
        /// <param name="cancellationToken">The token to monitor for cancellation requests. The default value is <see cref="P:System.Threading.CancellationToken.None" />.</param>
        /// <returns>
        /// A <see cref="T:System.Collections.Generic.IAsyncEnumerable`1" /> that represents the stream.
        /// </returns>
        public static IAsyncEnumerable<TResult> StreamAsync<TResult>(
          this HARRRConnection harrrConnection,
          string methodName,
          object arg1,
          CancellationToken cancellationToken = default(CancellationToken)) {
            return harrrConnection.StreamAsyncCore<TResult>(methodName, new object[1]
            {
        arg1
            }, cancellationToken);
        }

        /// <summary>
        /// Invokes a streaming hub method on the server using the specified method name, return type and argument.
        /// </summary>
        /// <typeparam name="TResult">The return type of the streaming server method.</typeparam>
        /// <param name="harrrConnection">The hub connection.</param>
        /// <param name="methodName">The name of the server method to invoke.</param>
        /// <param name="arg1">The first argument.</param>
        /// <param name="arg2">The second argument.</param>
        /// <param name="cancellationToken">The token to monitor for cancellation requests. The default value is <see cref="P:System.Threading.CancellationToken.None" />.</param>
        /// <returns>
        /// A <see cref="T:System.Collections.Generic.IAsyncEnumerable`1" /> that represents the stream.
        /// </returns>
        public static IAsyncEnumerable<TResult> StreamAsync<TResult>(
          this HARRRConnection harrrConnection,
          string methodName,
          object arg1,
          object arg2,
          CancellationToken cancellationToken = default(CancellationToken)) {
            return harrrConnection.StreamAsyncCore<TResult>(methodName, new object[2]
            {
        arg1,
        arg2
            }, cancellationToken);
        }

        /// <summary>
        /// Invokes a streaming hub method on the server using the specified method name, return type and argument.
        /// </summary>
        /// <typeparam name="TResult">The return type of the streaming server method.</typeparam>
        /// <param name="harrrConnection">The hub connection.</param>
        /// <param name="methodName">The name of the server method to invoke.</param>
        /// <param name="arg1">The first argument.</param>
        /// <param name="arg2">The second argument.</param>
        /// <param name="arg3">The third argument.</param>
        /// <param name="cancellationToken">The token to monitor for cancellation requests. The default value is <see cref="P:System.Threading.CancellationToken.None" />.</param>
        /// <returns>
        /// A <see cref="T:System.Collections.Generic.IAsyncEnumerable`1" /> that represents the stream.
        /// </returns>
        public static IAsyncEnumerable<TResult> StreamAsync<TResult>(
          this HARRRConnection harrrConnection,
          string methodName,
          object arg1,
          object arg2,
          object arg3,
          CancellationToken cancellationToken = default(CancellationToken)) {
            return harrrConnection.StreamAsyncCore<TResult>(methodName, new object[3]
            {
        arg1,
        arg2,
        arg3
            }, cancellationToken);
        }

        /// <summary>
        /// Invokes a streaming hub method on the server using the specified method name, return type and argument.
        /// </summary>
        /// <typeparam name="TResult">The return type of the streaming server method.</typeparam>
        /// <param name="harrrConnection">The hub connection.</param>
        /// <param name="methodName">The name of the server method to invoke.</param>
        /// <param name="arg1">The first argument.</param>
        /// <param name="arg2">The second argument.</param>
        /// <param name="arg3">The third argument.</param>
        /// <param name="arg4">The fourth argument.</param>
        /// <param name="cancellationToken">The token to monitor for cancellation requests. The default value is <see cref="P:System.Threading.CancellationToken.None" />.</param>
        /// <returns>
        /// A <see cref="T:System.Collections.Generic.IAsyncEnumerable`1" /> that represents the stream.
        /// </returns>
        public static IAsyncEnumerable<TResult> StreamAsync<TResult>(
          this HARRRConnection harrrConnection,
          string methodName,
          object arg1,
          object arg2,
          object arg3,
          object arg4,
          CancellationToken cancellationToken = default(CancellationToken)) {
            return harrrConnection.StreamAsyncCore<TResult>(methodName, new object[4]
            {
        arg1,
        arg2,
        arg3,
        arg4
            }, cancellationToken);
        }

        /// <summary>
        /// Invokes a streaming hub method on the server using the specified method name, return type and argument.
        /// </summary>
        /// <typeparam name="TResult">The return type of the streaming server method.</typeparam>
        /// <param name="harrrConnection">The hub connection.</param>
        /// <param name="methodName">The name of the server method to invoke.</param>
        /// <param name="arg1">The first argument.</param>
        /// <param name="arg2">The second argument.</param>
        /// <param name="arg3">The third argument.</param>
        /// <param name="arg4">The fourth argument.</param>
        /// <param name="arg5">The fifth argument.</param>
        /// <param name="cancellationToken">The token to monitor for cancellation requests. The default value is <see cref="P:System.Threading.CancellationToken.None" />.</param>
        /// <returns>
        /// A <see cref="T:System.Collections.Generic.IAsyncEnumerable`1" /> that represents the stream.
        /// </returns>
        public static IAsyncEnumerable<TResult> StreamAsync<TResult>(
          this HARRRConnection harrrConnection,
          string methodName,
          object arg1,
          object arg2,
          object arg3,
          object arg4,
          object arg5,
          CancellationToken cancellationToken = default(CancellationToken)) {
            return harrrConnection.StreamAsyncCore<TResult>(methodName, new object[5]
            {
        arg1,
        arg2,
        arg3,
        arg4,
        arg5
            }, cancellationToken);
        }

        /// <summary>
        /// Invokes a streaming hub method on the server using the specified method name, return type and argument.
        /// </summary>
        /// <typeparam name="TResult">The return type of the streaming server method.</typeparam>
        /// <param name="harrrConnection">The hub connection.</param>
        /// <param name="methodName">The name of the server method to invoke.</param>
        /// <param name="arg1">The first argument.</param>
        /// <param name="arg2">The second argument.</param>
        /// <param name="arg3">The third argument.</param>
        /// <param name="arg4">The fourth argument.</param>
        /// <param name="arg5">The fifth argument.</param>
        /// <param name="arg6">The sixth argument.</param>
        /// <param name="cancellationToken">The token to monitor for cancellation requests. The default value is <see cref="P:System.Threading.CancellationToken.None" />.</param>
        /// <returns>
        /// A <see cref="T:System.Collections.Generic.IAsyncEnumerable`1" /> that represents the stream.
        /// </returns>
        public static IAsyncEnumerable<TResult> StreamAsync<TResult>(
          this HARRRConnection harrrConnection,
          string methodName,
          object arg1,
          object arg2,
          object arg3,
          object arg4,
          object arg5,
          object arg6,
          CancellationToken cancellationToken = default(CancellationToken)) {
            return harrrConnection.StreamAsyncCore<TResult>(methodName, new object[6]
            {
        arg1,
        arg2,
        arg3,
        arg4,
        arg5,
        arg6
            }, cancellationToken);
        }

        /// <summary>
        /// Invokes a streaming hub method on the server using the specified method name, return type and argument.
        /// </summary>
        /// <typeparam name="TResult">The return type of the streaming server method.</typeparam>
        /// <param name="harrrConnection">The hub connection.</param>
        /// <param name="methodName">The name of the server method to invoke.</param>
        /// <param name="arg1">The first argument.</param>
        /// <param name="arg2">The second argument.</param>
        /// <param name="arg3">The third argument.</param>
        /// <param name="arg4">The fourth argument.</param>
        /// <param name="arg5">The fifth argument.</param>
        /// <param name="arg6">The sixth argument.</param>
        /// <param name="arg7">The seventh argument.</param>
        /// <param name="cancellationToken">The token to monitor for cancellation requests. The default value is <see cref="P:System.Threading.CancellationToken.None" />.</param>
        /// <returns>
        /// A <see cref="T:System.Collections.Generic.IAsyncEnumerable`1" /> that represents the stream.
        /// </returns>
        public static IAsyncEnumerable<TResult> StreamAsync<TResult>(
          this HARRRConnection harrrConnection,
          string methodName,
          object arg1,
          object arg2,
          object arg3,
          object arg4,
          object arg5,
          object arg6,
          object arg7,
          CancellationToken cancellationToken = default(CancellationToken)) {
            return harrrConnection.StreamAsyncCore<TResult>(methodName, new object[7]
            {
        arg1,
        arg2,
        arg3,
        arg4,
        arg5,
        arg6,
        arg7
            }, cancellationToken);
        }

        /// <summary>
        /// Invokes a streaming hub method on the server using the specified method name, return type and argument.
        /// </summary>
        /// <typeparam name="TResult">The return type of the streaming server method.</typeparam>
        /// <param name="harrrConnection">The hub connection.</param>
        /// <param name="methodName">The name of the server method to invoke.</param>
        /// <param name="arg1">The first argument.</param>
        /// <param name="arg2">The second argument.</param>
        /// <param name="arg3">The third argument.</param>
        /// <param name="arg4">The fourth argument.</param>
        /// <param name="arg5">The fifth argument.</param>
        /// <param name="arg6">The sixth argument.</param>
        /// <param name="arg7">The seventh argument.</param>
        /// <param name="arg8">The eighth argument.</param>
        /// <param name="cancellationToken">The token to monitor for cancellation requests. The default value is <see cref="P:System.Threading.CancellationToken.None" />.</param>
        /// <returns>
        /// A <see cref="T:System.Collections.Generic.IAsyncEnumerable`1" /> that represents the stream.
        /// </returns>
        public static IAsyncEnumerable<TResult> StreamAsync<TResult>(
          this HARRRConnection harrrConnection,
          string methodName,
          object arg1,
          object arg2,
          object arg3,
          object arg4,
          object arg5,
          object arg6,
          object arg7,
          object arg8,
          CancellationToken cancellationToken = default(CancellationToken)) {
            return harrrConnection.StreamAsyncCore<TResult>(methodName, new object[8]
            {
        arg1,
        arg2,
        arg3,
        arg4,
        arg5,
        arg6,
        arg7,
        arg8
            }, cancellationToken);
        }

        /// <summary>
        /// Invokes a streaming hub method on the server using the specified method name, return type and argument.
        /// </summary>
        /// <typeparam name="TResult">The return type of the streaming server method.</typeparam>
        /// <param name="harrrConnection">The hub connection.</param>
        /// <param name="methodName">The name of the server method to invoke.</param>
        /// <param name="arg1">The first argument.</param>
        /// <param name="arg2">The second argument.</param>
        /// <param name="arg3">The third argument.</param>
        /// <param name="arg4">The fourth argument.</param>
        /// <param name="arg5">The fifth argument.</param>
        /// <param name="arg6">The sixth argument.</param>
        /// <param name="arg7">The seventh argument.</param>
        /// <param name="arg8">The eighth argument.</param>
        /// <param name="arg9">The ninth argument.</param>
        /// <param name="cancellationToken">The token to monitor for cancellation requests. The default value is <see cref="P:System.Threading.CancellationToken.None" />.</param>
        /// <returns>
        /// A <see cref="T:System.Collections.Generic.IAsyncEnumerable`1" /> that represents the stream.
        /// </returns>
        public static IAsyncEnumerable<TResult> StreamAsync<TResult>(
          this HARRRConnection harrrConnection,
          string methodName,
          object arg1,
          object arg2,
          object arg3,
          object arg4,
          object arg5,
          object arg6,
          object arg7,
          object arg8,
          object arg9,
          CancellationToken cancellationToken = default(CancellationToken)) {
            return harrrConnection.StreamAsyncCore<TResult>(methodName, new object[9]
            {
        arg1,
        arg2,
        arg3,
        arg4,
        arg5,
        arg6,
        arg7,
        arg8,
        arg9
            }, cancellationToken);
        }

        /// <summary>
        /// Invokes a streaming hub method on the server using the specified method name, return type and argument.
        /// </summary>
        /// <typeparam name="TResult">The return type of the streaming server method.</typeparam>
        /// <param name="harrrConnection">The hub connection.</param>
        /// <param name="methodName">The name of the server method to invoke.</param>
        /// <param name="arg1">The first argument.</param>
        /// <param name="arg2">The second argument.</param>
        /// <param name="arg3">The third argument.</param>
        /// <param name="arg4">The fourth argument.</param>
        /// <param name="arg5">The fifth argument.</param>
        /// <param name="arg6">The sixth argument.</param>
        /// <param name="arg7">The seventh argument.</param>
        /// <param name="arg8">The eighth argument.</param>
        /// <param name="arg9">The ninth argument.</param>
        /// <param name="arg10">The tenth argument.</param>
        /// <param name="cancellationToken">The token to monitor for cancellation requests. The default value is <see cref="P:System.Threading.CancellationToken.None" />.</param>
        /// <returns>
        /// A <see cref="T:System.Collections.Generic.IAsyncEnumerable`1" /> that represents the stream.
        /// </returns>
        public static IAsyncEnumerable<TResult> StreamAsync<TResult>(
          this HARRRConnection harrrConnection,
          string methodName,
          object arg1,
          object arg2,
          object arg3,
          object arg4,
          object arg5,
          object arg6,
          object arg7,
          object arg8,
          object arg9,
          object arg10,
          CancellationToken cancellationToken = default(CancellationToken)) {
            return harrrConnection.StreamAsyncCore<TResult>(methodName, new object[10]
            {
        arg1,
        arg2,
        arg3,
        arg4,
        arg5,
        arg6,
        arg7,
        arg8,
        arg9,
        arg10
            }, cancellationToken);
        }
    }
}
