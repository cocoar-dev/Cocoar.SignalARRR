# Streaming

SignalARRR supports streaming in both directions using `IAsyncEnumerable<T>`,
`IObservable<T>`, and `ChannelReader<T>`.

---

## Server-to-Client Streaming

### Define the contract

```csharp
[SignalARRRContract]
public interface IChatHub {
    IAsyncEnumerable<string> StreamMessages(CancellationToken ct);
    IObservable<int> ObserveCountdown(int from);
    ChannelReader<LogEntry> StreamLogs(string filter);
}
```

### Implement on the server

```csharp
public class ChatMethods : ServerMethods<ChatHub>, IChatHub {

    // IAsyncEnumerable — preferred for most streaming scenarios
    public async IAsyncEnumerable<string> StreamMessages(
        [EnumeratorCancellation] CancellationToken ct) {
        while (!ct.IsCancellationRequested) {
            yield return $"Message at {DateTime.Now:HH:mm:ss}";
            await Task.Delay(1000, ct);
        }
    }

    // IObservable — for Rx-based pipelines
    public IObservable<int> ObserveCountdown(int from) {
        return Observable.Interval(TimeSpan.FromSeconds(1))
            .Take(from)
            .Select(i => from - (int)i);
    }

    // ChannelReader — for producer/consumer patterns
    public ChannelReader<LogEntry> StreamLogs(string filter) {
        var channel = Channel.CreateUnbounded<LogEntry>();
        _ = WriteLogsAsync(channel.Writer, filter);
        return channel.Reader;
    }

    private async Task WriteLogsAsync(ChannelWriter<LogEntry> writer, string filter) {
        try {
            await foreach (var log in _logService.Watch(filter)) {
                await writer.WriteAsync(log);
            }
        } finally {
            writer.Complete();
        }
    }
}
```

### Consume on the client

```csharp
var chat = connection.GetTypedMethods<IChatHub>();

// IAsyncEnumerable — natural async iteration
var cts = new CancellationTokenSource();
await foreach (var msg in chat.StreamMessages(cts.Token)) {
    Console.WriteLine(msg);
    if (shouldStop) cts.Cancel();
}

// IObservable — Rx subscription
chat.ObserveCountdown(10).Subscribe(
    count => Console.WriteLine($"Countdown: {count}"),
    () => Console.WriteLine("Done!"));

// ChannelReader — read from channel
var reader = chat.StreamLogs("error");
while (await reader.WaitToReadAsync()) {
    while (reader.TryRead(out var log)) {
        Console.WriteLine(log);
    }
}
```

---

## Client-to-Server Streaming (Server-Initiated)

The server can request a stream from the client. The client returns
`IAsyncEnumerable<T>` and SignalARRR handles the wire protocol automatically.

### Define the client contract

```csharp
[SignalARRRContract]
public interface IDataClient {
    IAsyncEnumerable<int> StreamNumbers(int count);
    Task<string> GetStatus();
}
```

### Implement on the client

```csharp
public class DataClientImpl : IDataClient {
    public async IAsyncEnumerable<int> StreamNumbers(int count) {
        for (int i = 0; i < count; i++) {
            yield return i;
            await Task.Delay(100);
        }
    }

    public Task<string> GetStatus() => Task.FromResult("OK");
}

// Register the implementation
connection.MessageHandler.RegisterInterface<IDataClient, DataClientImpl>();
```

### Request the stream from the server

```csharp
public class DataMethods : ServerMethods<MyHub> {
    public async Task ProcessClientData() {
        // Get typed proxy for the calling client
        var client = ClientContext.GetTypedMethods<IDataClient>();

        // Stream data from the client
        await foreach (var number in client.StreamNumbers(100)) {
            Logger.LogInformation("Received: {Number}", number);
        }
    }
}
```

### How it works internally

1. Server calls `StreamNumbers(100)` on the client proxy
2. Proxy sends a `ServerRequestMessage` with a `StreamId` to the client
3. Client invokes `DataClientImpl.StreamNumbers(100)`
4. Client enumerates the `IAsyncEnumerable<int>` and sends each item back
   to the server via `StreamItemToServer(streamId, item)`
5. Client signals completion via `StreamCompleteToServer(streamId, null)`
6. Server reads items from a `Channel<object>` managed by `ServerStreamManager`
7. Server receives items as `IAsyncEnumerable<T>` through the proxy

---

## Cancellation

### Server-to-client stream cancellation

Pass a `CancellationToken` to the streaming method. When cancelled, the server
stops the stream:

```csharp
var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
await foreach (var msg in chat.StreamMessages(cts.Token)) {
    Console.WriteLine(msg);
}
// Stream stops after 30 seconds
```

On the server side, use `[EnumeratorCancellation]` on the `CancellationToken`
parameter:

```csharp
public async IAsyncEnumerable<string> StreamMessages(
    [EnumeratorCancellation] CancellationToken ct) {
    // ct is cancelled when the client disconnects or cancels
}
```

### Server-to-client CancellationToken propagation

When the server calls a client method, it can pass a `CancellationToken` that
propagates to the client:

```csharp
// Server side
var cts = new CancellationTokenSource();
var client = ClientContext.GetTypedMethods<IWorkerClient>();
_ = client.DoLongWork(cts.Token);  // Token propagated to client

// Later...
cts.Cancel();  // Client's CancellationToken is cancelled remotely
```

```csharp
// Client side
public class WorkerImpl : IWorkerClient {
    public async Task DoLongWork(CancellationToken ct) {
        while (!ct.IsCancellationRequested) {
            await Task.Delay(1000, ct);
            // Cancelled when server calls cts.Cancel()
        }
    }
}
```

---

## Return type mapping

| Contract return type | Proxy dispatch | Wire protocol |
|---|---|---|
| `IAsyncEnumerable<T>` | `StreamAsync<T>()` | `StreamMessage` |
| `ChannelReader<T>` | `StreamAsync<T>()` → `ToChannelReader()` | `StreamMessage` |
| `IObservable<T>` | `StreamAsync<T>()` → `ToObservable()` | `StreamMessage` |
| `Task<T>` | `InvokeAsync<T>()` | `InvokeMessageResult` |
| `Task` | `SendAsync()` | `InvokeMessage` |
| `void` | `Send()` | `SendMessage` |
| `T` (sync) | `Invoke<T>()` | `InvokeMessageResult` |
