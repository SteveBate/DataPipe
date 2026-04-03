# Conditional Execution

Not every filter should run for every message. `IfTrue<T>` makes conditional logic explicit and readable.

## Execute only when the condition matches

```csharp
public async Task ProcessDocument(Document doc)
{
    var pipeline = new DataPipe<DocumentMessage>();
    
    pipeline.Add(new ExtractMetadata());
    
    pipeline.Add(new IfTrue<DocumentMessage>(
        msg => msg.RequiresApproval,
        new SendForApproval()
    ));
    
    pipeline.Add(new IfTrue<DocumentMessage>(
        msg => msg.IsConfidential,
        new EncryptDocument()
    ));
    
    pipeline.Add(new SaveDocument());
    
    var message = new DocumentMessage { Document = doc };
    await pipeline.Invoke(message);
}
```

The alternative shows cleaner formatting if use-case is small enough:

```csharp
public async Task ProcessDocument(Document doc)
{
    var pipeline = new DataPipe<DocumentMessage>();
    
    pipeline.Add(
        new ExtractMetadata(),
        new IfTrue<DocumentMessage>(msg => msg.RequiresApproval,
            new SendForApproval()),
        new IfTrue<DocumentMessage>(msg => msg.IsConfidential,
            new EncryptDocument()),
        new SaveDocument()
    );
    
    var message = new DocumentMessage { Document = doc };
    await pipeline.Invoke(message);
}
```


## What happens

- The condition is evaluated when the filter is reached
- If `true`, the nested filter executes
- If `false`, it's skipped
- Execution continues either way
- The nested filter can itself be a composite or another conditional

This keeps branching logic visible inside the pipeline definition, not buried in filter implementations.

---

## If / Else branching with IfTrueElse

When you need both a true and false path, use `IfTrueElse<T>` instead of two separate `IfTrue<T>` calls with inverted predicates:

```csharp
public async Task ProcessOrder(OrderData data)
{
    var pipeline = new DataPipe<OrderMessage>();
    
    pipeline.Add(
        new ValidateOrder(),
        new IfTrueElse<OrderMessage>(msg => msg.Customer.IsPremium,
            thenFilters: [new ApplyPremiumDiscount(), new AssignPriorityShipping()],
            elseFilters: [new ApplyStandardPricing(), new AssignStandardShipping()]
        ),
        new SaveOrder()
    );
    
    var message = new OrderMessage { Order = data };
    await pipeline.Invoke(message);
}
```

### What happens

- The predicate is evaluated once when the filter is reached
- If `true`, the `thenFilters` array executes
- If `false`, the `elseFilters` array executes
- Execution continues after whichever branch completes

### Why not two IfTrue calls?

```csharp
// Avoid this — evaluates the same condition twice
pipeline.Add(new IfTrue<OrderMessage>(msg => msg.Customer.IsPremium,
    new ApplyPremiumDiscount()));
pipeline.Add(new IfTrue<OrderMessage>(msg => !msg.Customer.IsPremium,
    new ApplyStandardPricing()));
```

Using `IfTrueElse` evaluates the predicate once, makes the branching intent explicit, and avoids subtle bugs if the predicate has side effects or depends on state that changes between evaluations.
