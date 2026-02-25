# Minimal Pipeline

The smallest useful DataPipe pipeline requires only three things: a message, a pipeline, and an invocation.

## Lambda filters are first-class

```csharp
public class OrderMessage : BaseMessage
{
    public string OrderId { get; set; }
}

public async Task ProcessOrder(string orderId)
{
    var pipeline = new DataPipe<OrderMessage>();
    
    pipeline.Add(async msg =>
    {
        await SaveToDatabase(msg);
    });
    
    await pipeline.Invoke(new OrderMessage { OrderId = orderId });
}
```

## What's happening

- The pipeline is created for a specific message type
- A lambda is added as a filter
- The message flows through when `Invoke` is called
- Execution is in-memory and sequential

Lambda filters and class-based filters are interchangeable within a pipeline. Use lambdas for simple steps, custom filter classes for reusable logic.
