using System;
using Cocoar.SignalARRR.Common;
using Cocoar.SignalARRR.Common.Exceptions;
using Cocoar.SignalARRR.Server;
using Microsoft.AspNetCore.SignalR;
using Xunit;

namespace Cocoar.SignalARRR.Tests;

/// <summary>
/// Covers the wire error contract (O-7): one machine-readable code per pipeline stage, user codes
/// verbatim, the cause chain nested instead of flattened, and a parse path that tolerates
/// everything older peers may send.
/// </summary>
public class ErrorContractTests {

    // ---- Code mapping: one code per pipeline stage -----------------------------------------

    [Theory]
    [InlineData(typeof(UnauthorizedException), HARRRErrorCodes.Unauthorized)]
    [InlineData(typeof(ArgumentException), HARRRErrorCodes.ArgumentBindingFailed)]
    [InlineData(typeof(OperationCanceledException), HARRRErrorCodes.Cancelled)]
    [InlineData(typeof(TimeoutException), HARRRErrorCodes.Timeout)]
    [InlineData(typeof(InvalidOperationException), HARRRErrorCodes.Internal)]
    public void The_pipeline_stage_determines_the_code(Type exceptionType, string expectedCode) {
        var exception = (Exception)Activator.CreateInstance(exceptionType)!;

        var wrapped = HARRRException.Wrap(exception);

        Assert.Equal(expectedCode, wrapped.Error.Code);
        Assert.Equal(1, wrapped.Error.Version);
    }

    [Fact]
    public void A_resolution_failure_carries_its_specific_code() {
        var wrapped = HARRRException.Wrap(
            new MethodResolutionException(HARRRErrorCodes.InvalidArgumentCount, "no method with 3 arguments"));

        Assert.Equal(HARRRErrorCodes.InvalidArgumentCount, wrapped.Error.Code);
    }

    [Fact]
    public void No_client_responding_carries_its_code() {
        var wrapped = HARRRException.Wrap(new NoClientRespondedException("nobody answered"));

        Assert.Equal(HARRRErrorCodes.NoClientResponded, wrapped.Error.Code);
    }

    // ---- User codes ------------------------------------------------------------------------

    [Fact]
    public void An_application_code_travels_verbatim() {
        var exception = new HARRRException("room_full", "The room is full.");

        var parsed = HARRRError.Parse(exception.Message);

        Assert.Equal("room_full", parsed.Code);
        Assert.Equal("The room is full.", parsed.Message);
    }

    [Fact]
    public void Wrap_is_idempotent() {
        var userThrown = new HARRRException("room_full", "The room is full.");

        // Entry points wrap every exception; a user-thrown HARRRException must pass through
        // unchanged instead of being serialized into itself.
        Assert.Same(userThrown, HARRRException.Wrap(userThrown));
    }

    // ---- Cause chain -----------------------------------------------------------------------

    // A recognized code is the precondition for seeing a chain at all — an unrecognized one is
    // withheld whole (see the redaction tests below), so these two use ArgumentException as the
    // outermost exception rather than the InvalidOperationException they used to use.

    [Fact]
    public void The_cause_chain_is_nested_not_flattened() {
        var exception = new ArgumentException("outer",
            new InvalidOperationException("middle",
                new FormatException("root")));

        var error = HARRRException.Wrap(exception).Error;

        // Previously only GetBaseException() survived — the intermediate step was discarded.
        Assert.Equal(typeof(ArgumentException).FullName, error.Type);
        Assert.Equal(typeof(InvalidOperationException).FullName, error.InnerError?.Type);
        Assert.Equal(typeof(FormatException).FullName, error.InnerError?.InnerError?.Type);
    }

    [Fact]
    public void The_cause_chain_is_depth_limited() {
        Exception exception = new Exception("0");
        for (var i = 1; i < 9; i++) {
            exception = new Exception(i.ToString(), exception);
        }
        exception = new ArgumentException("outermost", exception);

        var depth = 0;
        for (var error = HARRRException.Wrap(exception).Error; error != null; error = error.InnerError) {
            depth++;
        }

        Assert.Equal(5, depth);
    }

    // ---- Withholding the detail of an unexpected failure -------------------------------------

