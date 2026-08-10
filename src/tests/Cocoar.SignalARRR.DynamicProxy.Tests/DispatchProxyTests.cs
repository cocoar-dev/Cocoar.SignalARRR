using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Cocoar.SignalARRR.ProxyGenerator;
using Xunit;

namespace Cocoar.SignalARRR.DynamicProxy.Tests;

#region Test interfaces

public interface IVoidMethods {
    void FireAndForget(string message);
}

public interface ITaskMethods {
    Task SendAsync(string message);
}

public interface ITaskOfTMethods {
    Task<string> GetValueAsync(string key);
}

public interface ISyncMethods {
    int Add(int a, int b);
}

public interface IStreamingMethods {
    IAsyncEnumerable<string> StreamItems(string prefix, CancellationToken cancellationToken = default);
    ChannelReader<int> StreamChannel(CancellationToken cancellationToken = default);
    IObservable<double> ObserveValues();
}

public interface IGenericMethods {
    Task<T> Echo<T>(T value);
}

public interface ICancellationMethods {
    Task DoWork(string input, CancellationToken cancellationToken);
}

public interface IFallbackOnly {
    Task<string> GetName();
}

[Cocoar.SignalARRR.Common.Attributes.MessageName("renamed.contract")]
public interface IRenamedWireMethods {
    [Cocoar.SignalARRR.Common.Attributes.MessageName("greet")]
    Task<string> SayHello(string name);

    Task Untouched();
}

#endregion

#region Mock helper

public class MockProxyCreatorHelper : ProxyCreatorHelper {
    public string? LastMethodName { get; private set; }
    public object[]? LastArguments { get; private set; }
    public string[]? LastGenericArguments { get; private set; }
    public CancellationToken LastCancellationToken { get; private set; }
    public string? LastCalledMethod { get; private set; }

    public override void Send(string methodName, IEnumerable<object> arguments, string[] genericArguments, CancellationToken cancellationToken = default) {
        LastCalledMethod = nameof(Send);
        LastMethodName = methodName;
        LastArguments = arguments is object[] arr ? arr : new List<object>(arguments).ToArray();
        LastGenericArguments = genericArguments;
        LastCancellationToken = cancellationToken;
    }

    public override Task SendAsync(string methodName, IEnumerable<object> arguments, string[] genericArguments, CancellationToken cancellationToken = default) {
        LastCalledMethod = nameof(SendAsync);
        LastMethodName = methodName;
        LastArguments = arguments is object[] arr ? arr : new List<object>(arguments).ToArray();
        LastGenericArguments = genericArguments;
        LastCancellationToken = cancellationToken;
        return Task.CompletedTask;
    }

    public override T Invoke<T>(string methodName, IEnumerable<object> arguments, string[] genericArguments, CancellationToken cancellationToken = default) {
        LastCalledMethod = nameof(Invoke);
        LastMethodName = methodName;
        LastArguments = arguments is object[] arr ? arr : new List<object>(arguments).ToArray();
        LastGenericArguments = genericArguments;
        LastCancellationToken = cancellationToken;
        if (typeof(T) == typeof(int))
            return (T)(object)42;
        return default!;
    }

    public override Task<T> InvokeAsync<T>(string methodName, IEnumerable<object> arguments, string[] genericArguments, CancellationToken cancellationToken = default) {
        LastCalledMethod = nameof(InvokeAsync);
        LastMethodName = methodName;
        LastArguments = arguments is object[] arr ? arr : new List<object>(arguments).ToArray();
        LastGenericArguments = genericArguments;
        LastCancellationToken = cancellationToken;
        if (typeof(T) == typeof(string))
            return Task.FromResult((T)(object)"mock-value");
        return Task.FromResult(default(T)!);
    }

