using System.Threading.Channels;
using Cocoar.SignalARRR.Common.Attributes;
using Cocoar.SignalARRR.Server;
using Cocoar.SignalARRR.Tests.SharedModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Cocoar.SignalARRR.AotSmokeServer {

    /// <summary>The hub: the same methods as the IntegrationTestServer's, so the client probe's basic steps run against both.</summary>
    public class SmokeHub : HARRR, ITestServerMethods {

        public SmokeHub(IServiceProvider serviceProvider) : base(serviceProvider) {
        }

        public string GetName() => "MyName";
        public Task<string> GetNameAsync() => Task.FromResult("MyNameAsync");
        public Guid GetGuid() => Guid.NewGuid();
        public Task<Guid> GetGuidAsync() => Task.FromResult(Guid.NewGuid());
        public void Nothing() { }
        public Task NothingAsync() => Task.CompletedTask;
        public string Echo(string message) => message;

        public ChannelReader<int> Counter(int count, int delay, CancellationToken cancellationToken) {
            var channel = Channel.CreateUnbounded<int>();
            _ = Task.Run(async () => {
                for (var i = 0; i < count; i++) {
                    await channel.Writer.WriteAsync(i, cancellationToken);
                    if (delay > 0) await Task.Delay(delay, cancellationToken);
                }
                channel.Writer.Complete();
            }, cancellationToken);
            return channel.Reader;
        }
    }

    public sealed class SmokeGreeting {
        public string Text => "injected";
    }

    /// <summary>A ServerMethods class with a contract, by-name methods, defaults, injection, upload and a generic method.</summary>
    public class SmokeMethods : ServerMethods<SmokeHub>, IAotSmokeServerMethods {

        public Task<int> Add(int a, int b) => Task.FromResult(a + b);

        public string Page(int index, int size = 25) => $"{index}:{size}";

        [GenericArguments(typeof(int), typeof(string))]
        public T Echo<T>(T value) => value;

        public string Inject([FromServices] SmokeGreeting greeting) => greeting.Text;

        public string ReadStream(Stream data) {
            using var reader = new StreamReader(data);
            return reader.ReadToEnd();
        }

        [Authorize(Policy = "Nobody")]
        public string Secret() => "must not be reached";
    }

    /// <summary>Type-level authorization with a method-level opt-out.</summary>
    [Authorize(Policy = "Nobody")]
    public class SecureMethods : ServerMethods<SmokeHub> {

        [AllowAnonymous]
        public string Open() => "open";

        public string Closed() => "must not be reached";
    }
}
