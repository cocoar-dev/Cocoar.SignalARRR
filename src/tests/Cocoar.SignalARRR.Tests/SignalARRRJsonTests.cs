using System.Text.Json;
using System.Text.Json.Serialization;
using Cocoar.SignalARRR.Client;
using Cocoar.SignalARRR.Common;
using Cocoar.SignalARRR.Common.Serialization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Cocoar.SignalARRR.Tests;

/// <summary>
/// SignalARRR appends its own JSON context to the hub protocol's options. Appending to a chain
/// changes what the options can serialize, so these pin that the app's own resolvers keep
/// precedence and that a JIT app relying on reflection-based JSON keeps it.
/// </summary>
public partial class SignalARRRJsonTests {

    public sealed class AppPayload {
        public string Name { get; set; } = "";
        public int Count { get; set; }
    }

    [JsonSerializable(typeof(AppPayload))]
    private partial class AppJson : JsonSerializerContext { }

    private static JsonSerializerOptions PayloadOptionsOf(HARRRConnection connection) {
        // Create() wires the options of the connection's own service provider, not a global copy.
        var hubConnection = connection.AsSignalRHubConnection();
        var serviceProvider = (System.IServiceProvider)typeof(HubConnection)
            .GetField("_serviceProvider", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .GetValue(hubConnection)!;
        return serviceProvider.GetRequiredService<IOptions<JsonHubProtocolOptions>>().Value.PayloadSerializerOptions;
    }

    [Fact]
    public void Create_WithoutAppResolver_KeepsReflectionJsonForAppTypes() {
        var connection = HARRRConnection.Create(b => b.WithUrl("http://127.0.0.1:1/hub"));
        var options = PayloadOptionsOf(connection);

        Assert.Contains(SignalARRRJsonContext.Default, options.TypeInfoResolverChain);
        var json = JsonSerializer.Serialize(new AppPayload { Name = "a", Count = 2 }, options);
        Assert.Contains("\"a\"", json);
    }

    [Fact]
    public void Create_WithAppContext_KeepsTheAppFirstAndReflectionLast() {
        var connection = HARRRConnection.Create(b => b
            .WithUrl("http://127.0.0.1:1/hub")
            .AddJsonProtocol(o => o.PayloadSerializerOptions.TypeInfoResolverChain.Insert(0, AppJson.Default)));
        var options = PayloadOptionsOf(connection);

        // The test host runs with reflection-based JSON on, like every JIT app by default: the
        // reflection resolver goes last, so types missing from the app's context convert as before.
        Assert.Equal(3, options.TypeInfoResolverChain.Count);
        Assert.Same(AppJson.Default, options.TypeInfoResolverChain[0]);
        Assert.Same(SignalARRRJsonContext.Default, options.TypeInfoResolverChain[1]);
        Assert.IsType<System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver>(options.TypeInfoResolverChain[2]);
    }

    [Fact]
    public void Context_RoundTripsServerRequestMessageWithoutReflection() {
        // A chain of contexts only: what an AOT app has.
        var options = new JsonSerializerOptions { TypeInfoResolver = SignalARRRJsonContext.Default };
        var message = new ServerRequestMessage("IPush|Notify", "hello", 3);

        var json = JsonSerializer.Serialize(message, options);
        var back = JsonSerializer.Deserialize<ServerRequestMessage>(json, options)!;

        Assert.Equal("IPush|Notify", back.Method);
        Assert.Equal("hello", ((JsonElement)back.Arguments[0]).GetString());
    }

    [Fact]
    public void AddTo_LeavesReadOnlyOptionsAlone() {
        var options = new JsonSerializerOptions { TypeInfoResolver = AppJson.Default };
        options.MakeReadOnly();

        Assert.False(SignalARRRJson.AddTo(options));
    }

    [Fact]
    public void JsonProtocolSerializer_ConvertsWithTheGivenOptions() {
        var options = new JsonSerializerOptions { TypeInfoResolverChain = { AppJson.Default, SignalARRRJsonContext.Default } };
        var serializer = new JsonProtocolSerializer(options);
        var element = JsonSerializer.SerializeToElement(new AppPayload { Name = "x", Count = 5 }, AppJson.Default.AppPayload);

        var converted = Assert.IsType<AppPayload>(serializer.ConvertTo(element, typeof(AppPayload)));

        Assert.Equal("x", converted.Name);
        Assert.Equal(5, converted.Count);
    }
}
