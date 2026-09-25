using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Xunit;

namespace Cocoar.SignalARRR.Client.FullFramework.Tests {
    /// <summary>
    /// The credential options of the .NET Framework client — the same as in every SignalARRR client —
    /// and where SignalR's .NET Framework client carries the connection credential.
    /// <c>TestHub.TransportCredential</c> reports what the transport request carried; the test server
    /// does not copy the query into the header.
    /// </summary>
    [Collection("FullFramework")]
    public class CredentialTests {
        private readonly ServerFixture _fixture;

        public CredentialTests(ServerFixture fixture) {
            _fixture = fixture;
        }

        [Theory]
        [InlineData(HttpTransportType.WebSockets)]
        [InlineData(HttpTransportType.ServerSentEvents)]
        [InlineData(HttpTransportType.LongPolling)]
        public async Task Credential_authenticates_the_connection_with_a_header(HttpTransportType transport) {
            var connection = HARRRConnection.Create(
                builder => builder.WithUrl(_fixture.ServerUrl + "/signalr/testhub", o => o.Transports = transport),
                options => options.WithCredential("probe-token"));
            await connection.StartAsync();
            try {
                Assert.Equal("header=Bearer probe-token;query=-", await connection.InvokeAsync<string>("TransportCredential"));
            } finally {
                await connection.StopAsync();
                await connection.DisposeAsync();
            }
        }

        [Fact]
        public async Task Message_credential_alone_leaves_the_connection_anonymous() {
            var connection = HARRRConnection.Create(
                builder => builder.WithUrl(_fixture.ServerUrl + "/signalr/testhub"),
                options => options.WithMessageCredential("message-token"));
            await connection.StartAsync();
            try {
                Assert.Equal("header=-;query=-", await connection.InvokeAsync<string>("TransportCredential"));
            } finally {
                await connection.StopAsync();
                await connection.DisposeAsync();
            }
        }

        [Fact]
        public void Connection_credential_and_SignalRs_AccessTokenProvider_together_is_an_error() {
            Assert.Throws<InvalidOperationException>(() => HARRRConnection.Create(
                builder => builder.WithUrl(_fixture.ServerUrl + "/signalr/testhub", o => o.AccessTokenProvider = () => Task.FromResult("signalr")),
                options => options.WithConnectionCredential("signalarrr")));
        }

        [Fact]
        public void Connection_credential_on_a_built_HubConnection_is_an_error() {
            var hubConnection = new HubConnectionBuilder().WithUrl(_fixture.ServerUrl + "/signalr/testhub").Build();
            Assert.Throws<ArgumentException>(() => HARRRConnection.Create(hubConnection, options => options.WithCredential("both")));
        }
    }
}
