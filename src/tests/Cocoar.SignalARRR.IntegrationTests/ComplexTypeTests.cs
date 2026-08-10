using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Cocoar.SignalARRR.Client;
using Cocoar.SignalARRR.Common;
using Microsoft.AspNetCore.SignalR.Client;
using Xunit;

namespace Cocoar.SignalARRR.IntegrationTests {
    [Collection("Simple")]
    public class ComplexTypeTests : IAsyncLifetime {
        private readonly SignalARRRServerInstanceFixture _fixture;
        private HARRRConnection _connection = null!;

        public ComplexTypeTests(SignalARRRServerInstanceFixture fixture) {
            _fixture = fixture;
        }

        public async ValueTask InitializeAsync() {
            _connection = HARRRConnection.Create(builder =>
                builder.WithUrl($"{_fixture.ServerUrl}/signalr/testhub"));
            await _connection.StartAsync(TestContext.Current.CancellationToken);
        }

        public async ValueTask DisposeAsync() {
            await _connection.StopAsync();
            await _connection.DisposeAsync();
        }

        [Fact]
        public async Task DateTime_SerializesCorrectly() {
            var ct = TestContext.Current.CancellationToken;
            var date = new DateTime(2025, 6, 15, 0, 0, 0, DateTimeKind.Utc);
            var result = await _connection.InvokeCoreAsync<string>(
                new ClientRequestMessage("ExtraMethods.FormatDate", new object[] { date }), ct);

            Assert.Equal("2025-06-15", result);
        }

        [Fact]
        public async Task Guid_ParameterPassesCorrectly() {
            var ct = TestContext.Current.CancellationToken;
            var guid = Guid.NewGuid();
            var result = await _connection.InvokeCoreAsync<string>(
                new ClientRequestMessage("ExtraMethods.GuidToString", new object[] { guid }), ct);

            Assert.Equal(guid.ToString(), result);
        }

        [Fact]
        public async Task List_ReturnedCorrectly() {
            var ct = TestContext.Current.CancellationToken;
            var result = await _connection.InvokeCoreAsync<List<string>>(
                new ClientRequestMessage("ExtraMethods.GenerateItems", new object[] { 4 }), ct);

            Assert.NotNull(result);
            Assert.Equal(4, result!.Count);
            Assert.Equal("item-0", result[0]);
            Assert.Equal("item-3", result[3]);
        }

        [Fact]
        public async Task Dictionary_ReturnedCorrectly() {
            var ct = TestContext.Current.CancellationToken;
            var result = await _connection.InvokeCoreAsync<Dictionary<string, int>>(
                new ClientRequestMessage("ExtraMethods.WordLengths", new object[] { "hello world" }), ct);

            Assert.NotNull(result);
            Assert.Equal(5, result!["hello"]);
            Assert.Equal(5, result["world"]);
        }

        [Fact]
        public async Task MultipleParameterTypes_WorkTogether() {
            var ct = TestContext.Current.CancellationToken;
            var result = await _connection.InvokeCoreAsync<string>(
                new ClientRequestMessage("ExtraMethods.Combine", new object[] { "test", 42, true }), ct);

            Assert.Equal("test-42-True", result);
        }

        [Fact]
        public async Task ServerCallsClient_GetByGenericId_GuidRoundTrips() {
            var ct = TestContext.Current.CancellationToken;
            _connection.RegisterInterface<TestShared.ITestClientMethods, TestClientMethodsImpl>(new TestClientMethodsImpl());

            var guid = Guid.NewGuid();
            using var http = new System.Net.Http.HttpClient();
            var url = $"{_fixture.ServerUrl}/__test/trigger-client-getbygenericid?connectionId={_connection.ConnectionId}&id={guid}";
            var response = await http.PostAsync(url, null, ct);
            var result = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode) {
                Assert.Fail($"Server returned {response.StatusCode}: {result}");
            }

            Assert.Contains(guid.ToString(), result);
        }

