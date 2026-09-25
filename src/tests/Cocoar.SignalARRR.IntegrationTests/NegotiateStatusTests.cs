using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Cocoar.SignalARRR.Client;
using Microsoft.AspNetCore.SignalR.Client;
using Xunit;

namespace Cocoar.SignalARRR.IntegrationTests;

/// <summary>
/// A connection the server rejects at negotiate reports the HTTP status — in .NET through SignalR's
/// own <see cref="HttpRequestException.StatusCode"/>, the counterpart of <c>statusCode</c> in the
/// TypeScript, Swift and Kotlin clients.
/// </summary>
[Collection("Simple")]
public class NegotiateStatusTests {
    private readonly SignalARRRServerInstanceFixture _fixture;

    public NegotiateStatusTests(SignalARRRServerInstanceFixture fixture) {
        _fixture = fixture;
    }

    [Fact]
    public async Task A_rejected_negotiate_reports_its_HTTP_status() {
        await using var connection = HARRRConnection.Create(builder => builder.WithUrl($"{_fixture.ServerUrl}/signalr/no-such-hub"));
        var error = await Assert.ThrowsAsync<HttpRequestException>(() => connection.StartAsync(TestContext.Current.CancellationToken));
        Assert.Equal(HttpStatusCode.NotFound, error.StatusCode);
    }
}
