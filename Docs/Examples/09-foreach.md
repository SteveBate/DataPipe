# ForEach — Iterating Over Collections

`ForEach<TMessage, TItem>` iterates over an enumerable from the message and executes child filters for each item. It is the DataPipe equivalent of a `foreach` loop.

## How it works

`ForEach` takes three parameters:

1. **Selector** - a function that extracts the collection from the message
2. **Setter** — an action that assigns the current item onto the message for child filters to read
3. **Filters** — one or more filters to execute per item

```csharp
pipeline.Add(new ForEach<OrderMessage, OrderLine>(
    msg => msg.Lines,                          // selector: what to iterate
    (msg, line) => msg.CurrentLine = line,      // setter: put current item on the message
    new ValidateLineItem(),                     // filters: run for each item
    new CalculateLineTotal()
));
```

## Basic example — processing order lines

```csharp
var pipeline = new DataPipe<OrderMessage>();

pipeline.Add(new ValidateOrderHeader());

pipeline.Add(new ForEach<OrderMessage, OrderLine>(
    msg => msg.Lines,
    (msg, line) => msg.CurrentLine = line,
    new ValidateLineItem(),
    new ApplyLineDiscount(),
    new CalculateLineTotal()
));

pipeline.Add(new CalculateOrderTotal());
pipeline.Add(new SaveOrder());

await pipeline.Invoke(msg);
```

Each line flows through the same filters in sequence. After the loop completes, execution continues with `CalculateOrderTotal`.

## Message design for ForEach

The message needs a property for the collection and a property for the current item:

```csharp
public class OrderMessage : AppContext<OrderResult>
{
    // Input: the collection to iterate
    public List<OrderLine> Lines { get; set; } = new();

    // Internal: set by ForEach on each iteration
    internal OrderLine CurrentLine { get; set; }
}
```

Child filters read `msg.CurrentLine` to access the current item:

```csharp
public class ValidateLineItem : Filter<OrderMessage>
{
    public Task Execute(OrderMessage msg)
    {
        if (msg.CurrentLine.Quantity <= 0)
            msg.Fail(400, $"Invalid quantity for product {msg.CurrentLine.ProductId}");

        return Task.CompletedTask;
    }
}
```

## Null-safe enumerable

If the selector returns `null`, no filters execute and the pipeline continues:

```csharp
pipeline.Add(new ForEach<OrderMessage, OrderLine>(
    msg => msg.Lines,   // if Lines is null, skipped entirely
    (msg, line) => msg.CurrentLine = line,
    new ProcessLine()
));
```

## Stopping mid-loop

`ForEach` checks `msg.ShouldStop` before each iteration. A child filter can stop the loop early:

```csharp
pipeline.Add(new ForEach<ImportMessage, ImportRow>(
    msg => msg.Rows,
    (msg, row) => msg.CurrentRow = row,
    new ValidateRow(),
    new IfTrue<ImportMessage>(msg => !msg.IsSuccess,
        new LambdaFilter<ImportMessage>(msg =>
        {
            msg.Execution.Stop("Validation failed");
            return Task.CompletedTask;
        })),
    new SaveRow()
));
```

When validation fails, the loop breaks immediately. No further rows are processed.

## Combined with DelayExecution for throttled API calls

When sending each item to an external API with rate limits:

```csharp
pipeline.Add(new ForEach<SyncMessage, ProductUpdate>(
    msg => msg.Updates,
    (msg, update) => msg.CurrentUpdate = update,
    new DelayExecution<SyncMessage>(TimeSpan.FromMilliseconds(200)),
    new PushUpdateToApi(),
    new MarkUpdateSynced()
));
```

A 200ms pause before each API call keeps the request rate manageable.

## Combined with retry

Wrap the API call in retry so a single item failure doesn't stop the batch:

```csharp
pipeline.Add(new ForEach<SyncMessage, ProductUpdate>(
    msg => msg.Updates,
    (msg, update) => msg.CurrentUpdate = update,
    new OnTimeoutRetry<SyncMessage>(maxRetries: 2,
        new PushUpdateToApi()),
    new MarkUpdateSynced()
));
```

## Combined with TryCatch for per-item error handling

Process every item even if some fail, collecting errors along the way:

```csharp
pipeline.Add(new ForEach<ImportMessage, ImportRow>(
    msg => msg.Rows,
    (msg, row) => msg.CurrentRow = row,
    new TryCatch<ImportMessage>(
        tryFilters: [new ProcessRow()],
        catchFilters: [new RecordRowError()]
    )
));
```

Each row gets its own try/catch scope. Failed rows are recorded; processing continues with the next row.
