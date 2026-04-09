# Retry

`OnTimeoutRetry<T>` wraps filters with retry logic. You control the maximum attempts, which exceptions trigger a retry, and how long to wait between attempts.

## Default retry

```csharp
var pipeline = new DataPipe<OrderMessage>();

pipeline.Add(new ValidateOrder());

pipeline.Add(new OnTimeoutRetry<OrderMessage>(maxRetries: 3,
    new SaveToDatabase()));

await pipeline.Invoke(msg);
```

The default configuration retries on `TimeoutException`, transport-level errors, and deadlocks. Delay between attempts increases linearly: 2s, 4s, 6s.

## Custom retry condition

Control which exceptions trigger a retry:

```csharp
pipeline.Add(new OnTimeoutRetry<OrderMessage>(
    maxRetries: 5,
    retryWhen: (ex, msg) => ex is HttpRequestException,
    new CallPaymentGateway()));
```

## Custom delay strategy

Replace the default linear delay with exponential backoff or any custom logic:

```csharp
pipeline.Add(new OnTimeoutRetry<OrderMessage>(
    maxRetries: 4,
    customDelay: (attempt, msg) => TimeSpan.FromSeconds(Math.Pow(2, attempt)),
    new SyncWithExternalSystem()));
```

## Fully custom

Combine a custom retry condition with a custom delay:

```csharp
pipeline.Add(new OnTimeoutRetry<OrderMessage>(
    maxRetries: 3,
    retryWhen: (ex, msg) => ex is TimeoutException || ex.Message.Contains("busy"),
    customDelay: (attempt, msg) => TimeSpan.FromSeconds(attempt * 1.5),
    new CallExternalApi()));
```

## Message contract

The message must implement `IAmRetryable`:

```csharp
public class OrderMessage : BaseMessage, IAmRetryable
{
    public int Attempt { get; set; }
    public int MaxRetries { get; set; }
    public Action<int> OnRetrying { get; set; } = attempt => { };
}
```

`Attempt` and `MaxRetries` are set automatically by the filter. Use `OnRetrying` to hook into retry events for logging or metrics in the consuming application.

## Scoped retries

Retry only the steps that need it. Different operations can have different retry strategies:

```csharp
var pipeline = new DataPipe<InventoryMessage>();

pipeline.Add(new LoadLocalInventory());

pipeline.Add(new OnTimeoutRetry<InventoryMessage>(maxRetries: 3,
    new FetchInventoryFromExternalApi()));

pipeline.Add(new ReconcileInventoryDifferences());

pipeline.Add(new OnTimeoutRetry<InventoryMessage>(maxRetries: 2,
    retryWhen: (ex, msg) => ex.Message.Contains("deadlocked"),
    new SaveInventoryToDatabase()));

await pipeline.Invoke(msg);
```

## Retry combined with infrastructure boundaries

Retry can wrap other structural filters. A common pattern is retrying a transactional database operation:

```csharp
pipeline.Add(new OnTimeoutRetry<OrderMessage>(maxRetries: 2,
    new OpenSqlConnection<OrderMessage>(connectionString,
        new StartSqlTransaction<OrderMessage>(
            new SaveOrder(),
            new UpdateInventory()
        )
    )
));
```

Each retry gets a fresh connection and transaction.

## Telemetry attributes

When telemetry is enabled, `OnTimeoutRetry` emits start and end events with these attributes:

| Attribute | Phase | Description |
|-----------|-------|-------------|
| `max-attempts` | Start, End | Total attempts allowed (maxRetries + 1) |
| `final-attempt` | End | The attempt number when execution completed |
| `retry` | End | Number of retries that occurred (only if retries happened) |
| `retry-reason` | End | The exception message that triggered the last retry |

Child filter start events also include `retry` and `retry-reason` attributes on subsequent attempts, making it clear in telemetry which filter invocations are retries.

## Why explicit retries matter

- You control exactly which operations are retried
- Different retry strategies for different steps
- No hidden retry behavior or global configuration
- Retry state is visible in telemetry for monitoring and debugging
- Failures are isolated and traceable
