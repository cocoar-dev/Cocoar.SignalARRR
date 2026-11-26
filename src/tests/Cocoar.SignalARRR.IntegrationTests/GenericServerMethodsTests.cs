using System.Threading.Tasks;
using Cocoar.SignalARRR.Client;
using Cocoar.SignalARRR.Common;
using Cocoar.SignalARRR.Tests.SharedModels;
using Microsoft.AspNetCore.SignalR.Client;
using TestShared;
using Xunit;

namespace Cocoar.SignalARRR.IntegrationTests {
    /// <summary>
    /// Generic client-to-server methods: the client names the type arguments, the server accepts
    /// the ones it declared with [GenericArguments].
    /// </summary>
    [Collection("Simple")]
    public class GenericServerMethodsTests : IAsyncLifetime {
        private readonly SignalARRRServerInstanceFixture _fixture;
        private HARRRConnection _connection = null!;

        public GenericServerMethodsTests(SignalARRRServerInstanceFixture fixture) {
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
        public async Task Proxy_SendsTheTypeArgument_AndTheServerCallsThatInstantiation() {
            var proxy = _connection.GetTypedMethods<IGenericServerMethods>();

            Assert.Equal(42, await Task.Run(() => proxy.Echo(42), TestContext.Current.CancellationToken));
            Assert.Equal("text", await Task.Run(() => proxy.Echo("text"), TestContext.Current.CancellationToken));

            var complex = await Task.Run(() => proxy.Echo(new ComplexTestClass { Name = "c" }), TestContext.Current.CancellationToken);
            Assert.Equal("c", complex.Name);
        }

        [Fact]
        public async Task SeveralTypeParameters_ResolveInOrder() {
            var proxy = _connection.GetTypedMethods<IGenericServerMethods>();

            Assert.Equal("Int32:1|String:a", await Task.Run(() => proxy.Pair(1, "a"), TestContext.Current.CancellationToken));
            Assert.Equal("String:a|Int32:1", await Task.Run(() => proxy.Pair("a", 1), TestContext.Current.CancellationToken));
        }

        [Fact]
        public async Task AnUndeclaredTypeArgument_IsRefused() {
            // Guid is a perfectly loadable type, but the method did not declare it.
            var message = new ClientRequestMessage("GenericMethods.Echo", new object[] { System.Guid.NewGuid() }) {
                GenericArguments = new[] { typeof(System.Guid).FullName! },
            };

            var error = await Assert.ThrowsAsync<Cocoar.SignalARRR.Common.Exceptions.HARRRRemoteException>(
                () => _connection.InvokeCoreAsync<System.Guid>(message, TestContext.Current.CancellationToken));
            Assert.Contains("[GenericArguments]", error.Message);
        }

        [Fact]
        public async Task ShortSystemTypeName_IsAccepted() {
            // What a Kotlin contract may send: @GenericArguments("Int32") rather than the FullName.
            var message = new ClientRequestMessage("GenericMethods.Echo", new object[] { 7 }) {
                GenericArguments = new[] { "int32" },
            };

            Assert.Equal(7, await _connection.InvokeCoreAsync<int>(message, TestContext.Current.CancellationToken));
        }
    }
}