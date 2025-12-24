# DataPipe.EntityFramework

**Entity Framework helpers for DataPipe pipelines**

Provides filters to integrate Entity Framework operations into DataPipe pipelines.

---

## Install

```bash
dotnet add package SCB.DataPipe.EntityFramework
```

## Filters

### OpenDbContext

Opens a scoped `DbContext` for the duration of child filter execution.

```csharp
pipe.Run(new OpenDbContext<OrderMessage>(
    msg => new AppDbContext(options),
    new LoadOrder(),
    new SaveOrder()
));
```

### StartEfTransaction

Wraps child filters in a database transaction. Commits when `msg.Commit = true`.

```csharp
pipe.Run(new OpenDbContext<OrderMessage>(
    msg => new AppDbContext(options),
    new StartEfTransaction<OrderMessage>(
        new ProcessOrder(),
        new MarkCommit()  // Sets msg.Commit = true
    )
));
```

## Important: Scoped Resource Lifetime

Both `OpenDbContext` and `StartEfTransaction` manage resources with **strictly scoped lifetimes**.

### Rules for Child Filters

1. **Do not capture references** to `msg.DbContext` or the transaction for use after the filter's `Execute` method returns.

2. **Do not use fire-and-forget patterns** (e.g., `Task.Run` without awaiting) that access the `DbContext`.

3. **Complete all database work** within the synchronous-async flow of your filter's `Execute` method.

### ❌ Incorrect Usage

```csharp
public class BadFilter : Filter<OrderMessage>
{
    public async Task Execute(OrderMessage msg)
    {
        // DON'T DO THIS - context will be disposed before this runs
        _ = Task.Run(async () => 
        {
            await msg.DbContext.SaveChangesAsync();  // ObjectDisposedException!
        });
    }
}
```

### ✅ Correct Usage

```csharp
public class GoodFilter : Filter<OrderMessage>
{
    public async Task Execute(OrderMessage msg)
    {
        // All DbContext work completed before returning
        msg.DbContext.Orders.Add(new Order { ... });
        await msg.DbContext.SaveChangesAsync();
    }
}
```

## Key Points

- Scoped EF DbContext and transaction management
- Automatic disposal when child filters complete
- Works seamlessly with DataPipe pipelines
- Fully async and thread-safe
- Supports custom isolation levels for transactions
- Gracefully handles non-relational providers

## Documentation

Full documentation:
👉 https://github.com/SteveBate/DataPipe

## License

MIT

