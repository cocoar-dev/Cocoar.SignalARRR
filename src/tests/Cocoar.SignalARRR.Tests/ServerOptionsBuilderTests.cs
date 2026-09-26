using System;
using Cocoar.SignalARRR.Server;
using Cocoar.SignalARRR.Server.ExtensionMethods;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Cocoar.SignalARRR.Tests;

/// <summary>
/// Connection-bound schemes are declared through the options builder <c>AddSignalARRR</c> hands out,
/// not by casting the builder to the options instance.
/// </summary>
public class ServerOptionsBuilderTests {

    private static SignalARRRServerOptions Configure(Action<SignalARRRServerOptionsBuilder> configure) {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSignalR();
        services.AddSignalARRR(configure);
        return services.BuildServiceProvider().GetRequiredService<SignalARRRServerOptions>();
    }

    [Fact]
    public void WithConnectionBoundSchemes_declares_each_scheme_once() {
        var options = Configure(b => b
            .WithConnectionBoundSchemes("Cookies", "Identity.Application")
            .WithConnectionBoundSchemes("Cookies"));

        Assert.Equal(new[] { "Cookies", "Identity.Application" }, options.ConnectionBoundSchemes);
    }

    [Fact]
    public void WithConnectionBoundSchemes_rejects_an_empty_name() {
        Assert.Throws<ArgumentException>(() => new SignalARRRServerOptionsBuilder().WithConnectionBoundSchemes(" "));
    }
}
