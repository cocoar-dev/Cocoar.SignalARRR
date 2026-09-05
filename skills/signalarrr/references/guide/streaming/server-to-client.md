<!-- Generated from website/guide/streaming/server-to-client.md by website/scripts/sync-skill.mjs. Do not edit; edit the docs page. -->

# Server-to-Client Streaming

SignalARRR supports streaming results from server methods to clients using `IAsyncEnumerable<T>`, `IObservable<T>`, or `ChannelReader<T>`.

## IAsyncEnumerable (recommended)

The simplest streaming pattern — use `yield return` in an async iterator:

```csharp
[SignalARRRContract]
public interface IDataHub
{
    IAsyncEnumerable<string> StreamMessages(CancellationToken ct);
    IAsyncEnumerable<StockPrice> StreamPrices(string symbol, CancellationToken ct);
}
```

```csharp
public class DataMethods : ServerMethods<AppHub>, IDataHub
{
    public async IAsyncEnumerable<string> StreamMessages(
        [EnumeratorCancellation] CancellationToken ct)
    {
        var i = 0;
        while (!ct.IsCancellationRequested)
        {
            yield return $"Message {i++}";
            await Task.Delay(1000, ct);
        }
    }

    public async IAsyncEnumerable<StockPrice> StreamPrices(
        string symbol,
        [EnumeratorCancellation] CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            yield return await _stockService.GetPrice(symbol);
            await Task.Delay(500, ct);
        }
    }
}
```

## Consume on the .NET client

```csharp
var data = connection.GetTypedMethods<IDataHub>();

await foreach (var msg in data.StreamMessages(cancellationToken))
{
    Console.WriteLine(msg);
}
```

## Consume in TypeScript

```ts
connection.stream<string>('DataMethods.StreamMessages').subscribe({
    next: msg => console.log(msg),
    error: err => console.error('Stream error:', err),
    complete: () => console.log('Stream ended'),
});
```

## IObservable

Use Rx.NET observables for reactive streaming:

```csharp
public IObservable<StockPrice> ObservePrices(string symbol)
{
    return Observable.Create<StockPrice>(async (observer, ct) =>
    {
        while (!ct.IsCancellationRequested)
        {
            var price = await _stockService.GetPrice(symbol);
            observer.OnNext(price);
            await Task.Delay(500, ct);
        }
    });
}
```

## ChannelReader

Use channels for producer/consumer patterns:

```csharp
public ChannelReader<LogEntry> StreamLogs()
{
    var channel = Channel.CreateUnbounded<LogEntry>();

    _ = Task.Run(async () =>
    {
        await foreach (var entry in _logService.WatchLogs())
        {
            await channel.Writer.WriteAsync(entry);
        }
        channel.Writer.Complete();
    });

    return channel.Reader;
}
```

## Cancellation

All streaming patterns support cancellation. The client disconnecting or explicitly cancelling the stream triggers the `CancellationToken`:

```csharp
// .NET client — cancel after 10 seconds
using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
await foreach (var msg in data.StreamMessages(cts.Token))
{
    Console.WriteLine(msg);
}
```

```ts
// TypeScript — cancel by disposing the subscription
const sub = connection.stream<string>('DataMethods.StreamMessages').subscribe({
    next: msg => console.log(msg),
});

// Later...
sub.dispose();
```

## Next steps

- [Client-to-Server Streaming](./client-to-server.md) — stream data to the server
- [Cancellation Propagation](../advanced/cancellation.md) — remote cancellation
- [Typed Methods](../dotnet-client/typed-methods.md) — how return types map to protocol
