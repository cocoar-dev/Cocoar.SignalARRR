using System.Collections.Generic;
using Cocoar.SignalARRR.Server;
using Microsoft.Extensions.Primitives;
using Xunit;

namespace Cocoar.SignalARRR.Tests;

/// <summary>
/// Pins the lookup behaviour of <see cref="ClientAttributes"/>.
/// </summary>
/// <remarks>
/// It used to derive from <c>Dictionary&lt;string, StringValues&gt;</c> and hide the inherited
/// indexer with a <c>new</c> one returning <c>string?</c>. The same key therefore answered
/// differently depending on the static type it was asked through — <c>null</c> via
/// <c>ClientAttributes</c>, <see cref="KeyNotFoundException"/> via the dictionary — and consumers had
/// no way to see which one they were holding. These tests exist to keep the two from diverging again.
/// </remarks>
public class ClientAttributesTests {

    private static ClientAttributes WithVersion() {
        var attributes = new ClientAttributes();
        attributes.Set("AppVersion", "2.1.0");
        return attributes;
    }

    [Fact]
    public void The_indexer_answers_the_same_through_the_interface_as_through_the_type() {
        var attributes = WithVersion();
        IReadOnlyDictionary<string, StringValues> asDictionary = attributes;

        Assert.Equal(attributes["AppVersion"], asDictionary["AppVersion"]);
        Assert.Equal(new StringValues("2.1.0"), attributes["AppVersion"]);
    }

    [Fact]
    public void A_missing_key_throws_from_the_indexer_and_is_null_from_GetString() {
        var attributes = WithVersion();

        Assert.Throws<KeyNotFoundException>(() => attributes["Nope"]);
        Assert.Null(attributes.GetString("Nope"));
        Assert.Equal("2.1.0", attributes.GetString("AppVersion"));
    }

    [Fact]
    public void Keys_are_matched_without_regard_to_case() {
        var attributes = WithVersion();

        Assert.True(attributes.Has("appversion"));
        Assert.True(attributes.Has("APPVERSION", "2.1.0"));
        Assert.True(attributes.ContainsKey("AppVERSION"));
        Assert.Equal("2.1.0", attributes.GetString("appVersion"));
    }

    [Fact]
    public void Has_with_a_value_compares_against_every_stored_value() {
        var attributes = new ClientAttributes();
        attributes.Set("Role", new StringValues(new[] { "reader", "writer" }));

        Assert.True(attributes.Has("Role", "writer"));
        Assert.False(attributes.Has("Role", "admin"));
        Assert.False(attributes.Has("Missing", "writer"));
    }

    [Fact]
    public void The_attributes_enumerate_as_key_value_pairs() {
        var attributes = WithVersion();
        attributes.Set("Platform", "TestRunner");

        Assert.Equal(2, attributes.Count);
        Assert.Equal(
            new[] { "AppVersion", "Platform" },
            new List<string>(attributes.Keys));
        Assert.Contains(new KeyValuePair<string, StringValues>("Platform", "TestRunner"), attributes);
    }
}
