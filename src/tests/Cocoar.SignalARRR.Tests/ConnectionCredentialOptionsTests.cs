using System;
using System.Threading.Tasks;
using Cocoar.SignalARRR.Client;
using Microsoft.AspNetCore.SignalR.Client;
using Xunit;

namespace Cocoar.SignalARRR.Tests;

/// <summary>
/// The credential options of the .NET client: <c>WithCredential</c>, <c>WithConnectionCredential</c>
/// and <c>WithMessageCredential</c>, nothing coupled implicitly, and a clear error instead of a
/// silent choice when a credential is set in two places.
/// </summary>
public class ConnectionCredentialOptionsTests {

    private const string Url = "http://127.0.0.1:1/hub";

    private static Task<string> TokenAsync() => Task.FromResult("async-token");
    private static Task<string?> NullableTokenAsync() => Task.FromResult<string?>("async-token");
    private static string Token() => "sync-token";

    [Fact]
    public void Credential_sets_connection_and_message_credential() {
        HARRRConnectionOptions options = new HARRRConnectionOptionsBuilder().WithCredential("both");
        Assert.NotNull(options.ConnectionCredential);
        Assert.NotNull(options.MessageCredential);
    }

    [Fact]
    public void Message_credential_does_not_set_the_connection_credential() {
        HARRRConnectionOptions options = new HARRRConnectionOptionsBuilder().WithMessageCredential("message");
        Assert.Null(options.ConnectionCredential);
        Assert.NotNull(options.MessageCredential);
    }

    [Fact]
    public void Connection_credential_does_not_set_the_message_credential() {
        HARRRConnectionOptions options = new HARRRConnectionOptionsBuilder().WithConnectionCredential("connection");
        Assert.NotNull(options.ConnectionCredential);
        Assert.Null(options.MessageCredential);
    }

    [Fact]
    public async Task Every_call_form_compiles_and_resolves() {
        // Guards the overload set against ambiguity and nullability warnings (warnings are errors
        // here): a constant, sync and async lambdas, method groups returning string and
        // Task<string> — the usual shape of a token service — and a Task<string?> provider, which
        // goes through an async lambda.
        HARRRConnectionOptions constant = new HARRRConnectionOptionsBuilder().WithCredential("constant");
        HARRRConnectionOptions syncLambda = new HARRRConnectionOptionsBuilder().WithCredential(() => "sync");
        HARRRConnectionOptions syncGroup = new HARRRConnectionOptionsBuilder().WithMessageCredential(Token);
        HARRRConnectionOptions asyncGroup = new HARRRConnectionOptionsBuilder().WithMessageCredential(TokenAsync);
        HARRRConnectionOptions taskLambda = new HARRRConnectionOptionsBuilder().WithConnectionCredential(() => TokenAsync());
        HARRRConnectionOptions nullable = new HARRRConnectionOptionsBuilder().WithCredential(async () => await NullableTokenAsync() ?? "");

        Assert.Equal("constant", await constant.MessageCredential!());
        Assert.Equal("sync", await syncLambda.MessageCredential!());
        Assert.Equal("sync-token", await syncGroup.MessageCredential!());
        Assert.Equal("async-token", await asyncGroup.MessageCredential!());
        Assert.Equal("async-token", await taskLambda.ConnectionCredential!());
        Assert.Equal("async-token", await nullable.ConnectionCredential!());
    }

#pragma warning disable CS0618 // the deprecated option is exercised on purpose
    [Fact]
    public async Task The_former_WithAuthorization_still_sets_the_message_credential_alone() {
        HARRRConnectionOptions options = new HARRRConnectionOptionsBuilder().WithAuthorization("legacy");
        Assert.NotNull(options.Authorization);
        Assert.Equal("legacy", await options.Authorization!());
        Assert.Null(options.ConnectionCredential);
        Assert.NotNull(HARRRConnection.Create(builder => builder.WithUrl(Url), o => o.WithAuthorization("legacy")));
    }

    [Fact]
    public void Former_and_new_message_credential_together_is_an_error() {
        Assert.Throws<InvalidOperationException>(() => HARRRConnection.Create(
            builder => builder.WithUrl(Url),
            options => options.WithAuthorization("legacy").WithMessageCredential("new")));
    }
#pragma warning restore CS0618

    [Fact]
    public void Connection_credential_on_a_built_HubConnection_is_an_error() {
        var hubConnection = new HubConnectionBuilder().WithUrl(Url).Build();
        Assert.Throws<ArgumentException>(() => HARRRConnection.Create(hubConnection, options => options.WithCredential("both")));
    }

    [Fact]
    public void Message_credential_on_a_built_HubConnection_is_fine() {
        var hubConnection = new HubConnectionBuilder().WithUrl(Url).Build();
        Assert.NotNull(HARRRConnection.Create(hubConnection, options => options.WithMessageCredential("message")));
    }

    [Fact]
    public void Connection_credential_and_SignalRs_AccessTokenProvider_together_is_an_error() {
        Assert.Throws<InvalidOperationException>(() => HARRRConnection.Create(
            builder => builder.WithUrl(Url, http => http.AccessTokenProvider = () => Task.FromResult<string?>("signalr")),
            options => options.WithConnectionCredential("signalarrr")));
    }
}
