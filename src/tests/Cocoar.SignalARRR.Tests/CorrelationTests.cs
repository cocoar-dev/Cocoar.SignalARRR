using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using Cocoar.SignalARRR.Common;
using Cocoar.SignalARRR.Server;
using Xunit;

namespace Cocoar.SignalARRR.Tests;

/// <summary>
/// Covers the invocation-id correlation (O-5): the id is assigned once on the client, travels as
/// an additive wire field, and lands as a span tag on the server side.
/// </summary>
public class CorrelationTests {

    [Fact]
    public void WithInvocationId_assigns_an_id_once() {
        var message = new ClientRequestMessage("M").WithInvocationId();

        Assert.NotNull(message.InvocationId);

        var assigned = message.InvocationId;
        message.WithInvocationId();

        // Retries and re-sends keep the identity of the invocation.
        Assert.Equal(assigned, message.InvocationId);
    }

    [Fact]
    public void WithInvocationId_keeps_an_id_the_caller_set() {
        var callerChosen = Guid.NewGuid();
        var message = new ClientRequestMessage("M") { InvocationId = callerChosen }.WithInvocationId();

        Assert.Equal(callerChosen, message.InvocationId);
    }

    [Fact]
    public void The_server_span_carries_the_invocation_id() {
        var started = new ConcurrentBag<Activity>();
        using var listener = new ActivityListener {
            ShouldListenTo = source => source.Name == SignalARRRTelemetry.ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStarted = a => started.Add(a),
        };
        ActivitySource.AddActivityListener(listener);

        // Method name unique to this test: the ActivitySource is process-global and test classes
        // run in parallel, so the listener also sees other tests' spans.
        var message = new ClientRequestMessage("Correlation.Tagged").WithInvocationId();
        using (SignalARRRServerTelemetry.StartInvocation("TestHub", message, "conn-1")) {
        }

        var span = Assert.Single(started, a => a.OperationName == "TestHub/Correlation.Tagged");
        Assert.Equal(message.InvocationId, span.GetTagItem("signalarrr.invocation_id"));
    }

    [Fact]
    public void A_message_without_an_invocation_id_gets_no_empty_tag() {
        var started = new ConcurrentBag<Activity>();
        using var listener = new ActivityListener {
            ShouldListenTo = source => source.Name == SignalARRRTelemetry.ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStarted = a => started.Add(a),
        };
        ActivitySource.AddActivityListener(listener);

        // Raw TypeScript/Swift callers send no invocation id at all.
        using (SignalARRRServerTelemetry.StartInvocation("TestHub", new ClientRequestMessage("Correlation.Bare"), "conn-1")) {
        }

        var span = Assert.Single(started, a => a.OperationName == "TestHub/Correlation.Bare");
        Assert.Null(span.GetTagItem("signalarrr.invocation_id"));
    }
}
