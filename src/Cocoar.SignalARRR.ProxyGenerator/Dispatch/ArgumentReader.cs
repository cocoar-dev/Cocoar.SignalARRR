using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Cocoar.SignalARRR.ProxyGenerator {
    /// <summary>
    /// The arguments of one incoming call, read slot by slot with the type the generated dispatcher
    /// knows statically.
    /// </summary>
    public abstract class ArgumentReader {

        private readonly IReadOnlyList<object?> _arguments;

        protected ArgumentReader(IReadOnlyList<object?> arguments, CallContext context) {
            _arguments = arguments ?? Array.Empty<object?>();
            Context = context ?? throw new ArgumentNullException(nameof(context));
        }

        /// <summary>How many arguments the message carried.</summary>
        public int Count => _arguments.Count;

        /// <summary>The call this reader belongs to.</summary>
        public CallContext Context { get; }

        /// <summary>The raw value in slot <paramref name="index"/>, as the hub protocol delivered it.</summary>
        public object? GetRaw(int index) => _arguments[index];

        /// <summary>Reads slot <paramref name="index"/> as <typeparamref name="T"/>. A JSON <c>null</c> reads as <c>default</c>.</summary>
        public abstract T Read<T>(int index);

        /// <summary>
        /// The token for a <see cref="CancellationToken"/> parameter: the one the server sent a
        /// reference for in this slot, else the call's own token.
        /// </summary>
        public CancellationToken ReadCancellationToken(int index) => Context.ResolveCancellationToken(this, index);

        /// <summary>The stream for a <see cref="Stream"/> parameter, downloaded from the reference in this slot.</summary>
        public Task<Stream?> ReadStreamAsync(int index) => Context.ResolveStreamAsync(this, index);
    }

    /// <summary>
    /// What a call needs from the connection it arrived on: the tokens it may be cancelled with and
    /// the file transfers its <see cref="Stream"/> arguments travel through.
    /// </summary>
    public abstract class CallContext {

        /// <summary>The token of the call itself, cancelled by the server or by the connection ending.</summary>
        public abstract CancellationToken CallCancellation { get; }

        /// <summary>The token for the <see cref="CancellationToken"/> parameter in slot <paramref name="index"/>.</summary>
        public abstract CancellationToken ResolveCancellationToken(ArgumentReader reader, int index);

        /// <summary>The stream for the <see cref="Stream"/> parameter in slot <paramref name="index"/>.</summary>
        public abstract Task<Stream?> ResolveStreamAsync(ArgumentReader reader, int index);
    }
}
