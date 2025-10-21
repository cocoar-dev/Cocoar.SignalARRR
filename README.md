# SignalARRR

**Enhanced SignalR library with advanced features for enterprise applications**

SignalARRR extends Microsoft SignalR with powerful capabilities for building scalable, maintainable real-time applications.

## ✨ Key Features

* **Organized Hub Methods** - Split hub logic into multiple classes by inheriting from `ServerMethods<T>`
* **Granular Authorization** - Apply `[Authorize]` attributes on individual methods or entire classes
* **Continuous Authentication** - Token validation on each method call with automatic challenge/refresh
* **Bidirectional RPC** - Server can invoke client methods and await responses (unique!)
* **Advanced Streaming** - Support for `IObservable<T>`, `IAsyncEnumerable<T>`, and `ChannelReader<T>`
* **Type-Safe Proxies** - Generate strongly-typed client proxies from interfaces
* **Multi-Platform** - Server (.NET 8+), .NET Client (.NET 8+ & .NET Standard 2.0), TypeScript Client

## 🎯 Framework Support

- **Server**: .NET 8.0+
- **Client**: .NET 8.0+ and .NET Standard 2.0
- **TypeScript**: Browser and Node.js environments

## 📦 Installation

```bash
# Server
dotnet add package Cocoar.SignalARRR.Server

# .NET Client  
dotnet add package Cocoar.SignalARRR.Client

# TypeScript Client
npm install signalarrr
```

## 🚀 Quick Start

### Server Setup

```csharp
// Define your hub
public class MyHub : HARRR
{
    public MyHub(IServiceProvider serviceProvider) : base(serviceProvider) { }
}

// Organize methods in separate classes
public class UserMethods : ServerMethods<MyHub>
{
    [Authorize]
    public async Task<string> GetUserName()
    {
        return ClientContext.User.Identity?.Name ?? "Anonymous";
    }
}

// Configure in Startup.cs
services.AddSignalARRR(options =>
{
    options.RegisterServerMethods(Assembly.GetExecutingAssembly());
});

app.UseEndpoints(endpoints =>
{
    endpoints.MapHARRRController<MyHub>("/hubs/my");
});
```

### .NET Client

```csharp
// Type-safe client
var connection = HARRRConnection.Create(builder =>
{
    builder.WithUrl("https://localhost:5001/hubs/my");
});

await connection.StartAsync();

// Call server methods with type safety
var userName = await connection.InvokeAsync<string>("GetUserName");
```

## 📚 Documentation

Library is actively being modernized and documented. Comprehensive documentation coming soon!

## 🔧 Building from Source

```bash
dotnet build src/Cocoar.SignalARRR.slnx
dotnet test src/Cocoar.SignalARRR.slnx
```

## 📄 License

MIT License - see [LICENSE](LICENSE) file for details

## 🤝 Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md) for contribution guidelines.

## 🔒 Security

See [SECURITY.md](SECURITY.md) for security policy and vulnerability reporting.

---

**Status**: Active Development | **Owner**: Cocoar | **Maintainer**: Bernhard Windisch

