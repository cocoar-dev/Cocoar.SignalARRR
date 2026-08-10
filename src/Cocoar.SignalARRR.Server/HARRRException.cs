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

        /// <summary>
        /// Set only when the detail was withheld from the client, i.e. for
        /// <see cref="HARRRErrorCodes.Internal"/>. It appears both in the message the caller
        /// receives and in the server-side log entry, so a user can quote it in a bug report and
        /// an operator can find the exception it stands for. Never travels as its own wire field.
        /// </summary>
        public string? CorrelationId { get; }

        public HARRRException(Exception exception) : this(BuildError(exception)) {
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

        private HARRRException(HARRRError error) : this((error, (string?)null)) {
        }

        private HARRRException((HARRRError Error, string? CorrelationId) built)
            : base(JsonSerializer.Serialize(built.Error)) {
            Error = built.Error;
            CorrelationId = built.CorrelationId;
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

        /// <summary>
        /// Builds the wire error, withholding the detail of anything the pipeline does not
        /// recognize.
        /// </summary>
        /// <remarks>
        /// A recognized code names a stage this library controls, so its message is ours to show:
        /// "not authorized", "no method with 3 arguments", "the call timed out". Everything else is
        /// whatever the invoked method happened to throw, and its <c>Message</c> routinely carries
        /// things the caller has no business seeing — a <c>SqlException</c> naming the server and
        /// database, a <c>FileNotFoundException</c> naming an absolute path, a DI failure spelling
        /// out the internal type graph. SignalR's own <c>EnableDetailedErrors=false</c> default
        /// exists to stop exactly that, and passing the error contract through used to bypass it
        /// with no way to opt back in.
        /// <para>
        /// So <see cref="HARRRErrorCodes.Internal"/> now travels as a fixed sentence plus a
        /// correlation id, and the exception itself is logged at the call site under that same id.
        /// Nothing is lost — it moves from a place the caller can read to a place the operator can.
        /// Application errors are unaffected: throw
        /// <c>new HARRRException("room_full", "The room is full")</c> and both halves reach the
        /// client verbatim, which is what that constructor is for.
        /// </para>
        /// </remarks>
        private static (HARRRError Error, string? CorrelationId) BuildError(Exception exception) {
            var code = MapCode(exception);

            if (code != HARRRErrorCodes.Internal) {
                var detailed = ToErrorNode(exception, MaxInnerErrorDepth);
                detailed.Version = 1;
                detailed.Code = code;
                return (detailed, null);
            }

            // Short and copyable: this ends up in a bug report typed by hand, not in a parser.
            var correlationId = Guid.NewGuid().ToString("n").Substring(0, 12);

            return (new HARRRError {
                Version = 1,
                Code = code,
                // The concrete exception type is withheld with the message: naming
                // SqlException or NpgsqlException already tells a caller what runs behind the hub.
                Type = typeof(HARRRException).FullName!,
                Message = "The server failed to handle this call. " +
                          $"Correlation id: {correlationId}",
#if DEBUG
                StackTrace = exception.StackTrace,
#endif
            }, correlationId);
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
