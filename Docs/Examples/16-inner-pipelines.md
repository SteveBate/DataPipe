# Inner / Nested Pipelines

A filter can create and execute a sub-pipeline during its own execution. DataPipe provides two invocation methods to support this:

| Method | Message Ownership | Disposes Message | Use When |
|--------|-------------------|------------------|----------|
| `Invoke(msg)` | Pipeline owns the message | Yes | Inner pipeline has its own message |
| `InvokeBorrowed(msg)` | Caller owns the message | No | Inner pipeline shares the outer message |

## InvokeBorrowed — Shared Message

When the inner pipeline operates on the **same message** as the outer pipeline, use `InvokeBorrowed`. This prevents the inner pipeline from disposing the message, so delegates like `OnLog`, `OnTelemetry`, lifecycle callbacks, and `State` remain intact for subsequent outer filters.

```csharp
public class EnrichOrder : Filter<OrderMessage>
{
    public async Task Execute(OrderMessage msg)
    {
        var innerPipe = new DataPipe<OrderMessage>();
        innerPipe.Name = "EnrichOrder-Inner";
        innerPipe.Use(new ExceptionAspect<OrderMessage>());
        innerPipe.Add(new LookupCustomerDetails());
        innerPipe.Add(new ApplyDiscountRules());

        // InvokeBorrowed — the outer pipeline owns the message lifecycle
        await innerPipe.InvokeBorrowed(msg);

        // OnLog and all other delegates are still intact here
        msg.OnLog?.Invoke("Enrichment complete");
    }
}
```

### Why InvokeBorrowed Matters

`Invoke` always disposes the message when execution completes. Dispose clears all delegate references (`OnLog`, `OnTelemetry`, `OnError`, `OnStart`, `OnComplete`, `OnSuccess`) and transient state. If the inner pipeline called `Invoke` on a shared message, the outer pipeline would lose its logging and telemetry hooks for all remaining filters.

`InvokeBorrowed` skips disposal, leaving the message fully functional for the outer pipeline.

## Invoke — Separate Message

When the inner pipeline uses its own message, call `Invoke` normally. Copy any needed context from the outer message and extract results afterward:

```csharp
public class GetDayStats : Filter<StatsMessage>
{
    private readonly ILogger _logger;

    public GetDayStats(ILogger logger) { _logger = logger; }

    public async Task Execute(StatsMessage msg)
    {
        var innerMsg = new PalletAndBoxesMessage();
        innerMsg.CopyClaimsFrom(msg);  // propagate user context

        var innerPipe = new DataPipe<PalletAndBoxesMessage>();
        innerPipe.Use(new ExceptionAspect<PalletAndBoxesMessage>());
        innerPipe.Use(new LoggingAspect<PalletAndBoxesMessage>(_logger));
        innerPipe.Add(new ValidatePalletAndBoxesMessage());
        innerPipe.Add(new GetPalletAndBoxesStats());
        await innerPipe.Invoke(innerMsg);

        // Copy results back to outer message
        msg.Result.PalletStats = innerMsg.Result;
    }
}
```

## Guidelines

- Use `InvokeBorrowed` when the inner pipeline shares the outer message — avoids disposing delegates mid-execution
- Use `Invoke` when the inner pipeline has its own message — the inner message is disposed normally
- Use `CopyClaimsFrom(msg)` to propagate user context to separate inner messages
- Inner pipes typically have aspects (error handling, logging) but often skip telemetry
- Keep nesting shallow — one level of inner pipeline is the practical limit
