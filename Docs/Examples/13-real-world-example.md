# Real-World Example

Below is a more complete example showing validation, policy-driven routing, retries, and transactional composition.

```csharp
public async Task ProcessSupplierInvoice(SupplierInvoiceDto dto)
{
    var env = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Development";
    var policy = new MinimumDurationPolicy(50);
    var adapter = new SqlServerTelemetryAdapter(TelemetryMode.PipelineAndErrors, AppSettings.Instance.TelemetryDb, policy);
    
    // build the pipeline
    var pipeline = new DataPipe<SupplierInvoiceMessage> { Name = "SupplierInvoice", TelemetryMode = TelemetryMode.PipelineAndFilters };    
    pipeline.Use(new ExceptionAspect<SupplierInvoiceMessage>());
    pipeline.Use(new SinkLoggingAspect<SupplierInvoiceMessage>(logger, "SupplierInvoice", env));
    pipeline.Use(new TelemetryAspect<SupplierInvoiceMessage>(adapter));
    pipeline.Add(new ValidateInvoiceFormat());
    pipeline.Add(new VerifySupplierExists());
    // Decide how to process the invoice based on validation and business rules
    pipeline.Add(new Policy<SupplierInvoiceMessage>(msg =>
    {
        if (!msg.IsValid) return new RejectInvoiceWithErrors();
        
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
