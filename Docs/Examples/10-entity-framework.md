# Entity Framework Integration

The `DataPipe.EntityFramework` package provides filters for managing DbContext lifecycle and transactions explicitly within pipelines.

## Scoped DbContext and transactions

```csharp
public async Task UpdateCustomerProfile(CustomerUpdateRequest request)
{
    var pipeline = new DataPipe<CustomerMessage>();
    
    pipeline.Add(new ValidateUpdateRequest());
    
    // EF transaction wraps business logic
    pipeline.Add(
        new StartEfTransaction<CustomerMessage>(
            new OpenDbContext<CustomerMessage>(dbContextFactory: () => new AppDbContext(),
                new LoadCustomerEntity(),
                new ApplyProfileChanges(),
                new RecordChangeHistory()
            )
        ));
    
    pipeline.Add(new SendProfileUpdateNotification());
    
    var message = new CustomerMessage { Request = request };
    await pipeline.Invoke(message);
}
```

## Why this matters

- DbContext lifetime is visible in the pipeline
- Transaction boundaries are explicit
- No hidden Unit of Work pattern
- No repository abstractions hiding the data layer

The infrastructure is part of the pipeline definition, making it clear when and where database operations occur.
