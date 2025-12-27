# Infrastructure Boundaries

Infrastructure concerns like database connections and transactions are explicit pipeline steps, not hidden framework behavior.

The `DataPipe.Sql` package provides filters for SQL connection and transaction management.

## Scoped infrastructure

```csharp
public async Task ProcessOrder(Order order)
{
    var pipeline = new DataPipe<OrderMessage>();
    
    pipeline.Add(new ValidateOrder());
    
    // SQL transaction wraps business logic - Sequence not required as OpenSqlConnection accepts multiple filters
    pipeline.Add(
        new StartTransaction<OrderMessage>(
            new OpenSqlConnection<OrderMessage>(connectionString: "...",                
                new CreateOrderRecord(),
                new UpdateInventory(),
                new RecordAuditLog()
            )
        ));
    
    pipeline.Add(new SendOrderConfirmation());
    
    var message = new OrderMessage { Order = order };
    await pipeline.Invoke(message);
}
```

## What you see

- The transaction scope is visible
- The connection lifetime is explicit
- Business logic sits inside infrastructure filters
- Nothing is opened, committed, or rolled back without your knowledge

Infrastructure boundaries are first-class citizens in the pipeline, not configuration or attributes.
