// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Channels;

namespace Cocoar.SignalARRR.Server.ExtensionMethods {
    internal static class ObservableExtensions {
        public static ChannelReader<T> AsChannelReader<T>(this IObservable<T> observable, int? maxBufferSize = null, CancellationToken cancellationToken = default) {
            // This sample shows adapting an observable to a ChannelReader without 
            // back pressure, if the connection is slower than the producer, memory will
            // start to increase.

            // If the channel is bounded, TryWrite will return false and effectively
            // drop items.

            // The other alternative is to use a bounded channel, and when the limit is reached
            // block on WaitToWriteAsync. This will block a thread pool thread and isn't recommended and isn't shown here.
            var channel = maxBufferSize != null ? Channel.CreateBounded<T>(maxBufferSize.Value) : Channel.CreateUnbounded<T>();

            var cancel = Observable.Create<T>(observer => cancellationToken.Register(() => observer.OnNext(default)));

            var disposable = observable.TakeUntil(cancel).Subscribe(
                                value => channel.Writer.TryWrite(value),
                                error => channel.Writer.TryComplete(error),
                                () => channel.Writer.TryComplete());

            // Complete the subscription on the reader completing
            channel.Reader.Completion.ContinueWith(task => disposable.Dispose());

            return channel.Reader;
        }

        public static ChannelReader<T> AsChannelReader<T>(this IObservable<T> observable, CancellationToken cancellationToken = default) {
            return observable.AsChannelReader<T>(null, cancellationToken);
        }

        internal static ChannelReader<T> AsChannelReaderInternal<T>(this IObservable<T> observable,
            CancellationToken cancellationToken = default) => AsChannelReader(observable, null, cancellationToken);

    }
}
