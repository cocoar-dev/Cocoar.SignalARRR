using System.Threading.Tasks;
using Cocoar.SignalARRR.Server;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
using Xunit;

namespace Cocoar.SignalARRR.Tests;

/// <summary>
/// <see cref="SignalARRRAccessTokenValidationMiddleware"/> turns the <c>access_token</c> query
/// parameter into an <c>Authorization</c> header — only as a fallback, never over a header the
/// client sent itself.
/// </summary>
public class AccessTokenValidationMiddlewareTests {

    private sealed class ProbeHub : Hub { }

    private static async Task<string> Run(string query, string? header = null, bool signalR = true) {
        var context = new DefaultHttpContext();
        context.Request.QueryString = new QueryString(query);
        if (header != null) context.Request.Headers.Authorization = header;
        var metadata = signalR
            ? new EndpointMetadataCollection(new HubMetadata(typeof(ProbeHub)))
            : new EndpointMetadataCollection();
        context.SetEndpoint(new Endpoint(null, metadata, "probe"));

        string seen = "";
        var middleware = new SignalARRRAccessTokenValidationMiddleware(ctx => {
            seen = ctx.Request.Headers.Authorization.ToString();
            return Task.CompletedTask;
        });
        await middleware.Invoke(context);
        return seen;
    }

    [Fact]
    public async Task Copies_the_query_token_when_no_header_is_present() {
        Assert.Equal("Bearer from-query", await Run("?access_token=from-query"));
    }

    [Fact]
    public async Task Keeps_an_existing_header_over_the_query_token() {
        Assert.Equal("Bearer from-header", await Run("?access_token=from-query", "Bearer from-header"));
    }

    [Fact]
    public async Task Leaves_requests_without_a_query_token_alone() {
        Assert.Equal("", await Run(""));
    }

    [Fact]
    public async Task Ignores_endpoints_that_are_not_signalr_hubs() {
        Assert.Equal("", await Run("?access_token=from-query", signalR: false));
    }
}