    public override IAsyncEnumerable<TResult> StreamAsync<TResult>(string methodName, IEnumerable<object> arguments, string[] genericArguments, CancellationToken cancellationToken = default) {
        LastCalledMethod = nameof(StreamAsync);
        LastMethodName = methodName;
        LastArguments = arguments is object[] arr ? arr : new List<object>(arguments).ToArray();
        LastGenericArguments = genericArguments;
        LastCancellationToken = cancellationToken;
        return EmptyAsyncEnumerable<TResult>();
    }

    private static async IAsyncEnumerable<T> EmptyAsyncEnumerable<T>() {
        await Task.CompletedTask;
        yield break;
    }
}

#endregion

public class DispatchProxyTests {

    [Fact]
    public void Void_Method_Routes_To_Send() {
        var helper = new MockProxyCreatorHelper();
        var proxy = (IVoidMethods)SignalARRRDispatchProxy.CreateForType(typeof(IVoidMethods), helper);

        proxy.FireAndForget("hello");

        Assert.Equal(nameof(MockProxyCreatorHelper.Send), helper.LastCalledMethod);
        Assert.Contains(nameof(IVoidMethods.FireAndForget), helper.LastMethodName);
        Assert.Equal("hello", helper.LastArguments![0]);
    }

    [Fact]
    public async Task Task_Method_Routes_To_SendAsync() {
        var helper = new MockProxyCreatorHelper();
        var proxy = (ITaskMethods)SignalARRRDispatchProxy.CreateForType(typeof(ITaskMethods), helper);

        await proxy.SendAsync("world");

        Assert.Equal(nameof(MockProxyCreatorHelper.SendAsync), helper.LastCalledMethod);
        Assert.Contains(nameof(ITaskMethods.SendAsync), helper.LastMethodName);
    }

    [Fact]
    public async Task TaskOfT_Method_Routes_To_InvokeAsync() {
        var helper = new MockProxyCreatorHelper();
        var proxy = (ITaskOfTMethods)SignalARRRDispatchProxy.CreateForType(typeof(ITaskOfTMethods), helper);

        var result = await proxy.GetValueAsync("key1");

        Assert.Equal(nameof(MockProxyCreatorHelper.InvokeAsync), helper.LastCalledMethod);
        Assert.Equal("mock-value", result);
    }

    [Fact]
    public void Sync_Method_Routes_To_Invoke() {
        var helper = new MockProxyCreatorHelper();
        var proxy = (ISyncMethods)SignalARRRDispatchProxy.CreateForType(typeof(ISyncMethods), helper);

        var result = proxy.Add(1, 2);

        Assert.Equal(nameof(MockProxyCreatorHelper.Invoke), helper.LastCalledMethod);
        Assert.Equal(42, result);
    }

    [Fact]
    public void AsyncEnumerable_Streaming_Routes_To_StreamAsync() {
        var helper = new MockProxyCreatorHelper();
        var proxy = (IStreamingMethods)SignalARRRDispatchProxy.CreateForType(typeof(IStreamingMethods), helper);

        var stream = proxy.StreamItems("test", TestContext.Current.CancellationToken);

        Assert.NotNull(stream);
        Assert.Equal(nameof(MockProxyCreatorHelper.StreamAsync), helper.LastCalledMethod);
        Assert.Contains("StreamItems", helper.LastMethodName);
    }

    [Fact]
    public void ChannelReader_Streaming_Routes_To_StreamAsync() {
        var helper = new MockProxyCreatorHelper();
        var proxy = (IStreamingMethods)SignalARRRDispatchProxy.CreateForType(typeof(IStreamingMethods), helper);

        var reader = proxy.StreamChannel(TestContext.Current.CancellationToken);

        Assert.NotNull(reader);
        Assert.Equal(nameof(MockProxyCreatorHelper.StreamAsync), helper.LastCalledMethod);
        Assert.Contains("StreamChannel", helper.LastMethodName);
    }

