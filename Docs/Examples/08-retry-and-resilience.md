# Retry and Resilience

Retries are scoped to specific operations, not applied globally. `OnTimeoutRetry<T>` wraps the filters that should be retried.

## Retry only what matters

```csharp
public async Task SyncInventory(string warehouseId)
{
    var pipeline = new DataPipe<InventoryMessage>();
    
    pipeline.Add(new LoadLocalInventory());
    
    // Retry only the external API call
    pipeline.Add(
        new OnTimeoutRetry<InventoryMessage>(maxRetries: 3,
            new FetchInventoryFromExternalApi()
    ));
    
    pipeline.Add(new ReconcileInventoryDifferences());
    
    // Retry the database save operation
    pipeline.Add(
        new OnTimeoutRetry<InventoryMessage>(maxRetries: 2,
            new SaveInventoryToDatabase()
    ));
    
    var message = new InventoryMessage { WarehouseId = warehouseId };
    await pipeline.Invoke(message);
}
```

## Why explicit retries matter

- You control exactly which operations are retried
- Different retry strategies for different steps
- No hidden retry behavior
- Failures are isolated and traceable

Retries are visible in the pipeline definition, not buried in configuration or base classes.
