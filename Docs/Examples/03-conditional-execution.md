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
