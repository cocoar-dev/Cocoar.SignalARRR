using System.Net.Http;
using System.Threading.Tasks;
using Xunit;

namespace Cocoar.SignalARRR.IntegrationTests {

    /// <summary>
    /// The health check over the real endpoint (O-8): a single-node server without a backplane
    /// reports Healthy.
    /// </summary>
    [Collection("Simple")]
    public class HealthEndpointTests {
        private readonly SignalARRRServerInstanceFixture _fixture;

        public HealthEndpointTests(SignalARRRServerInstanceFixture fixture) {
            _fixture = fixture;
        }

        [Fact]
        public async Task The_health_endpoint_reports_healthy_on_a_single_node() {
            var ct = TestContext.Current.CancellationToken;
            using var http = new HttpClient();

            var response = await http.GetAsync($"{_fixture.ServerUrl}/health", ct);
            var body = await response.Content.ReadAsStringAsync(ct);

            Assert.True(response.IsSuccessStatusCode, $"health endpoint returned {response.StatusCode}: {body}");
            Assert.Equal("Healthy", body);
        }
    }
}
