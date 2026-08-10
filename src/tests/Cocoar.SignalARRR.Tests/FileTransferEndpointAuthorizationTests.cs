using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Cocoar.SignalARRR.Server;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Cocoar.SignalARRR.Tests;

/// <summary>
/// Covers whether the file-transfer endpoints are protected the same way their hub is.
/// </summary>
/// <remarks>
/// <c>/download/{id}</c> and <c>/upload/{id}</c> are plain HTTP endpoints, so neither the hub's
/// <c>[Authorize]</c> nor a <c>.RequireAuthorization()</c> on the returned builder reached them on
/// their own. The attribute case was closed earlier; the chained case — the idiomatic ASP.NET Core
/// style, and the one the README shows — was not, so a hub secured that way handed out anonymous
/// transfer endpoints.
/// </remarks>
public class FileTransferEndpointAuthorizationTests {

    private static IEndpointRouteBuilder BuildRoutes() {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddRouting();
        services.AddAuthorization();
        services.AddSignalR();
        services.AddSignalARRR(_ => { });
        // MapHub builds SignalR's connection manager, which wants the host lifetime. Nothing here
        // runs a host, and nothing under test observes shutdown.
        services.AddSingleton<IHostApplicationLifetime>(new NoHostLifetime());

        return new CollectingEndpointRouteBuilder(services.BuildServiceProvider());
    }

    private static Endpoint EndpointFor(IEndpointRouteBuilder routes, string rawRoute) =>
        routes.DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Single(endpoint => endpoint.RoutePattern.RawText == rawRoute);

    private static bool IsProtected(Endpoint endpoint) =>
        endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>().Count > 0;

    [Fact]
    public void A_chained_RequireAuthorization_reaches_the_transfer_endpoints() {
        var routes = BuildRoutes();

        routes.MapSignalARRRHub<OpenTransferHub>("/open").RequireAuthorization();

        // The hub was protected and its transfers were not — the whole point of the finding.
        Assert.True(IsProtected(EndpointFor(routes, "/open")));
        Assert.True(IsProtected(EndpointFor(routes, "/open/download/{id}")));
        Assert.True(IsProtected(EndpointFor(routes, "/open/upload/{id}")));
    }

    [Fact]
    public void An_Authorize_attribute_on_the_hub_still_reaches_them() {
        var routes = BuildRoutes();

        routes.MapSignalARRRHub<ProtectedTransferHub>("/protected");

        // The other way of securing a hub, closed in an earlier block; asserted here so the two
        // cannot drift apart again.
        Assert.True(IsProtected(EndpointFor(routes, "/protected/download/{id}")));
        Assert.True(IsProtected(EndpointFor(routes, "/protected/upload/{id}")));
    }

    [Fact]
    public void An_unsecured_hub_still_gets_unsecured_transfer_endpoints() {
        var routes = BuildRoutes();

        routes.MapSignalARRRHub<OpenTransferHub>("/anonymous");

        // Guards the two above: they would pass just as well against a version that protects the
        // transfer endpoints unconditionally, which would break every open hub.
        Assert.False(IsProtected(EndpointFor(routes, "/anonymous/download/{id}")));
        Assert.False(IsProtected(EndpointFor(routes, "/anonymous/upload/{id}")));
    }

    [Fact]
    public void The_configureOptions_overload_behaves_the_same() {
        var routes = BuildRoutes();

        routes.MapSignalARRRHub<OpenTransferHub>("/configured", _ => { }).RequireAuthorization();

        // Two overloads, one of which is easy to fix and forget.
        Assert.True(IsProtected(EndpointFor(routes, "/configured/download/{id}")));
        Assert.True(IsProtected(EndpointFor(routes, "/configured/upload/{id}")));
    }

    private sealed class NoHostLifetime : IHostApplicationLifetime {
        public CancellationToken ApplicationStarted => CancellationToken.None;
        public CancellationToken ApplicationStopping => CancellationToken.None;
        public CancellationToken ApplicationStopped => CancellationToken.None;
        public void StopApplication() { }
    }

    /// <summary>Collects what was mapped without standing a host up.</summary>
    private sealed class CollectingEndpointRouteBuilder : IEndpointRouteBuilder {
        public CollectingEndpointRouteBuilder(IServiceProvider serviceProvider) => ServiceProvider = serviceProvider;

        public IServiceProvider ServiceProvider { get; }
        public ICollection<EndpointDataSource> DataSources { get; } = new List<EndpointDataSource>();
        public IApplicationBuilder CreateApplicationBuilder() => new ApplicationBuilder(ServiceProvider);
    }
}

public class OpenTransferHub : HARRR {
    public OpenTransferHub(IServiceProvider serviceProvider) : base(serviceProvider) { }
}

[Authorize]
public class ProtectedTransferHub : HARRR {
    public ProtectedTransferHub(IServiceProvider serviceProvider) : base(serviceProvider) { }
}
