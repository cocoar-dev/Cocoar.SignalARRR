using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Channels;
using Cocoar.SignalARRR.Common;
using Cocoar.SignalARRR.ProxyGenerator;
using Cocoar.SignalARRR.Tests.SharedModels;
using TestShared;
using Xunit;

namespace Cocoar.SignalARRR.Tests;

/// <summary>
/// The dispatchers the source generator emits for receiving server calls on a client. They replace
/// reflection-based dispatch, so they have to keep its rules: defaults, overloads by argument
/// count, inherited members, hiding, [MessageName] and the error codes.
/// </summary>
public class SourceGeneratorDispatcherTests {

    private sealed class Probe : IDispatchProbe {
        public CancellationToken LastToken { get; private set; }
        public string Inherited(int value) => $"inherited:{value}";
        public string Shadowed(int value) => $"base-shadowed:{value}";
        public string WithDefaults(string first, int second = 7, string? third = "x") => $"{first}|{second}|{third ?? "null"}";
        public string Overload(int a) => $"one:{a}";
        public string Overload(int a, int b, int c) => $"three:{a + b + c}";
        public string Shadowed() => "shadowed";
        public string Original() => "original";
        public Task<string> WaitFor(string label, CancellationToken cancellationToken) {
            LastToken = cancellationToken;
            return Task.FromResult(label);
        }
        public Task Nothing() => Task.CompletedTask;
        public ChannelReader<int> Channel(int count) {
            var channel = System.Threading.Channels.Channel.CreateUnbounded<int>();
            for (var i = 0; i < count; i++) channel.Writer.TryWrite(i);
            channel.Writer.TryComplete();
            return channel.Reader;
        }
        public IObservable<int> Observe(int count) => System.Reactive.Linq.Observable.Range(0, count);
    }

    private sealed class FakeContext : CallContext {
        public CancellationToken Token { get; } = new CancellationTokenSource().Token;
        public override CancellationToken CallCancellation => Token;
        public override CancellationToken ResolveCancellationToken(ArgumentReader reader, int index) => Token;
        public override Task<Stream?> ResolveStreamAsync(ArgumentReader reader, int index) => Task.FromResult<Stream?>(null);
    }

    private sealed class ValueReader : ArgumentReader {
        public ValueReader(params object?[] values) : base(values, new FakeContext()) { }
        public override T Read<T>(int index) => (T)GetRaw(index)!;
    }

    private static ContractDispatcher<IDispatchProbe> Dispatcher {
        get {
            // Touching the contract's assembly runs its module initializer, which registers the dispatcher.
            System.Runtime.CompilerServices.RuntimeHelpers.RunModuleConstructor(typeof(IDispatchProbe).Module.ModuleHandle);
            return DispatcherRegistry.Find<IDispatchProbe>()!;
        }
    }

    private static Task<object?> Call(string method, params object?[] args) => Dispatcher.InvokeAsync(new Probe(), method, new ValueReader(args));

    [Fact]
    public void Dispatcher_IsRegistered_WithTheContractsWireName() {
        Assert.NotNull(Dispatcher);
        Assert.Equal(typeof(IDispatchProbe).FullName, Dispatcher.WireName);
        Assert.Null(Dispatcher.UnsupportedReason);
    }

    [Fact]
    public async Task MissingTrailingArguments_TakeTheDeclaredDefaults() {
        Assert.Equal("a|7|x", await Call("WithDefaults", "a"));
        Assert.Equal("a|2|x", await Call("WithDefaults", "a", 2));
        Assert.Equal("a|2|null", await Call("WithDefaults", "a", 2, null));
    }

    [Fact]
    public async Task Overloads_ResolveByArgumentCount() {
        Assert.Equal("one:1", await Call("Overload", 1));
        Assert.Equal("three:6", await Call("Overload", 1, 2, 3));

        var error = await Assert.ThrowsAsync<DispatchException>(() => Call("Overload", 1, 2));
        Assert.Equal(HARRRErrorCodes.InvalidArgumentCount, error.Code);
        Assert.Contains("1, 3", error.Message);
    }

    [Fact]
    public async Task InheritedMembers_AreReceivable() {
        Assert.Equal("inherited:4", await Call("Inherited", 4));
    }

    [Fact]
    public async Task AMemberOfTheContract_HidesInheritedMembersOfTheSameName() {
        Assert.Equal("shadowed", await Call("Shadowed"));
        var error = await Assert.ThrowsAsync<DispatchException>(() => Call("Shadowed", 1));
        Assert.Equal(HARRRErrorCodes.InvalidArgumentCount, error.Code);
    }

    [Fact]
    public async Task MessageName_IsTheOnlyNameAMemberAnswersTo() {
        Assert.Equal("original", await Call("Renamed"));
        var error = await Assert.ThrowsAsync<DispatchException>(() => Call("Original"));
        Assert.Equal(HARRRErrorCodes.MethodNotFound, error.Code);
    }

    [Fact]
    public async Task TokenSlots_AreFilledByTheCallContext() {
        var probe = new Probe();
        var reader = new ValueReader("label", null);

        Assert.Equal("label", await Dispatcher.InvokeAsync(probe, "WaitFor", reader));
        Assert.Equal(((FakeContext)reader.Context).Token, probe.LastToken);
    }

    [Fact]
    public async Task TaskMethods_ReturnNull() {
        Assert.Null(await Call("Nothing"));
    }

    [Theory]
    [InlineData("Channel")]
    [InlineData("Observe")]
    public async Task StreamingResults_ComeBackAsStreamingResult(string method) {
        var result = Assert.IsAssignableFrom<IStreamingResult>(await Call(method, 3));
        var items = new List<object?>();
        await result.ForEachAsync(item => { items.Add(item); return Task.CompletedTask; }, CancellationToken.None);
        Assert.Equal(new object?[] { 0, 1, 2 }, items);
    }

    [Fact]
    public void WireTypes_ListArgumentAndResultTypes_ButNotTokensOrStreams() {
        Assert.Contains(typeof(string), Dispatcher.WireTypes);
        Assert.Contains(typeof(int), Dispatcher.WireTypes);
        Assert.DoesNotContain(typeof(CancellationToken), Dispatcher.WireTypes);
    }

    [Fact]
    public void AmbiguousOverloads_MakeTheContractUnreceivable() {
        System.Runtime.CompilerServices.RuntimeHelpers.RunModuleConstructor(typeof(IAmbiguousDispatchProbe).Module.ModuleHandle);
        var reason = DispatcherRegistry.Find<IAmbiguousDispatchProbe>()!.UnsupportedReason;
        Assert.NotNull(reason);
        Assert.Contains("'Same'", reason);
    }

    [Fact]
    public void GenericMethods_MakeTheContractUnreceivable() {
        System.Runtime.CompilerServices.RuntimeHelpers.RunModuleConstructor(typeof(IGeneric).Module.ModuleHandle);
        var reason = DispatcherRegistry.Find<IGeneric>()!.UnsupportedReason;
        Assert.NotNull(reason);
        Assert.Contains("Invoke<T>", reason);
    }

    [Fact]
    public void DispatchErrorCodes_MatchTheWireErrorCodes() {
        Assert.Equal(HARRRErrorCodes.MethodNotFound, DispatchException.MethodNotFound);
        Assert.Equal(HARRRErrorCodes.InvalidArgumentCount, DispatchException.InvalidArgumentCount);
    }
}
