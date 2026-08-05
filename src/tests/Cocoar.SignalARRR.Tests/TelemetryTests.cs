using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Linq;
using Cocoar.SignalARRR.Common;
using Cocoar.SignalARRR.Server;
using Xunit;

namespace Cocoar.SignalARRR.Tests;

/// <summary>
/// Covers the telemetry primitives (O-1/O-2): trace-context stamping and parsing, the server
/// invocation span joining the caller's trace, and the invocation-duration metric with its
/// outcome tag — cancellation is an expected outcome, not an error.
/// </summary>
public class TelemetryTests {

    // ---- Trace context on the messages -----------------------------------------------------

    [Fact]
    public void WithTraceContext_stamps_the_current_activity() {
        using var activity = new Activity("test");
        activity.SetIdFormat(ActivityIdFormat.W3C);
        activity.Start();

        var message = new ClientRequestMessage("M").WithTraceContext();

        Assert.Equal(activity.Id, message.TraceParent);
    }

    [Fact]
    public void WithTraceContext_without_an_activity_leaves_the_message_untouched() {
        Assert.Null(Activity.Current);

        var message = new ClientRequestMessage("M").WithTraceContext();

        Assert.Null(message.TraceParent);
        Assert.Null(message.TraceState);
    }

    [Fact]
    public void ParseTraceContext_round_trips_a_stamped_context() {
        using var activity = new Activity("test");
        activity.SetIdFormat(ActivityIdFormat.W3C);
        activity.Start();
        var message = new ServerRequestMessage("M").WithTraceContext();

        var context = SignalARRRTelemetry.ParseTraceContext(message.TraceParent, message.TraceState);

        Assert.Equal(activity.TraceId, context.TraceId);
        Assert.True(context.IsRemote);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-traceparent")]
    public void ParseTraceContext_tolerates_missing_or_malformed_values(string? traceParent) {
        // Older SDKs and TypeScript/Swift clients send no trace context at all; a malformed one
        // must start a fresh trace, not fail the message.
        Assert.Equal(default, SignalARRRTelemetry.ParseTraceContext(traceParent, null));
    }

    // ---- Server invocation span and metric -------------------------------------------------

    private sealed class Capture : IDisposable {
        public readonly ConcurrentBag<Activity> Started = new();
        public readonly List<(double Value, Dictionary<string, object?> Tags)> Measurements = new();
        private readonly ActivityListener _activityListener;
        private readonly MeterListener _meterListener;

        public Capture() {
            _activityListener = new ActivityListener {
                ShouldListenTo = source => source.Name == SignalARRRTelemetry.ActivitySourceName,
                Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
                ActivityStarted = a => Started.Add(a),
            };
            ActivitySource.AddActivityListener(_activityListener);

            _meterListener = new MeterListener();
            _meterListener.InstrumentPublished = (instrument, listener) => {
                if (instrument.Name == "signalarrr.server.invocation.duration") {
                    listener.EnableMeasurementEvents(instrument);
                }
            };
            _meterListener.SetMeasurementEventCallback<double>((instrument, value, tags, state) => {
                var tagDict = new Dictionary<string, object?>();
                foreach (var tag in tags) {
                    tagDict[tag.Key] = tag.Value;
                }
                lock (Measurements) {
                    Measurements.Add((value, tagDict));
                }
            });
            _meterListener.Start();
        }

        public void Dispose() {
            _activityListener.Dispose();
            _meterListener.Dispose();
        }
    }

    [Fact]
    public void The_server_span_joins_the_callers_trace() {
        using var capture = new Capture();

        using var caller = new Activity("caller");
        caller.SetIdFormat(ActivityIdFormat.W3C);
        caller.Start();
        var message = new ClientRequestMessage("Ns.IFoo|Do").WithTraceContext();
        caller.Stop();

        using (SignalARRRServerTelemetry.StartInvocation("TestHub", message, "conn-1")) {
        }

        var span = Assert.Single(capture.Started, a => a.OperationName == "TestHub/Ns.IFoo|Do");
        Assert.Equal(ActivityKind.Server, span.Kind);
        Assert.Equal(caller.TraceId, span.TraceId);
        Assert.Equal("signalarrr", span.GetTagItem("rpc.system"));
        Assert.Equal("conn-1", span.GetTagItem("signalarrr.connection_id"));
    }

    [Fact]
    public void A_completed_invocation_records_outcome_ok() {
        using var capture = new Capture();

        using (SignalARRRServerTelemetry.StartInvocation("TestHub", new ClientRequestMessage("M"), "conn-1")) {
        }

        var measurement = Assert.Single(capture.Measurements);
        Assert.Equal("ok", measurement.Tags["signalarrr.outcome"]);
        Assert.Equal("TestHub", measurement.Tags["signalarrr.hub"]);
        Assert.Equal("M", measurement.Tags["signalarrr.method"]);
    }

    [Fact]
    public void A_failed_invocation_records_outcome_error_and_the_exception_type() {
        using var capture = new Capture();

        using (var scope = SignalARRRServerTelemetry.StartInvocation("TestHub", new ClientRequestMessage("M"), "conn-1")) {
            scope.RecordFailure(new InvalidOperationException("boom"));
        }

        var measurement = Assert.Single(capture.Measurements);
        Assert.Equal("error", measurement.Tags["signalarrr.outcome"]);

        var span = Assert.Single(capture.Started);
        Assert.Equal(ActivityStatusCode.Error, span.Status);
        Assert.Equal(typeof(InvalidOperationException).FullName, span.GetTagItem("error.type"));
    }

    [Fact]
    public void A_cancelled_invocation_is_an_outcome_not_an_error() {
        using var capture = new Capture();

        using (var scope = SignalARRRServerTelemetry.StartInvocation("TestHub", new ClientRequestMessage("M"), "conn-1")) {
            scope.RecordFailure(new OperationCanceledException());
        }

        var measurement = Assert.Single(capture.Measurements);
        Assert.Equal("cancelled", measurement.Tags["signalarrr.outcome"]);

        var span = Assert.Single(capture.Started);
        Assert.Equal(ActivityStatusCode.Ok, span.Status);
        Assert.Null(span.GetTagItem("error.type"));
    }
}