    [Fact]
    public void An_unexpected_failure_does_not_carry_its_detail_to_the_client() {
        var exception = new InvalidOperationException(
            "Login failed for user 'svc_app' on server 'sql-prod-07.internal'.",
            new FormatException("D:\\secrets\\connection.config"));

        var wrapped = HARRRException.Wrap(exception);

        // Nothing about what actually failed may survive into what the caller receives —
        // not the message, not the concrete exception type, not the cause chain.
        Assert.Equal(HARRRErrorCodes.Internal, wrapped.Error.Code);
        Assert.DoesNotContain("sql-prod-07", wrapped.Message);
        Assert.DoesNotContain("svc_app", wrapped.Message);
        Assert.DoesNotContain("secrets", wrapped.Message);
        Assert.DoesNotContain(nameof(InvalidOperationException), wrapped.Message);
        Assert.Null(wrapped.Error.InnerError);
    }

    [Fact]
    public void A_withheld_failure_is_traceable_by_correlation_id() {
        var wrapped = HARRRException.Wrap(new InvalidOperationException("internal detail"));

        // The id is what ties the sentence the user quotes to the exception in the server log,
        // so it has to reach both — hence it travels inside the message rather than as a
        // wire field the TypeScript and Swift clients would each have to learn.
        Assert.False(string.IsNullOrWhiteSpace(wrapped.CorrelationId));
        Assert.Contains(wrapped.CorrelationId!, wrapped.Error.Message);
    }

    [Fact]
    public void A_recognized_failure_keeps_its_detail() {
        var wrapped = HARRRException.Wrap(new TimeoutException("the call took longer than 30s"));

        Assert.Equal(HARRRErrorCodes.Timeout, wrapped.Error.Code);
        Assert.Equal("the call took longer than 30s", wrapped.Error.Message);
        Assert.Null(wrapped.CorrelationId);
    }

    [Fact]
    public void An_application_error_is_never_withheld() {
        // The whole point of the (code, message) constructor is that both halves are meant for
        // the client — redaction must not touch it.
        var wrapped = HARRRException.Wrap(new HARRRException("room_full", "The room is full."));

        Assert.Equal("room_full", wrapped.Error.Code);
        Assert.Equal("The room is full.", wrapped.Error.Message);
        Assert.Null(wrapped.CorrelationId);
    }

    // ---- Round trip and client surface ------------------------------------------------------

    [Fact]
    public void A_wrapped_error_round_trips_through_parse() {
        var wrapped = HARRRException.Wrap(new UnauthorizedException());

        var parsed = HARRRError.Parse(wrapped.Message);

        Assert.Equal(1, parsed.Version);
        Assert.Equal(HARRRErrorCodes.Unauthorized, parsed.Code);
        Assert.Equal(typeof(UnauthorizedException).FullName, parsed.Type);
    }

    [Fact]
    public void The_remote_exception_exposes_the_human_message_and_the_raw_code() {
        var received = new HubException(new HARRRException("room_full", "The room is full.").Message);

        var remote = HARRRRemoteException.FromReceived(received);

        Assert.Equal("The room is full.", remote.Message);
        Assert.Equal("room_full", remote.Code);
        Assert.Same(received, remote.InnerException);
        // Still catchable as HubException — the compatibility guarantee.
        Assert.IsAssignableFrom<HubException>(remote);
    }

    [Fact]
    public void A_message_from_an_older_server_still_parses() {
        // Old envelope: no Version, no Code, real type name.
        var remote = HARRRRemoteException.FromReceived(
            new HubException("{\"Type\":\"System.InvalidOperationException\",\"Message\":\"boom\"}"));

        Assert.Equal("boom", remote.Message);
        Assert.Equal(HARRRErrorCodes.Internal, remote.Code);
    }

    [Fact]
    public void Garbage_still_becomes_a_usable_error() {
        var remote = HARRRRemoteException.FromReceived(new HubException("not json at all"));

        Assert.Equal("not json at all", remote.Message);
        Assert.Equal(HARRRErrorCodes.Internal, remote.Code);
    }

    [Fact]
    public void Unknown_codes_normalize_to_internal_but_stay_readable_raw() {
        var error = new HARRRError { Version = 1, Code = "some_future_code", Message = "m" };

        // Framework bucketing folds what it does not know; the raw code stays for app logic.
        Assert.Equal(HARRRErrorCodes.Internal, error.NormalizedCode);
        Assert.Equal("some_future_code", new HARRRRemoteException(error).Code);
    }
}
