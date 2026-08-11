using System;
using System.Net.Http;
using System.Threading.Tasks;
using Cocoar.SignalARRR.Client;
using Microsoft.AspNetCore.SignalR.Client;
using TestShared;
using Xunit;

namespace Cocoar.SignalARRR.IntegrationTests {
    /// <summary>
    /// Guards the predicates the rest of the suite waits on.
    /// </summary>
    /// <remarks>
    /// <c>/__test/client-exists</c> answered <c>true</c> for every id, including ones that were
    /// never registered: it treated "the lookup did not throw" as "the client is there", and the
    /// lookup returns <c>null</c> for a miss rather than throwing. So
    /// <see cref="TestHelper.WaitForClientRegistration"/> — which polls it — returned on its first
    /// attempt without waiting for anything, and every test using it was racing the registration,
    /// winning only because the window is small. It finally lost on the slowest CI leg.
    /// <para>
    /// A wait is only worth anything if its predicate can say no. Nothing asserted that, so nothing
    /// noticed. These tests assert both answers, which is the cheapest way to keep a wait honest.
    /// </para>
    /// </remarks>
    [Collection("Simple")]
    public class TestHarnessGuardTests {
        private readonly SignalARRRServerInstanceFixture _fixture;

        public TestHarnessGuardTests(SignalARRRServerInstanceFixture fixture) {
            _fixture = fixture;
        }

        private async Task<string> ClientExists(string connectionId) {
            using var http = new HttpClient();
            var response = await http.GetAsync(
                $"{_fixture.ServerUrl}/__test/client-exists?connectionId={Uri.EscapeDataString(connectionId)}",
                TestContext.Current.CancellationToken);

            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        }

        [Fact]
        public async Task The_registration_probe_can_answer_no() {
            // The half that was broken, and the half nothing checked.
            Assert.Contains("false", await ClientExists("not-a-real-connection-id"));
        }

        [Fact]
        public async Task The_registration_probe_can_answer_yes() {
            // The other half: a probe stuck at "false" would make every wait time out instead, which
            // is at least loud — but it would still be broken, so assert both directions.
            var connection = HARRRConnection.Create(builder => builder.WithUrl($"{_fixture.ServerUrl}/signalr/testhub"));
            await connection.StartAsync(TestContext.Current.CancellationToken);

            try {
                await TestHelper.WaitForClientRegistration(_fixture.ServerUrl, connection, TestContext.Current.CancellationToken);

                Assert.Contains("true", await ClientExists(connection.ConnectionId!));
            } finally {
                await connection.StopAsync(TestContext.Current.CancellationToken);
                await connection.DisposeAsync();
            }
        }
    }
}
