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

    [Fact]
    public void The_cause_chain_is_nested_not_flattened() {
        var exception = new InvalidOperationException("outer",
            new ArgumentException("middle",
                new FormatException("root")));

        var error = HARRRException.Wrap(exception).Error;

        // Previously only GetBaseException() survived — the intermediate step was discarded.
        Assert.Equal(typeof(InvalidOperationException).FullName, error.Type);
        Assert.Equal(typeof(ArgumentException).FullName, error.InnerError?.Type);
        Assert.Equal(typeof(FormatException).FullName, error.InnerError?.InnerError?.Type);
    }

    [Fact]
    public void The_cause_chain_is_depth_limited() {
        Exception exception = new Exception("0");
        for (var i = 1; i < 10; i++) {
            exception = new Exception(i.ToString(), exception);
        }

        var depth = 0;
        for (var error = HARRRException.Wrap(exception).Error; error != null; error = error.InnerError) {
            depth++;
        }

        Assert.Equal(5, depth);
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
