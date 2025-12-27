# Conditional Registration with RunIf

`RunIf` conditionally registers filters when the pipeline is constructed, not when the message flows through.

This is useful for environment-specific behavior, feature flags, or configuration-driven pipelines.

## Register based on environment

```csharp
public DataPipe<OrderMessage> BuildOrderPipeline()
{
    var pipeline = new DataPipe<OrderMessage>();
    var env = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
    
    pipeline.Add(new ValidateOrder());
    
    // Only register in UAT
    pipeline.AddIf(
        condition: env == "UAT",
        new SimulatePaymentGatewayDelay()
    );
    
    // Only register in Production
    pipeline.AddIf(
        condition: env == "Production",
        new EnableFraudDetection()
    );
    
    pipeline.Add(new ProcessPayment());
    
    return pipeline;
}
```

## Alternative: RunIf with else

```csharp
public DataPipe<OrderMessage> BuildOrderPipeline()
{
    var pipeline = new DataPipe<OrderMessage>();
    var isProd = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") == "Production";
    
    pipeline.Add(new ValidateOrder());
    
    // Register one filter OR the other
    pipeline.AddIf(
        condition: isProd,
        ifTrue: new CallRealPaymentGateway(),
        ifFalse: new CallMockPaymentGateway()
    );
    
    pipeline.Add(new RecordTransaction());
    
    return pipeline;
}
```

## RunIf vs IfTrue

- `RunIf` evaluates at **pipeline construction** — the filter is added or not
- `IfTrue<T>` evaluates at **message execution** — the filter is registered but may skip

Use `RunIf` for configuration, environment checks, or feature flags. Use `IfTrue<T>` for message-specific branching.
