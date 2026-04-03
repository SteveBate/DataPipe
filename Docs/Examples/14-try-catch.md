# Try / Catch — Error Handling with Fallback

`TryCatch<T>` executes a set of filters and, if an exception occurs, runs fallback filters instead of propagating the error. This is useful for degraded-mode processing, compensating actions, or providing default values when a primary path fails.

## Basic usage — fallback on any error

```csharp
var pipeline = new DataPipe<PricingMessage>();

pipeline.Add(new TryCatch<PricingMessage>(
    tryFilters: [new FetchLivePricing()],
    catchFilters: [new UseCachedPricing(), new LogDegradedMode()]
));

pipeline.Add(new CalculateTotal());

await pipeline.Invoke(msg);
```

If `FetchLivePricing` throws (API down, timeout, etc.), the pipeline falls back to `UseCachedPricing` and continues normally. Without `TryCatch`, the pipeline would fail entirely.

## Selective catch — handle specific exceptions only

Pass a predicate to control which exceptions are caught. Unmatched exceptions propagate normally:

```csharp
pipeline.Add(new TryCatch<OrderMessage>(
    tryFilters: [new CallPaymentGateway()],
    catchFilters: [new QueueForManualProcessing()],
    catchWhen: (ex, msg) => ex is HttpRequestException or TimeoutException
));
```

Only network/timeout errors trigger the fallback. Other exceptions (e.g. `ArgumentException` from bad data) propagate to the exception aspect as usual.

## Accessing the caught exception

The caught exception is stored in the message's transient state as `TryCatch.Exception` while the catch filters execute:

```csharp
public class LogDegradedMode : Filter<PricingMessage>
{
    public Task Execute(PricingMessage msg)
    {
        var ex = msg.State.Get<Exception>("TryCatch.Exception");
        msg.OnLog?.Invoke($"Primary path failed: {ex.Message}. Using fallback.");
        return Task.CompletedTask;
    }
}
```

## Combined with Timeout — fallback on slow responses

```csharp
pipeline.Add(new TryCatch<InventoryMessage>(
    tryFilters: [
        new Timeout<InventoryMessage>(TimeSpan.FromSeconds(5),
            new FetchInventoryFromSupplier())
    ],
    catchFilters: [new UseLastKnownInventory()]
));
```

If the supplier API doesn't respond within 5 seconds, the `Timeout` fires a `TimeoutException`, which `TryCatch` catches and falls back to cached inventory.

## Combined with retry — exhaust retries then fall back

```csharp
pipeline.Add(new TryCatch<OrderMessage>(
    tryFilters: [
        new OnTimeoutRetry<OrderMessage>(maxRetries: 2,
            new SyncWithExternalSystem())
    ],
    catchFilters: [new QueueForLaterSync(), new ContinueWithLocalData()]
));
```

The retry exhausts its attempts first. If all retries fail, the final exception propagates to `TryCatch`, which catches it and runs the fallback path.

## What happens

- The `tryFilters` execute normally
- If no exception occurs, the `catchFilters` are never executed
- If an exception occurs (and matches `catchWhen` if provided), the `catchFilters` execute
- Pipeline execution continues after whichever path completes
- If the `catchFilters` themselves throw, that exception propagates normally
