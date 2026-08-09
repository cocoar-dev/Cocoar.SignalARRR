// Convenience wrappers over the *Core* methods on HARRRConnection: they pack loose arguments
// into the object[] those methods take, and unpack them again for typed handlers.
//
// The names and shapes deliberately match Microsoft.AspNetCore.SignalR.Client's
// HubConnectionExtensions, so that code written against a HubConnection reads the same here.
// The implementations are this project's own, written against HARRRConnection's public surface.
//
// The arity ladder stops at four arguments. SignalR's own goes to eight (handlers) and ten
// (calls); past four, an object[] overload or a typed contract via GetTypedMethods<T>() is the
// better tool, and every additional rung buries GetTypedMethods<T>() deeper in the completion
// list for an API that is not the one you should be reaching for.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Cocoar.SignalARRR.Client {

    public static class HARRRConnectionExtensions {

        #region Handler registration plumbing

        private static IDisposable On(
            this HARRRConnection connection,
            string methodName,
            Type[] parameterTypes,
            Action<object[]> handler) {
            return connection.On(
                methodName,
                parameterTypes,
                (parameters, state) => {
                    ((Action<object[]>)state)(parameters!);
                    return Task.CompletedTask;
                },
                handler);
        }

        private static IDisposable On(
            this HARRRConnection connection,
            string methodName,
            Type[] parameterTypes,
            Func<object[], Task> handler) {
            return connection.On(
                methodName,
                parameterTypes,
                (parameters, state) => ((Func<object[], Task>)state)(parameters!),
                handler);
        }

        #endregion

        #region On — synchronous handlers

        /// <summary>Registers a handler for a raw SignalR method name. Dispose the result to unsubscribe.</summary>
        public static IDisposable On(this HARRRConnection connection, string methodName, Action handler) {
            ArgumentNullException.ThrowIfNull(connection);
            ArgumentNullException.ThrowIfNull(handler);
            return connection.On(methodName, Type.EmptyTypes, (Action<object[]>)(_ => handler()));
        }

        /// <inheritdoc cref="On(HARRRConnection, string, Action)"/>
        public static IDisposable On<T1>(this HARRRConnection connection, string methodName, Action<T1> handler) {
            ArgumentNullException.ThrowIfNull(connection);
            ArgumentNullException.ThrowIfNull(handler);
            return connection.On(
                methodName,
                new[] { typeof(T1) },
                (Action<object[]>)(args => handler((T1)args[0])));
        }

        /// <inheritdoc cref="On(HARRRConnection, string, Action)"/>
        public static IDisposable On<T1, T2>(this HARRRConnection connection, string methodName, Action<T1, T2> handler) {
            ArgumentNullException.ThrowIfNull(connection);
            ArgumentNullException.ThrowIfNull(handler);
            return connection.On(
                methodName,
                new[] { typeof(T1), typeof(T2) },
                (Action<object[]>)(args => handler((T1)args[0], (T2)args[1])));
        }

        /// <inheritdoc cref="On(HARRRConnection, string, Action)"/>
        public static IDisposable On<T1, T2, T3>(this HARRRConnection connection, string methodName, Action<T1, T2, T3> handler) {
            ArgumentNullException.ThrowIfNull(connection);
            ArgumentNullException.ThrowIfNull(handler);
            return connection.On(
                methodName,
                new[] { typeof(T1), typeof(T2), typeof(T3) },
                (Action<object[]>)(args => handler((T1)args[0], (T2)args[1], (T3)args[2])));
        }

        /// <inheritdoc cref="On(HARRRConnection, string, Action)"/>
        public static IDisposable On<T1, T2, T3, T4>(this HARRRConnection connection, string methodName, Action<T1, T2, T3, T4> handler) {
            ArgumentNullException.ThrowIfNull(connection);
            ArgumentNullException.ThrowIfNull(handler);
            return connection.On(
                methodName,
                new[] { typeof(T1), typeof(T2), typeof(T3), typeof(T4) },
                (Action<object[]>)(args => handler((T1)args[0], (T2)args[1], (T3)args[2], (T4)args[3])));
        }

        #endregion

        #region On — asynchronous handlers

        /// <summary>Registers an asynchronous handler for a raw SignalR method name. Dispose the result to unsubscribe.</summary>
        public static IDisposable On(this HARRRConnection connection, string methodName, Func<Task> handler) {
            ArgumentNullException.ThrowIfNull(connection);
            ArgumentNullException.ThrowIfNull(handler);
            return connection.On(methodName, Type.EmptyTypes, (Func<object[], Task>)(_ => handler()));
        }

        /// <inheritdoc cref="On(HARRRConnection, string, Func{Task})"/>
        public static IDisposable On<T1>(this HARRRConnection connection, string methodName, Func<T1, Task> handler) {
            ArgumentNullException.ThrowIfNull(connection);
            ArgumentNullException.ThrowIfNull(handler);
            return connection.On(
                methodName,
                new[] { typeof(T1) },
                (Func<object[], Task>)(args => handler((T1)args[0])));
        }

        /// <inheritdoc cref="On(HARRRConnection, string, Func{Task})"/>
        public static IDisposable On<T1, T2>(this HARRRConnection connection, string methodName, Func<T1, T2, Task> handler) {
            ArgumentNullException.ThrowIfNull(connection);
            ArgumentNullException.ThrowIfNull(handler);
            return connection.On(
                methodName,
                new[] { typeof(T1), typeof(T2) },
                (Func<object[], Task>)(args => handler((T1)args[0], (T2)args[1])));
        }

        /// <inheritdoc cref="On(HARRRConnection, string, Func{Task})"/>
        public static IDisposable On<T1, T2, T3>(this HARRRConnection connection, string methodName, Func<T1, T2, T3, Task> handler) {
            ArgumentNullException.ThrowIfNull(connection);
            ArgumentNullException.ThrowIfNull(handler);
            return connection.On(
                methodName,
                new[] { typeof(T1), typeof(T2), typeof(T3) },
                (Func<object[], Task>)(args => handler((T1)args[0], (T2)args[1], (T3)args[2])));
        }

        /// <inheritdoc cref="On(HARRRConnection, string, Func{Task})"/>
        public static IDisposable On<T1, T2, T3, T4>(this HARRRConnection connection, string methodName, Func<T1, T2, T3, T4, Task> handler) {
            ArgumentNullException.ThrowIfNull(connection);
            ArgumentNullException.ThrowIfNull(handler);
            return connection.On(
                methodName,
                new[] { typeof(T1), typeof(T2), typeof(T3), typeof(T4) },
                (Func<object[], Task>)(args => handler((T1)args[0], (T2)args[1], (T3)args[2], (T4)args[3])));
        }

        #endregion

        #region InvokeAsync — no result

        /// <summary>Invokes a server method and awaits its completion.</summary>
        public static Task InvokeAsync(this HARRRConnection connection, string methodName, CancellationToken cancellationToken = default) {
            ArgumentNullException.ThrowIfNull(connection);
            return connection.InvokeCoreAsync(methodName, Array.Empty<object>(), cancellationToken);
        }

        /// <inheritdoc cref="InvokeAsync(HARRRConnection, string, CancellationToken)"/>
        public static Task InvokeAsync(this HARRRConnection connection, string methodName, object arg1, CancellationToken cancellationToken = default) {
            ArgumentNullException.ThrowIfNull(connection);
            return connection.InvokeCoreAsync(methodName, new[] { arg1 }, cancellationToken);
        }

        /// <inheritdoc cref="InvokeAsync(HARRRConnection, string, CancellationToken)"/>
        public static Task InvokeAsync(this HARRRConnection connection, string methodName, object arg1, object arg2, CancellationToken cancellationToken = default) {
            ArgumentNullException.ThrowIfNull(connection);
            return connection.InvokeCoreAsync(methodName, new[] { arg1, arg2 }, cancellationToken);
        }

        /// <inheritdoc cref="InvokeAsync(HARRRConnection, string, CancellationToken)"/>
        public static Task InvokeAsync(this HARRRConnection connection, string methodName, object arg1, object arg2, object arg3, CancellationToken cancellationToken = default) {
            ArgumentNullException.ThrowIfNull(connection);
            return connection.InvokeCoreAsync(methodName, new[] { arg1, arg2, arg3 }, cancellationToken);
        }

        /// <inheritdoc cref="InvokeAsync(HARRRConnection, string, CancellationToken)"/>
        public static Task InvokeAsync(this HARRRConnection connection, string methodName, object arg1, object arg2, object arg3, object arg4, CancellationToken cancellationToken = default) {
            ArgumentNullException.ThrowIfNull(connection);
            return connection.InvokeCoreAsync(methodName, new[] { arg1, arg2, arg3, arg4 }, cancellationToken);
        }

        #endregion

        #region InvokeAsync — typed result

        /// <summary>Invokes a server method and awaits its typed result.</summary>
        public static Task<TResult> InvokeAsync<TResult>(this HARRRConnection connection, string methodName, CancellationToken cancellationToken = default) {
            ArgumentNullException.ThrowIfNull(connection);
            return connection.InvokeCoreAsync<TResult>(methodName, Array.Empty<object>(), cancellationToken);
        }

        /// <inheritdoc cref="InvokeAsync{TResult}(HARRRConnection, string, CancellationToken)"/>
        public static Task<TResult> InvokeAsync<TResult>(this HARRRConnection connection, string methodName, object arg1, CancellationToken cancellationToken = default) {
            ArgumentNullException.ThrowIfNull(connection);
            return connection.InvokeCoreAsync<TResult>(methodName, new[] { arg1 }, cancellationToken);
        }

        /// <inheritdoc cref="InvokeAsync{TResult}(HARRRConnection, string, CancellationToken)"/>
        public static Task<TResult> InvokeAsync<TResult>(this HARRRConnection connection, string methodName, object arg1, object arg2, CancellationToken cancellationToken = default) {
            ArgumentNullException.ThrowIfNull(connection);
            return connection.InvokeCoreAsync<TResult>(methodName, new[] { arg1, arg2 }, cancellationToken);
        }

        /// <inheritdoc cref="InvokeAsync{TResult}(HARRRConnection, string, CancellationToken)"/>
        public static Task<TResult> InvokeAsync<TResult>(this HARRRConnection connection, string methodName, object arg1, object arg2, object arg3, CancellationToken cancellationToken = default) {
            ArgumentNullException.ThrowIfNull(connection);
            return connection.InvokeCoreAsync<TResult>(methodName, new[] { arg1, arg2, arg3 }, cancellationToken);
        }

        /// <inheritdoc cref="InvokeAsync{TResult}(HARRRConnection, string, CancellationToken)"/>
        public static Task<TResult> InvokeAsync<TResult>(this HARRRConnection connection, string methodName, object arg1, object arg2, object arg3, object arg4, CancellationToken cancellationToken = default) {
            ArgumentNullException.ThrowIfNull(connection);
            return connection.InvokeCoreAsync<TResult>(methodName, new[] { arg1, arg2, arg3, arg4 }, cancellationToken);
        }

        #endregion

        #region SendAsync — fire and forget

        /// <summary>Sends a server method call without waiting for it to run.</summary>
        public static Task SendAsync(this HARRRConnection connection, string methodName, CancellationToken cancellationToken = default) {
            ArgumentNullException.ThrowIfNull(connection);
            return connection.SendCoreAsync(methodName, Array.Empty<object>(), cancellationToken);
        }

        /// <inheritdoc cref="SendAsync(HARRRConnection, string, CancellationToken)"/>
        public static Task SendAsync(this HARRRConnection connection, string methodName, object arg1, CancellationToken cancellationToken = default) {
            ArgumentNullException.ThrowIfNull(connection);
            return connection.SendCoreAsync(methodName, new[] { arg1 }, cancellationToken);
        }

        /// <inheritdoc cref="SendAsync(HARRRConnection, string, CancellationToken)"/>
        public static Task SendAsync(this HARRRConnection connection, string methodName, object arg1, object arg2, CancellationToken cancellationToken = default) {
            ArgumentNullException.ThrowIfNull(connection);
            return connection.SendCoreAsync(methodName, new[] { arg1, arg2 }, cancellationToken);
        }

        /// <inheritdoc cref="SendAsync(HARRRConnection, string, CancellationToken)"/>
        public static Task SendAsync(this HARRRConnection connection, string methodName, object arg1, object arg2, object arg3, CancellationToken cancellationToken = default) {
            ArgumentNullException.ThrowIfNull(connection);
            return connection.SendCoreAsync(methodName, new[] { arg1, arg2, arg3 }, cancellationToken);
        }

        /// <inheritdoc cref="SendAsync(HARRRConnection, string, CancellationToken)"/>
        public static Task SendAsync(this HARRRConnection connection, string methodName, object arg1, object arg2, object arg3, object arg4, CancellationToken cancellationToken = default) {
            ArgumentNullException.ThrowIfNull(connection);
            return connection.SendCoreAsync(methodName, new[] { arg1, arg2, arg3, arg4 }, cancellationToken);
        }

        #endregion

        #region StreamAsChannelAsync

        /// <summary>Invokes a streaming server method and returns its items as a channel.</summary>
        public static Task<ChannelReader<TResult>> StreamAsChannelAsync<TResult>(this HARRRConnection connection, string methodName, CancellationToken cancellationToken = default) {
            ArgumentNullException.ThrowIfNull(connection);
            return connection.StreamAsChannelCoreAsync<TResult>(methodName, Array.Empty<object>(), cancellationToken);
        }

        /// <inheritdoc cref="StreamAsChannelAsync{TResult}(HARRRConnection, string, CancellationToken)"/>
        public static Task<ChannelReader<TResult>> StreamAsChannelAsync<TResult>(this HARRRConnection connection, string methodName, object arg1, CancellationToken cancellationToken = default) {
            ArgumentNullException.ThrowIfNull(connection);
            return connection.StreamAsChannelCoreAsync<TResult>(methodName, new[] { arg1 }, cancellationToken);
        }

        /// <inheritdoc cref="StreamAsChannelAsync{TResult}(HARRRConnection, string, CancellationToken)"/>
        public static Task<ChannelReader<TResult>> StreamAsChannelAsync<TResult>(this HARRRConnection connection, string methodName, object arg1, object arg2, CancellationToken cancellationToken = default) {
            ArgumentNullException.ThrowIfNull(connection);
            return connection.StreamAsChannelCoreAsync<TResult>(methodName, new[] { arg1, arg2 }, cancellationToken);
        }

        /// <inheritdoc cref="StreamAsChannelAsync{TResult}(HARRRConnection, string, CancellationToken)"/>
        public static Task<ChannelReader<TResult>> StreamAsChannelAsync<TResult>(this HARRRConnection connection, string methodName, object arg1, object arg2, object arg3, CancellationToken cancellationToken = default) {
            ArgumentNullException.ThrowIfNull(connection);
            return connection.StreamAsChannelCoreAsync<TResult>(methodName, new[] { arg1, arg2, arg3 }, cancellationToken);
        }

        /// <inheritdoc cref="StreamAsChannelAsync{TResult}(HARRRConnection, string, CancellationToken)"/>
        public static Task<ChannelReader<TResult>> StreamAsChannelAsync<TResult>(this HARRRConnection connection, string methodName, object arg1, object arg2, object arg3, object arg4, CancellationToken cancellationToken = default) {
            ArgumentNullException.ThrowIfNull(connection);
            return connection.StreamAsChannelCoreAsync<TResult>(methodName, new[] { arg1, arg2, arg3, arg4 }, cancellationToken);
        }

        #endregion

        #region StreamAsync

        /// <summary>Invokes a streaming server method and returns its items as an async sequence.</summary>
        public static IAsyncEnumerable<TResult> StreamAsync<TResult>(this HARRRConnection connection, string methodName, CancellationToken cancellationToken = default) {
            ArgumentNullException.ThrowIfNull(connection);
            return connection.StreamAsyncCore<TResult>(methodName, Array.Empty<object>(), cancellationToken);
        }

        /// <inheritdoc cref="StreamAsync{TResult}(HARRRConnection, string, CancellationToken)"/>
        public static IAsyncEnumerable<TResult> StreamAsync<TResult>(this HARRRConnection connection, string methodName, object arg1, CancellationToken cancellationToken = default) {
            ArgumentNullException.ThrowIfNull(connection);
            return connection.StreamAsyncCore<TResult>(methodName, new[] { arg1 }, cancellationToken);
        }

        /// <inheritdoc cref="StreamAsync{TResult}(HARRRConnection, string, CancellationToken)"/>
        public static IAsyncEnumerable<TResult> StreamAsync<TResult>(this HARRRConnection connection, string methodName, object arg1, object arg2, CancellationToken cancellationToken = default) {
            ArgumentNullException.ThrowIfNull(connection);
            return connection.StreamAsyncCore<TResult>(methodName, new[] { arg1, arg2 }, cancellationToken);
        }

        /// <inheritdoc cref="StreamAsync{TResult}(HARRRConnection, string, CancellationToken)"/>
        public static IAsyncEnumerable<TResult> StreamAsync<TResult>(this HARRRConnection connection, string methodName, object arg1, object arg2, object arg3, CancellationToken cancellationToken = default) {
            ArgumentNullException.ThrowIfNull(connection);
            return connection.StreamAsyncCore<TResult>(methodName, new[] { arg1, arg2, arg3 }, cancellationToken);
        }

        /// <inheritdoc cref="StreamAsync{TResult}(HARRRConnection, string, CancellationToken)"/>
        public static IAsyncEnumerable<TResult> StreamAsync<TResult>(this HARRRConnection connection, string methodName, object arg1, object arg2, object arg3, object arg4, CancellationToken cancellationToken = default) {
            ArgumentNullException.ThrowIfNull(connection);
            return connection.StreamAsyncCore<TResult>(methodName, new[] { arg1, arg2, arg3, arg4 }, cancellationToken);
        }

        #endregion
    }
}
