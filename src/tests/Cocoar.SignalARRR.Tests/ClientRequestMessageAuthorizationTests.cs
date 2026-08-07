using System;
using System.Threading.Tasks;
using Cocoar.SignalARRR.Common;
using Xunit;

namespace Cocoar.SignalARRR.Tests;

/// <summary>
/// Covers the non-blocking token resolution introduced for P-3.
/// </summary>
public class ClientRequestMessageAuthorizationTests {

    [Fact]
    public async Task WithAuthorizationAsync_resolves_the_provider() {
        var message = await new ClientRequestMessage("M").WithAuthorizationAsync(() => Task.FromResult("token-1"));

        Assert.Equal("token-1", message.Authorization);
        Assert.Same(message, await message.WithAuthorizationAsync(() => Task.FromResult("token-2")));
    }

    [Fact]
    public async Task WithAuthorizationAsync_tolerates_a_missing_provider_and_a_null_token() {
        var withoutProvider = await new ClientRequestMessage("M").WithAuthorizationAsync(null!);
        Assert.Equal(string.Empty, withoutProvider.Authorization);

        var withNullToken = await new ClientRequestMessage("M").WithAuthorizationAsync(() => Task.FromResult<string>(null!));
        Assert.Equal(string.Empty, withNullToken.Authorization);
    }
}
