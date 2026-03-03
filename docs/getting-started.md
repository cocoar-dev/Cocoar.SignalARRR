# Getting Started with SignalARRR

## Prerequisites

- .NET 10 SDK
- ASP.NET Core application (server)
- .NET 10 console/app (client)

## Project structure

A typical SignalARRR solution has three projects:

```
MyApp.Shared   → references Cocoar.SignalARRR.Contracts
MyApp.Server   → references Cocoar.SignalARRR.Server + MyApp.Shared
MyApp.Client   → references Cocoar.SignalARRR.Client + MyApp.Shared
```

## Step 1: Define contracts (MyApp.Shared)

Add `Cocoar.SignalARRR.Contracts` to your shared project. This single package includes:
- The `[SignalARRRContract]` attribute
- The Roslyn source generator (generates proxy classes at compile time)
- The `ProxyGenerator` dependency (so generated code compiles)

```csharp
using Cocoar.SignalARRR.Contracts;

// Methods the client can call on the server
[SignalARRRContract]
public interface IChatHub {
    Task SendMessage(string user, string message);
    Task<List<string>> GetHistory();
    IAsyncEnumerable<string> StreamMessages(CancellationToken ct);
}

// Methods the server can call on the client
[SignalARRRContract]
public interface IChatClient {
    void ReceiveMessage(string user, string message);
    Task<string> GetClientName();
    IAsyncEnumerable<int> StreamNumbers(int count);
}
```

The source generator automatically creates proxy classes and registers them via
`[ModuleInitializer]`. No manual registration needed.

## Step 2: Server setup (MyApp.Server)

### Install package

```
dotnet add package Cocoar.SignalARRR.Server
```

### Configure services and middleware

```csharp
var builder = WebApplication.CreateBuilder(args);

// Add SignalR (required)
builder.Services.AddSignalR().AddJsonProtocol(options => {
    options.PayloadSerializerOptions.PropertyNamingPolicy = null;
});

// Add SignalARRR
builder.Services.AddSignalARRR(options => options
    .AddServerMethodsFrom(typeof(Program).Assembly));

var app = builder.Build();

app.UseRouting();
app.MapHARRRController<ChatHub>("/chathub");

app.Run();
```

### Define the Hub

The hub class must inherit from `HARRR` (not `Hub`):

```csharp
public class ChatHub : HARRR {
    public ChatHub(IServiceProvider serviceProvider) : base(serviceProvider) { }
}
```

The hub itself can be empty. Methods are organized in `ServerMethods<T>` classes.

### Define ServerMethods

```csharp
public class ChatMethods : ServerMethods<ChatHub>, IChatHub {
    // Dependency injection works — add constructor parameters
    private readonly IChatRepository _repo;

    public ChatMethods(IChatRepository repo) {
        _repo = repo;
    }

    public async Task SendMessage(string user, string message) {
        await _repo.SaveMessage(user, message);
        // Access SignalR primitives via base class properties:
        //   ClientContext — enhanced client info (IP, claims, attributes)
        //   Context       — HubCallerContext (ConnectionId, User, etc.)
        //   Clients       — IHubCallerClients (All, Caller, Group, etc.)
        //   Groups        — IGroupManager
        //   Logger        — ILogger
    }

    public async Task<List<string>> GetHistory() {
        return await _repo.GetRecentMessages();
    }

    public async IAsyncEnumerable<string> StreamMessages(
        [EnumeratorCancellation] CancellationToken ct) {
        while (!ct.IsCancellationRequested) {
            yield return await _repo.GetLatest();
            await Task.Delay(1000, ct);
        }
    }
}
```

ServerMethods classes are:
- Auto-discovered from assemblies registered via `AddServerMethodsFrom()`
- Registered as transient in DI (constructor injection works)
- Can implement the same interface as the contract (for compile-time safety)
- Can have multiple classes per hub (method organization)

### Method naming

By default, methods are registered as `ClassName.MethodName`. Use `[MessageName]`
to customize:

```csharp
[MessageName("Chat")]  // Registers as "Chat.SendMessage" instead of "ChatMethods.SendMessage"
public class ChatMethods : ServerMethods<ChatHub>, IChatHub { ... }

// On a method:
[MessageName("Send")]  // Registers as "Chat.Send"
public Task SendMessage(string user, string message) { ... }
```

## Step 3: Client setup (MyApp.Client)

### Install package

```
dotnet add package Cocoar.SignalARRR.Client
```

### Create connection

```csharp
var connection = HARRRConnection.Create(builder => {
    builder.WithUrl("https://localhost:5001/chathub");
    // All standard HubConnectionBuilder options work:
    // builder.WithAutomaticReconnect();
    // builder.AddJsonProtocol(options => { ... });
});
```

### Start and use

```csharp
await connection.StartAsync();

// Option A: Typed proxy (recommended)
var chat = connection.GetTypedMethods<IChatHub>();
await chat.SendMessage("Alice", "Hello!");

// Option B: Direct invocation
var history = await connection.InvokeCoreAsync<List<string>>(
    "GetHistory", Array.Empty<object>());
```

### Register client-side method handlers

When the server calls client methods, the client needs handlers:

```csharp
// Option A: Register an implementation class
connection.MessageHandler.RegisterInterface<IChatClient, ChatClientImpl>();

// Option B: Register a singleton instance
connection.MessageHandler.RegisterInterface<IChatClient, ChatClientImpl>(
    new ChatClientImpl());

// Option C: Register with factory (supports DI)
connection.MessageHandler.RegisterInterface<IChatClient, ChatClientImpl>(
    sp => new ChatClientImpl(sp.GetRequiredService<ILogger>()));
```

### Connection lifecycle

```csharp
connection.Closed += async (exception) => {
    Console.WriteLine($"Disconnected: {exception?.Message}");
};

connection.Reconnecting += async (exception) => {
    Console.WriteLine("Reconnecting...");
};

connection.Reconnected += async (connectionId) => {
    Console.WriteLine($"Reconnected: {connectionId}");
};

// Access underlying HubConnection if needed
var hubConnection = connection.AsSignalRHubConnection();
```

## Step 4: DynamicProxy (optional)

If you have interfaces that aren't known at compile time (plugin scenarios),
add the DynamicProxy package:

```
dotnet add package Cocoar.SignalARRR.DynamicProxy
```

It automatically registers a fallback proxy factory via `[ModuleInitializer]`.
No code changes needed — `GetTypedMethods<T>()` will use DispatchProxy for
interfaces that don't have source-generated proxies.
