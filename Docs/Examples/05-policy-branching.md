# Policy Branching

IfTrue is suitable for simple conditional execution.
When you need to select exactly one path from multiple options based on message state, use `Policy<T>`. It evaluates a selector and executes the chosen filter.

## Select one path at runtime

```csharp
public async Task RouteInvoice(Invoice invoice)
{
    var pipeline = new DataPipe<InvoiceMessage>();
    
    pipeline.Add(new ValidateInvoice());
    
    pipeline.Add(new Policy<InvoiceMessage>(msg =>
    {
        return msg.InvoiceType switch
        {
            InvoiceType.Standard => new ProcessStandardInvoice(),
            InvoiceType.Recurring => new ProcessRecurringInvoice(),
            InvoiceType.CreditNote => 
                new Sequence<InvoiceMessage>(
                    new ReverseOriginalInvoice(),
                    new ApplyCreditToAccount()
            ),
            _ => null
        };
    }));
    
    pipeline.Add(new SendInvoiceNotification());
    
    var message = new InvoiceMessage { Invoice = invoice };
    await pipeline.Invoke(message);
}
```

## How it works

- The selector function evaluates the message
- Returns a filter to execute, or `null` to skip
- Only the selected branch runs
- Use `Sequence<T>` to group multiple filters in a branch

This keeps branching logic centralized and obvious when reading the pipeline.