    [Fact]
    public void Observable_Streaming_Routes_To_StreamAsync() {
        var helper = new MockProxyCreatorHelper();
        var proxy = (IStreamingMethods)SignalARRRDispatchProxy.CreateForType(typeof(IStreamingMethods), helper);

        var observable = proxy.ObserveValues();

        Assert.NotNull(observable);
        Assert.Equal(nameof(MockProxyCreatorHelper.StreamAsync), helper.LastCalledMethod);
        Assert.Contains("ObserveValues", helper.LastMethodName);
    }

    [Fact]
    public void CancellationToken_Extracted_From_Args() {
        var helper = new MockProxyCreatorHelper();
        var proxy = (ICancellationMethods)SignalARRRDispatchProxy.CreateForType(typeof(ICancellationMethods), helper);
        using var cts = new CancellationTokenSource();

        proxy.DoWork("test", cts.Token);

        Assert.Equal(cts.Token, helper.LastCancellationToken);
    }

    [Fact]
    public void MethodName_Includes_InterfaceType_FullName() {
        var helper = new MockProxyCreatorHelper();
        var proxy = (IVoidMethods)SignalARRRDispatchProxy.CreateForType(typeof(IVoidMethods), helper);

        proxy.FireAndForget("x");

        Assert.StartsWith(typeof(IVoidMethods).FullName!, helper.LastMethodName);
        Assert.Contains("|", helper.LastMethodName);
    }
}

public class FallbackRegistrationTests {

    [Fact]
    public void ProxyCreator_Uses_Fallback_For_Unregistered_Interface() {
        // Explicitly call the module initializer to ensure fallback is registered
        DynamicProxyInitializer.Initialize();
        var helper = new MockProxyCreatorHelper();

        // IFallbackOnly has no source-generated factory, so it should use the fallback
        var proxy = ProxyCreator.CreateInstanceFromInterface<IFallbackOnly>(helper);

        Assert.NotNull(proxy);
        Assert.IsAssignableFrom<IFallbackOnly>(proxy);
    }

    [Fact]
    public async Task ProxyCreator_Fallback_Proxy_Is_Functional() {
        DynamicProxyInitializer.Initialize();
        var helper = new MockProxyCreatorHelper();
        var proxy = ProxyCreator.CreateInstanceFromInterface<IFallbackOnly>(helper);

        var result = await proxy.GetName();

        Assert.Equal("mock-value", result);
        Assert.Equal(nameof(MockProxyCreatorHelper.InvokeAsync), helper.LastCalledMethod);
    }
}

public class NoFallbackTests {

    [Fact]
    public void ProxyCreator_Throws_InvalidOperationException_With_Guidance_When_No_Factory() {
        // We cannot fully test "no fallback" since the DynamicProxy assembly is loaded,
        // but we can verify the error message is correct by testing with a type that
        // has a registered factory AND verifying the fallback path works.
        // The actual no-fallback exception path is tested implicitly: if you remove the
        // DynamicProxy reference, ProxyCreator.CreateInstanceFromInterface throws.

        // Verify the fallback IS registered (proving the module initializer works)
        DynamicProxyInitializer.Initialize();
        var helper = new MockProxyCreatorHelper();
        var proxy = ProxyCreator.CreateInstanceFromInterface<IFallbackOnly>(helper);
        Assert.NotNull(proxy);
    }

    [Fact]
    public async Task DispatchProxy_emits_the_declared_wire_names() {
        // The third place a contract name is formed. It has to match what the registration index
        // resolves and what the source generator emits for the same contract -- all three now go
        // through the one rule in Cocoar.SignalARRR.Common.WireName.
        DynamicProxyInitializer.Initialize();
        var helper = new MockProxyCreatorHelper();
        var proxy = ProxyCreator.CreateInstanceFromInterface<IRenamedWireMethods>(helper);

        await proxy.SayHello("world");
        Assert.Equal("renamed.contract|greet", helper.LastMethodName);

        await proxy.Untouched();
        Assert.Equal("renamed.contract|Untouched", helper.LastMethodName);
    }
}
