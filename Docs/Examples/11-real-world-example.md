# Real-World Example

A realistic pipeline processing incoming supplier invoices, demonstrating validation, conditional routing, persistence, and response generation.

```csharp
public async Task ProcessSupplierInvoice(SupplierInvoiceDto dto)
{
    var pipeline = new DataPipe<SupplierInvoiceMessage>();
    
    // Cross-cutting concerns
    pipeline.Use(new ExceptionAspect<SupplierInvoiceMessage>());
    pipeline.Use(new BasicLoggingAspect<SupplierInvoiceMessage>("SupplierInvoice"));
    
    // Validation
    pipeline.Add(new ValidateInvoiceFormat());
    pipeline.Add(new VerifySupplierExists());
    
    // Conditional routing based on validation
    pipeline.Add(new Policy<SupplierInvoiceMessage>(msg =>
    {
        if (!msg.IsValid)
            return new RejectInvoiceWithErrors();
        
        return msg.RequiresManualReview
            ? new RouteToApprovalQueue()
            : new OnTimeoutRetry<SupplierInvoiceMessage>(maxRetries: 2,
                new StartTransaction<SupplierInvoiceMessage>(
                    new OpenSqlConnection<SupplierInvoiceMessage>(connectionString: "...",                        
                        new CreateInvoiceRecord(),
                        new MatchToPurchaseOrder(),
                        new UpdateAccountsPayable()
                    )
                )
            );
    }));
    
    // Response generation
    pipeline.Add(new GenerateProcessingResponse());
    pipeline.Add(new NotifySupplier());
    
    var message = new SupplierInvoiceMessage { InvoiceDto = dto };
    await pipeline.Invoke(message);
}
```

## What this demonstrates

- Exception and logging aspects wrap everything
- Validation happens before routing decisions
- `Policy<T>` branches on message state
- Infrastructure boundaries are explicit and scoped
- Retry logic targets specific operations
- The entire workflow is readable top to bottom

This is production-grade orchestration with clear intent and no hidden behavior.