        [Fact]
        public async Task ClientSendsStreamArgument_AutomaticUpload() {
            var ct = TestContext.Current.CancellationToken;

            var stream = new System.IO.MemoryStream();
            var writer = new System.IO.StreamWriter(stream);
            writer.Write("AutoUploadContent");
            writer.Flush();
            stream.Position = 0;

            var result = await _connection.InvokeCoreAsync<string>(
                new ClientRequestMessage("ExtraMethods.ReadStreamContent", new object[] { stream }), ct);

            Assert.Equal("AutoUploadContent", result);
        }

        /// <summary>
        /// The same upload has to happen on the invoke paths that are not the generic one.
        /// </summary>
        /// <remarks>
        /// Only <c>InvokeCoreAsync&lt;TResult&gt;</c> and <c>SendCoreAsync</c> prepared their Stream
        /// arguments. The void overload, the Type-based one and the three streaming ones handed the
        /// live Stream straight to the hub protocol serializer instead — MessagePack threw, JSON
        /// emitted a <c>{"CanRead":true,…}</c> object the server then bound as garbage. So the very
        /// same call worked or failed depending on whether the caller wanted the result back.
        /// </remarks>
        [Fact]
        public async Task ClientSendsStreamArgument_OnTheTypeBasedInvokePath() {
            var ct = TestContext.Current.CancellationToken;

            var stream = new System.IO.MemoryStream();
            var writer = new System.IO.StreamWriter(stream);
            writer.Write("TypeBasedUploadContent");
            writer.Flush();
            stream.Position = 0;

            var result = await _connection.InvokeCoreAsync(
                new ClientRequestMessage("ExtraMethods.ReadStreamContent", new object[] { stream }),
                typeof(string), ct);

            Assert.Equal("TypeBasedUploadContent", result);
        }

        [Fact]
        public async Task ClientSendsStreamArgument_OnTheVoidInvokePath() {
            var ct = TestContext.Current.CancellationToken;

            var stream = new System.IO.MemoryStream();
            var writer = new System.IO.StreamWriter(stream);
            writer.Write("VoidUploadContent");
            writer.Flush();
            stream.Position = 0;

            // Nothing comes back on this path, so the assertion is that the call completes at all:
            // an unprepared Stream fails while the message is being written, or lands on the server
            // as an object its Stream parameter cannot bind.
            await _connection.InvokeCoreAsync(
                new ClientRequestMessage("ExtraMethods.ReadStreamContent", new object[] { stream }), ct);
        }

        [Fact]
        public async Task RequestUploadSlot_ReturnsValidUrl() {
            var ct = TestContext.Current.CancellationToken;

            // Call RequestUploadSlot directly via SignalR (not through SignalARRR protocol)
            var hubConnection = _connection.AsSignalRHubConnection();
            var uploadUrl = await hubConnection.InvokeCoreAsync<string>("RequestUploadSlot", System.Array.Empty<object>(), ct);

            Assert.NotNull(uploadUrl);
            Assert.Contains("/upload/", uploadUrl);
        }

        [Fact]
        public async Task RequestUploadSlot_ThenUpload_ThenServerReads() {
            var ct = TestContext.Current.CancellationToken;

            // Step 1: Get upload URL
            var hubConnection = _connection.AsSignalRHubConnection();
            var uploadUrl = await hubConnection.InvokeCoreAsync<string>("RequestUploadSlot", System.Array.Empty<object>(), ct);

            // Step 2: Upload data via HTTP
            using var httpClient = new System.Net.Http.HttpClient();
            var content = new System.Net.Http.StringContent("TestUploadContent");
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
            var uploadResponse = await httpClient.PostAsync(uploadUrl, content, ct);
            uploadResponse.EnsureSuccessStatusCode();

            // Step 3: Call server method with StreamReference as argument
            var streamRef = new Cocoar.SignalARRR.Common.RemoteReferenceTypes.StreamReference { Uri = uploadUrl };
            var result = await _connection.InvokeCoreAsync<string>(
                new ClientRequestMessage("ExtraMethods.ReadStreamContent", new object[] { streamRef }), ct);

            Assert.Equal("TestUploadContent", result);
        }
    }
}
