using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Cocoar.SignalARRR.Common;
using Cocoar.SignalARRR.ProxyGenerator;
using Cocoar.SignalARRR.Tests.SharedModels;
using Xunit;

namespace Cocoar.SignalARRR.Tests {

    /// <summary>
    /// A contract name is formed in four independent places: the source generator, the two
    /// reflection <c>DispatchProxy</c> flavours, and the registration that builds the receiving
    /// index. Three of them emit, one of them resolves. They agree or calls disappear — the
    /// receiving side answers "method not found", or worse, silently drops a fire-and-forget.
    /// These tests hold the emitting side against the resolving side for the same contract.
    /// </summary>
    public class WireNameAgreementTests {

        private sealed class CapturingHelper : ProxyCreatorHelper {
            public string? LastMethodName { get; private set; }

            public override void Send(string methodName, IEnumerable<object> arguments, string[] genericArguments, CancellationToken cancellationToken = default) {
                LastMethodName = methodName;
            }

            public override Task SendAsync(string methodName, IEnumerable<object> arguments, string[] genericArguments, CancellationToken cancellationToken = default) {
                LastMethodName = methodName;
                return Task.CompletedTask;
            }

            public override TResult Invoke<TResult>(string methodName, IEnumerable<object> arguments, string[] genericArguments, CancellationToken cancellationToken = default) {
                LastMethodName = methodName;
                return default!;
            }

            public override Task<TResult> InvokeAsync<TResult>(string methodName, IEnumerable<object> arguments, string[] genericArguments, CancellationToken cancellationToken = default) {
                LastMethodName = methodName;
                return Task.FromResult<TResult>(default!);
            }

            public override IAsyncEnumerable<TResult> StreamAsync<TResult>(string methodName, IEnumerable<object> arguments, string[] genericArguments, CancellationToken cancellationToken = default) {
                LastMethodName = methodName;
                return Empty<TResult>();
            }

#pragma warning disable CS1998 // no await: an empty sequence has nothing to wait for
            private static async IAsyncEnumerable<T> Empty<T>() {
                yield break;
            }
#pragma warning restore CS1998
        }

        private sealed class RenamedWireContract : IRenamedWireContract {
            public Task<string> SayHello(string name) => Task.FromResult(name);
            public Task Untouched() => Task.CompletedTask;
        }

        [Fact]
        public async Task The_generated_proxy_emits_the_declared_names() {
            var helper = new CapturingHelper();
            var proxy = ProxyCreator.CreateInstanceFromInterface<IRenamedWireContract>(helper);

            await proxy.SayHello("world");

            Assert.Equal("renamed.contract|greet", helper.LastMethodName);
        }

        [Fact]
        public async Task An_undeclared_member_keeps_its_csharp_name_under_the_declared_interface() {
            var helper = new CapturingHelper();
            var proxy = ProxyCreator.CreateInstanceFromInterface<IRenamedWireContract>(helper);

            await proxy.Untouched();

            Assert.Equal("renamed.contract|Untouched", helper.LastMethodName);
        }

        [Fact]
        public async Task Registration_resolves_exactly_what_the_generated_proxy_emits() {
            // The one that matters. Both halves are derived independently — the emitting half by the
            // generator from Roslyn symbols at compile time, the resolving half by WireName from
            // reflection at startup — so this is the only check that they met in the middle.
            var collection = new SignalARRRInterfaceCollection();
            collection.RegisterInterface(typeof(IRenamedWireContract), typeof(RenamedWireContract));

            var helper = new CapturingHelper();
            var proxy = ProxyCreator.CreateInstanceFromInterface<IRenamedWireContract>(helper);
            await proxy.SayHello("world");

            var (_, methodInfo) = collection.GetInvokeInformation(helper.LastMethodName!, 1);
            Assert.Equal(nameof(IRenamedWireContract.SayHello), methodInfo.Name);
        }

        [Fact]
        public void The_reflection_helper_agrees_with_the_generator() {
            // WireName is what the two DispatchProxy flavours and the registration use; the generator
            // re-implements the rule against symbols. Compare the two derivations directly.
            Assert.Equal(
                "renamed.contract|greet",
                WireName.For(typeof(IRenamedWireContract), typeof(IRenamedWireContract).GetMethod(nameof(IRenamedWireContract.SayHello))!));

            Assert.Equal(
                "renamed.contract|Untouched",
                WireName.For(typeof(IRenamedWireContract), typeof(IRenamedWireContract).GetMethod(nameof(IRenamedWireContract.Untouched))!));
        }
    }
}
