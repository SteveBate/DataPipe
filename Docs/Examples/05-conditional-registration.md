# Conditional Registration with UseIf and AddIf

`UseIf` conditionally registers aspects when the pipeline is constructed, not when the message flows through.
`AddIf` conditionally registers filters when the pipeline is constructed, not when the message flows through.

This is useful for environment-specific behavior, feature flags, or configuration-driven pipelines.

UseIf follows the same rules as AddIf, but applies to aspects instead of filters.

## Register based on environment

```csharp
public DataPipe<OrderMessage> BuildOrderPipeline()
{
    var env = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
    var pipeline = new DataPipe<OrderMessage>();
    pipeline.UseIf(env == "Development", new BasicConsoleLoggingAspect<OrderMessage>());
    pipeline.UseIf(env != "Development", new DatabaseLoggingAspect<OrderMessage>());
    
    // always part of the pipeline
    pipeline.Add(new ValidateOrder());
    
    // only part of the pipeline in Test
    pipeline.AddIf(condition: env == "Test", new SimulatePaymentGatewayDelay());
    
    // Only register in Production
    pipeline.AddIf(condition: env == "Production", new EnableFraudDetection());
    
    // always part of the pipeline
    pipeline.Add(new ProcessPayment());
    
    return pipeline;
}
```

## Alternative: AddIf with an 'else'

```csharp
public DataPipe<OrderMessage> BuildOrderPipeline()
{
    var isProd = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") == "Production";
    var pipeline = new DataPipe<OrderMessage>();
    
    // always part of the pipeline
    pipeline.Add(new ValidateOrder());
    
    // Register one filter OR the other
    pipeline.AddIf(
        condition: isProd,
        ifTrue: new CallRealPaymentGateway(),
        ifFalse: new CallMockPaymentGateway()
    );
    
    // always part of the pipeline
    pipeline.Add(new RecordTransaction());
    
    return pipeline;
}
```

## AddIf vs IfTrue

- `AddIf` evaluates at **pipeline construction** - the filter is added or not
- `IfTrue<T>` evaluates at **message execution** - the filter is registered but may skip

You can use either approach depending on your needs, but using AddIf for environment or configuration concerns helps make intent clearer. To summarize:

Use `AddIf` for configuration, environment checks, or feature flags. Use `IfTrue<T>` for message-specific branching.
