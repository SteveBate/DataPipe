# ForEach - Sequential Fan-Out

`ForEach<TParent, TChild>` iterates a collection of child messages and executes child filters sequentially. It is the sequential counterpart to `ParallelForEach<TParent, TChild>`.

## How it works

`ForEach` takes:

1. Selector - extracts child messages from the parent
2. Mapper (optional) - copies domain values from parent to child
3. Filters - one or more filters to execute for each child, in sequence

```csharp
pipeline.Add(new ForEach<OrderBatchMessage, OrderLineMessage>(msg => msg.Lines,
    (parent, child) => child.Currency = parent.Currency,
    new ValidateLineItem(),
    new CalculateLineTotal()
));
```

## Basic example - processing order lines

```csharp
var pipeline = new DataPipe<OrderBatchMessage>();

pipeline.Add(new ValidateOrderHeader());

pipeline.Add(new ForEach<OrderBatchMessage, OrderLineMessage>(msg => msg.Lines,
    (parent, child) => child.Currency = parent.Currency,
    new ValidateLineItem(),
    new ApplyLineDiscount(),
    new CalculateLineTotal()
));

pipeline.Add(new CalculateOrderTotal());
pipeline.Add(new SaveOrder());

await pipeline.Invoke(msg);
```

Each child line message flows through the same child filters sequentially. After the loop completes, pipeline execution continues.

## Message design for ForEach

The parent holds the collection, and each child is a `BaseMessage` type:

```csharp
public class OrderBatchMessage : AppContext<OrderResult>
{
    public List<OrderLineMessage> Lines { get; set; } = new();
    public string Currency { get; set; } = "GBP";
}

public class OrderLineMessage : BaseMessage
{
    public string ProductId { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal LineTotal { get; set; }
    public string Currency { get; set; } = string.Empty;
}
```

Child filters run against `OrderLineMessage` directly:

```csharp
public class ValidateLineItem : Filter<OrderLineMessage>
{
    public Task Execute(OrderLineMessage msg)
    {
        if (msg.Quantity <= 0)
            msg.Fail(400, $"Invalid quantity for product {msg.ProductId}");

        return Task.CompletedTask;
    }
}
```

## Null-safe selector

If selector returns `null`, no child filters run and the parent pipeline continues:

```csharp
pipeline.Add(new ForEach<OrderBatchMessage, OrderLineMessage>(msg => msg.Lines,
    new ProcessLine()
));
```

## Stopping mid-loop

`ForEach` checks parent stop conditions before each child. If a child calls `Execution.Stop(...)`, `ForEach` propagates stop to the parent and exits the loop.

```csharp
pipeline.Add(new ForEach<ImportBatchMessage, ImportRowMessage>(msg => msg.Rows,
    new ValidateRow(),
    new IfTrue<ImportRowMessage>(row => !row.IsSuccess,
        new LambdaFilter<ImportRowMessage>(row =>
        {
            row.Execution.Stop("Validation failed");
            return Task.CompletedTask;
        })),
    new SaveRow()
));
```

## Combined with DelayExecution for throttled API calls

```csharp
pipeline.Add(new ForEach<SyncBatchMessage, ProductUpdateMessage>(msg => msg.Updates,
    new DelayExecution<ProductUpdateMessage>(TimeSpan.FromMilliseconds(200)),
    new PushUpdateToApi(),
    new MarkUpdateSynced()
));
```

## Combined with retry

```csharp
pipeline.Add(new ForEach<SyncBatchMessage, ProductUpdateMessage>(msg => msg.Updates,
    new OnTimeoutRetry<ProductUpdateMessage>(maxRetries: 2,
        new PushUpdateToApi()),
    new MarkUpdateSynced()
));
```

## Combined with TryCatch for per-item error handling

```csharp
pipeline.Add(new ForEach<ImportBatchMessage, ImportRowMessage>(msg => msg.Rows,
    new TryCatch<ImportRowMessage>(
        tryFilters: [new ProcessRow()],
        catchFilters: [new RecordRowError()]
    )
));
```

Each row message gets its own try/catch scope. Failed rows are recorded, and processing continues.

## Switching to parallel in one line

You can easily switch from sequential to parallel fan-out by changing only the filter type:

```csharp
// Sequential
new ForEach<OrderBatchMessage, OrderLineMessage>(msg => msg.Lines,
    (parent, child) => child.Currency = parent.Currency,
    new ValidateLineItem(),
    new CalculateLineTotal())

// Parallel
new ParallelForEach<OrderBatchMessage, OrderLineMessage>(msg => msg.Lines,
    (parent, child) => child.Currency = parent.Currency,
    new ValidateLineItem(),
    new CalculateLineTotal())
```

Optionally add `maxDegreeOfParallelism` on the parallel version when you need to cap concurrent work.
