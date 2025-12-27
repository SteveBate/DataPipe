# Sequential Filters

Filters execute in the exact order they are registered. There are no surprises, no hidden sorting, and no framework magic.

This example shows how to register multiple filters in two ways. Registering filters one at a time can improve readability for longer pipelines, while grouping related filters together can reduce visual noise for shorter pipelines. Choose the style that best fits your scenario.

## Execution order is explicit

Register filters one at a time for clarity:

```csharp
public async Task ProcessPayment(PaymentRequest request)
{
    var pipeline = new DataPipe<PaymentMessage>();
    
    pipeline.Add(new ValidatePaymentDetails());
    pipeline.Add(new CheckFraudRules());
    pipeline.Add(new ChargePaymentGateway());
    pipeline.Add(new RecordTransactionHistory());
    pipeline.Add(new SendConfirmationEmail());
    
    var message = new PaymentMessage { Request = request };
    await pipeline.Invoke(message);
}
```

Alternatively, register multiple filters in a single `Add` call for brevity:

```csharp
public async Task ProcessPayment(PaymentRequest request)
{
    var pipeline = new DataPipe<PaymentMessage>();
    
    pipeline.Add(
        new ValidatePaymentDetails(),
        new CheckFraudRules(),
        new ChargePaymentGateway(),
        new RecordTransactionHistory(),
        new SendConfirmationEmail());
    
    var message = new PaymentMessage { Request = request };
    await pipeline.Invoke(message);
}
```

## Reading the pipeline

Top to bottom. That's it.

Each filter executes only after the previous one completes. If a filter stops execution using `msg.Execution.Stop()`, subsequent filters are skipped.

This is intentional. The code describes what happens, in order, without requiring you to trace through middleware stacks or event handlers.
