using System;
using System.Text.Json;
using Cocoar.SignalARRR.Common;
using Cocoar.SignalARRR.Common.Exceptions;
using Microsoft.AspNetCore.SignalR;

namespace Cocoar.SignalARRR.Server {
    /// <summary>
    /// Carries a structured <see cref="HARRRError"/> across the wire: the envelope is serialized
    /// as pure JSON into the <see cref="HubException"/> message, which SignalR passes through to
    /// the caller verbatim.
    /// </summary>
    /// <remarks>
    /// The <see cref="HARRRError.Code"/> is derived from the pipeline stage that failed (see
    /// <see cref="HARRRErrorCodes"/>). Application code can put its own code on the wire by
    /// throwing <c>new HARRRException("room_full", "The room is full")</c> — user codes travel
    /// verbatim and are never overwritten by the framework. The cause chain is nested as
    /// <see cref="HARRRError.InnerError"/> instead of being flattened to
    /// <c>GetBaseException()</c>, which used to discard every intermediate step.
    /// </remarks>
    public class HARRRException : HubException {

        private const int MaxInnerErrorDepth = 5;

        /// <summary>The structured error this exception puts on the wire.</summary>
        public HARRRError Error { get; }

        public HARRRException(Exception exception) : this(ToError(exception)) {
        }

        /// <summary>
        /// An application-defined error: <paramref name="code"/> reaches every client verbatim as
        /// the machine-readable <see cref="HARRRError.Code"/>. Use lower_snake_case; the names in
        /// <see cref="HARRRErrorCodes"/> are reserved for the framework.
        /// </summary>
        public HARRRException(string code, string message) : this(new HARRRError {
            Version = 1,
            Code = code,
            Type = typeof(HARRRException).FullName!,
            Message = message,
        }) {
        }

        private HARRRException(HARRRError error) : base(JsonSerializer.Serialize(error)) {
            Error = error;
        }

        /// <summary>
        /// Wraps an exception for the wire — idempotently: an exception that already carries a
        /// structured error (a user-thrown <see cref="HARRRException"/>, or one the pipeline
        /// wrapped earlier) passes through unchanged instead of being serialized into itself.
        /// </summary>
        public static HARRRException Wrap(Exception exception) {
            var unwrapped = UnwrapInvocationLayers(exception);
            return unwrapped as HARRRException ?? new HARRRException(unwrapped);
        }

        /// <summary>
        /// Peels reflection wrappers only. The old code used <c>GetBaseException()</c>, which
        /// also flattened *every* meaningful intermediate cause; this removes just the layers the
        /// invocation machinery adds around what the method actually threw.
        /// </summary>
        private static Exception UnwrapInvocationLayers(Exception exception) {
            while (true) {
                switch (exception) {
                    case System.Reflection.TargetInvocationException { InnerException: { } inner }:
                        exception = inner;
                        continue;
                    case AggregateException { InnerExceptions.Count: 1 } aggregate when aggregate.InnerException != null:
                        exception = aggregate.InnerException;
                        continue;
                    default:
                        return exception;
                }
            }
        }

        private static HARRRError ToError(Exception exception) {
            var error = ToErrorNode(exception, MaxInnerErrorDepth);
            error.Version = 1;
            error.Code = MapCode(exception);
            return error;
        }

        private static HARRRError ToErrorNode(Exception exception, int remainingDepth) {
            return new HARRRError {
                Type = exception.GetType().FullName!,
                Message = exception.Message,
#if DEBUG
                StackTrace = exception.StackTrace,
#endif
                InnerError = exception.InnerException != null && remainingDepth > 1
                    ? ToErrorNode(exception.InnerException, remainingDepth - 1)
                    : null,
            };
        }

        /// <summary>
        /// One code per pipeline stage — matched on the exception the stage throws, never by
        /// message text. Everything unrecognized is <see cref="HARRRErrorCodes.Internal"/>: the
        /// invoked method itself threw.
        /// </summary>
        private static string MapCode(Exception exception) => exception switch {
            UnauthorizedException => HARRRErrorCodes.Unauthorized,
            MethodResolutionException resolution => resolution.Code,
            // Covers the binder (deserialization/coercion) and methods rejecting their input —
            // both mean "fix the call".
            ArgumentException => HARRRErrorCodes.ArgumentBindingFailed,
            OperationCanceledException => HARRRErrorCodes.Cancelled,
            TimeoutException => HARRRErrorCodes.Timeout,
            NoClientRespondedException => HARRRErrorCodes.NoClientResponded,
            _ => HARRRErrorCodes.Internal,
        };
    }
}
