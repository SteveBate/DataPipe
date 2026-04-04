# Parallel — Concurrent Fan-Out

`Parallel<TParent, TChild>` fans out over a collection of child messages and executes filters concurrently for each one. It is the concurrent counterpart to `ForEach` — where `ForEach` processes items sequentially on the same message, `Parallel` processes independent child messages concurrently.

## When to use Parallel vs ForEach

| | ForEach | Parallel |
|---|---|---|
| **Items** | Properties of a single message | Independent `BaseMessage` instances |
| **Execution** | Sequential | Concurrent |
| **State isolation** | Shared (same message) | Fully isolated (separate messages) |
| **Use case** | Process order lines | Process multiple orders |

## How it works

`Parallel` takes:

1. **Selector** — extracts a collection of child messages from the parent
2. **Mapper** (optional) — copies domain-specific properties from parent to each child
3. **MaxDegreeOfParallelism** (optional) — limits concurrent branches (default: unlimited)
4. **Filters** — the filter chain to execute per branch

Infrastructure properties are copied automatically to each child: lifecycle callbacks (`OnError`, `OnLog`, `OnTelemetry`, etc.), `CancellationToken`, `TelemetryMode`, `Service`, `Actor`, and `PipelineName`.

## Basic usage

```csharp
var pipeline = new DataPipe<BatchMessage>();

pipeline.Add(
    new LoadUnprocessedOrders(),
    new Parallel<BatchMessage, OrderMessage>(
        msg => msg.Orders,
        (parent, child) => child.ConnectionString = parent.ConnectionString,
        new ValidateOrder(),
        new ProcessOrder(),
        new SaveOrderResult()
    ));

await pipeline.Invoke(msg);
```

Each `OrderMessage` flows through `ValidateOrder → ProcessOrder → SaveOrderResult` concurrently.

## Message design

The parent message holds the collection. Each child is an independent `BaseMessage` subclass:

```csharp
public class BatchMessage : AppContext
{
    public List<OrderMessage> Orders { get; set; } = new();
}

public class OrderMessage : BaseMessage
{
    public string OrderId { get; set; }
    public string ConnectionString { get; set; }
    public OrderResult Result { get; set; } = new();
}
```

## Per-branch error isolation with TryCatch

Without error handling, a single branch failure cancels all remaining branches. Wrap branch filters in `TryCatch` for independent error handling:

```csharp
pipeline.Add(new Parallel<BatchMessage, OrderMessage>(
    msg => msg.Orders,
    (parent, child) => child.ConnectionString = parent.ConnectionString,
    new TryCatch<OrderMessage>(
        tryFilters: [
            new ValidateOrder(),
            new ProcessOrder()
        ],
        catchFilters: [new RecordOrderError()]
    )));
```

Failed orders are recorded; other orders continue processing.

## Rate limiting across branches

`OnRateLimit` uses a shared `RateLimiterState` — it works naturally inside parallel branches to throttle the combined throughput:

```csharp
private static readonly RateLimiterState _apiThrottle = new();

pipeline.Add(new Parallel<BatchMessage, OrderMessage>(
    msg => msg.Orders,
    (parent, child) => child.ConnectionString = parent.ConnectionString,
    new OnRateLimit<OrderMessage>(_apiThrottle,
        capacity: 20,
        leakInterval: TimeSpan.FromMilliseconds(250),
        new CallExternalApi(),
        new ProcessResponse()
    )));
```

All branches share the same bucket — total throughput stays within limits regardless of parallelism.

## Combined with retry and circuit breaker

All resilience filters compose inside parallel branches:

```csharp
pipeline.Add(new Parallel<BatchMessage, OrderMessage>(
    msg => msg.Orders,
    (parent, child) => child.ConnectionString = parent.ConnectionString,
    new TryCatch<OrderMessage>(
        tryFilters: [
            new OnRateLimit<OrderMessage>(apiThrottle, 20, TimeSpan.FromMilliseconds(250),
                new OnCircuitBreak<OrderMessage>(circuitState,
                    new OnTimeoutRetry<OrderMessage>(maxRetries: 2,
                        new Timeout<OrderMessage>(TimeSpan.FromSeconds(10),
                            new CallExternalApi())))),
            new SaveResult()
        ],
        catchFilters: [new RecordError()]
    )));
```

Per branch: rate limit → circuit breaker → retry (2 attempts) → 10-second timeout → API call. If all retries fail, `TryCatch` records the error and other orders continue.

## Controlling parallelism

Limit the number of concurrent branches with `maxDegreeOfParallelism`:

```csharp
pipeline.Add(new Parallel<BatchMessage, OrderMessage>(
    msg => msg.Orders,
    mapper: (parent, child) => child.ConnectionString = parent.ConnectionString,
    maxDegreeOfParallelism: 4,
    new ProcessOrder()
));
```

Useful when each branch opens a database connection or consumes significant resources.

## Real-world example — carrier integration

Before (nested pipeline inside a custom filter):

```csharp
// Hidden inside ProcessOrdersInParallel filter
public class ProcessOrdersInParallel : Filter<ParseOrdersMessage>
{
    public async Task Execute(ParseOrdersMessage msg)
    {
        var orderPipe = new DataPipe<UndeliveredOrder>();
        orderPipe.Use(new ExceptionAspect<UndeliveredOrder>());
        orderPipe.Add(new OnRateLimit<UndeliveredOrder>(...));
        
        await Parallel.ForEachAsync(msg.UndeliveredOrders, ...);
    }
}
```

After (declarative in the pipeline):

```csharp
pipe.Add(
    new DeleteOldLogFiles(),
    new CheckCarriersAreValid<ParseOrdersMessage>(),
    new OpenSqlConnection<ParseOrdersMessage>(msg.ConnectionString,
        new PurgeOldEventData(),
        new GetUndeliveredOrders()),
    new Parallel<ParseOrdersMessage, UndeliveredOrder>(
        msg => msg.UndeliveredOrders,
        (parent, child) =>
        {
            child.ConnectionString = parent.ConnectionString;
            child.Commit = parent.Commit;
        },
        new TryCatch<UndeliveredOrder>(
            tryFilters: [
                new OnRateLimit<UndeliveredOrder>(rateLimiter, 20, TimeSpan.FromMilliseconds(250),
                    new OpenSqlConnection<UndeliveredOrder>(child.ConnectionString,
                        new LoadEvents(),
                        new ParseEvents(),
                        new IfTrue<UndeliveredOrder>(o => o.Commit,
                            new UpdateCrm(),
                            new UpdateDeliveredOrdersInStaging())))
            ],
            catchFilters: [new LogOrderError()]
        )));
```

The entire execution strategy is visible in the pipeline declaration.

## Thread safety requirements

- All filters must be stateless (already a core DataPipe requirement)
- `OnLog` and `OnTelemetry` handlers must be thread-safe (logging frameworks and telemetry sinks already are)
- Use thread-safe collections (`ConcurrentBag`, `ConcurrentDictionary`) if branches need to write results back to a shared location
