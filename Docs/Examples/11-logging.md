# Logging with SinkLoggingAspect

The `SinkLoggingAspect` provides structured logging for your pipelines using the standard `ILogger` interface. This means it works with any logging library that supports .NET's logging abstractions - Serilog, NLog, Application Insights, or any other provider.

## Logging vs Telemetry

In DataPipe, **logging** captures human-readable operational events and debugging information, while **telemetry** tracks metrics and system diagnostics. 

The `SinkLoggingAspect` includes an `IsTelemetry = false` scope flag by default, allowing you to filter telemetry data separately from application logs. This keeps your log storage lean and your dashboards focused.

```csharp
// Your logging configuration can exclude telemetry
// while capturing all application logs
var logger = LoggerFactory.CreateLogger("DataPipe");
pipeline.Use(new SinkLoggingAspect<OrderMessage>(
    logger, 
    title: "OrderProcessing",
    mode: PipeLineLogMode.Full
));
```

## Constructor Parameters

- **logger** (required): The `ILogger` instance to write to. This is typically injected via dependency injection.
- **title** (optional, default: message type name): A friendly name for the pipeline. If not provided, uses the message class name.
- **env** (optional, default: "Development"): The environment name added to the log scope. Useful for distinguishing logs from different deployments.
- **startEndLevel** (optional, default: `LogLevel.Information`): The log level for START/END messages and the default level for all logs.
- **mode** (optional, default: `PipeLineLogMode.Full`): Controls logging verbosity:
  - `Full`: Logs start, all steps via `msg.OnLog?.Invoke()`, end, and errors
  - `StartEndOnly`: Logs only pipeline start, end, and errors
  - `ErrorsOnly`: Logs only exceptions

## Log Scope

Every log entry includes structured scope data:

```
Environment: Development
PipelineName: OrderMessage  
CorrelationId: {correlation-id}
Tag: {custom-tag}
IsTelemetry: false
```

The `Tag` property on your message allows you to attach custom context that will appear in all logs for that pipeline execution:

```csharp
var message = new OrderMessage { OrderId = "12345" };
message.Tag = "batch_import_2024_Q1";  // Appears in all logs for this execution
await pipeline.Invoke(message);
```

## Basic Usage

```csharp
public async Task ProcessOrder(OrderMessage order, ILogger logger)
{
    var pipeline = new DataPipe<OrderMessage>();
    
    // Add the logging aspect
    pipeline.Use(new SinkLoggingAspect<OrderMessage>(
        logger,
        title: "OrderProcessing",
        env: "Production",
        startEndLevel: LogLevel.Information,
        mode: PipeLineLogMode.Full
    ));
    
    // Your business logic
    pipeline.Add(new ValidateOrder());
    pipeline.Add(new ChargePaymentMethod());
    pipeline.Add(new ShipOrder());
    
    // When invoked, all pipeline activity is logged
    await pipeline.Invoke(order);
}
```

Output:
```
[Production] [12345] [OrderMessage] START: OrderProcessing
[Production] [12345] [OrderMessage] Processing standard order 12345
[Production] [12345] [OrderMessage] END: OrderProcessing
```

## Serilog Integration

Serilog is a popular structured logging library for .NET. Configure it to respect DataPipe's logging scope and filter out telemetry data:

```json
{
  "Serilog": {
    "Using": [ "Serilog.Sinks.Console", "Serilog.Expressions" ],
    "MinimumLevel": "Debug",
    "WriteTo": [
      {
        "Name": "Logger",
        "Args": {
          "configureLogger": {
            "Filter": [
              {
                "Name": "ByExcluding",
                "Args": { "expression": "IsTelemetry = true" }
              }
            ],
            "WriteTo": [
              {
                "Name": "Console",
                "Args": { 
                  "outputTemplate": "[{Environment}] [{CorrelationId}] [{PipelineName}] {Message:lj}{NewLine}{Exception}"
                }
              },
              {
                "Name": "File",
                "Args": {
                  "path": "logs/datapipe-.txt",
                  "rollingInterval": "Day",
                  "outputTemplate": "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Environment}] [{CorrelationId}] [{PipelineName}] [{Level:u3}] {Message:lj}{NewLine}{Exception}"
                }
              }
            ]
          }
        }
      }
    ]
  }
}
```

Then initialize Serilog in your application startup:

```csharp
var logger = new LoggerConfiguration()
    .ReadFrom.Configuration(configuration)
    .CreateLogger();

using var serviceProvider = new ServiceCollection()
    .AddLogging(config => config.AddSerilog(logger))
    .BuildServiceProvider();
```

## Controlling Verbosity

Use the `mode` parameter to adjust logging output based on your needs:

```csharp
// Development: verbose logging for debugging
pipeline.Use(new SinkLoggingAspect<OrderMessage>(
    logger,
    mode: PipeLineLogMode.Full
));

// Production: minimal overhead, errors only
pipeline.Use(new SinkLoggingAspect<OrderMessage>(
    logger,
    mode: PipeLineLogMode.ErrorsOnly
));

// Monitoring: start and end only
pipeline.Use(new SinkLoggingAspect<OrderMessage>(
    logger,
    mode: PipeLineLogMode.StartEndOnly
));
```

## Manual Logging from Filters

You can still emit custom logs from within your filters using the `OnLog` event on the message:

```csharp
public class ValidateOrder : Filter<OrderMessage>
{
    public async Task Execute(OrderMessage msg)
    {
        var isValid = !string.IsNullOrEmpty(msg.OrderId);
        
        if (isValid)
        {
            msg.OnLog?.Invoke($"Order {msg.OrderId} passed validation");
        }
        else
        {
            msg.OnLog?.Invoke($"Order validation failed: OrderId is empty");
        }
        
        msg.IsValid = isValid;
        await Task.CompletedTask;
    }
}
```

When `mode` is set to `Full`, these manual logs are automatically captured with the same scope and context as the aspect's START/END messages.

## Real-World Example

```csharp
public async Task ProcessSupplierInvoice(SupplierInvoiceDto dto, ILogger logger)
{
    var pipeline = new DataPipe<SupplierInvoiceMessage>();
    
    // Exception handling first
    pipeline.Use(new ExceptionAspect<SupplierInvoiceMessage>());
    
    // Then add logging with structured context
    pipeline.Use(new SinkLoggingAspect<SupplierInvoiceMessage>(
        logger,
        title: "SupplierInvoiceProcessing",
        env: Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Development",
        startEndLevel: LogLevel.Information,
        mode: PipeLineLogMode.Full
    ));
    
    pipeline.Add(new ValidateInvoiceFormat());
    pipeline.Add(new VerifySupplierExists());
    pipeline.Add(new OnTimeoutRetry<SupplierInvoiceMessage>(maxRetries: 2,
        new StartTransaction<SupplierInvoiceMessage>(
            new SaveInvoiceToDatabase()
        )
    ));
    pipeline.Add(new GenerateAcknowledgment());
    
    var message = new SupplierInvoiceMessage { InvoiceDto = dto };
    message.Tag = $"batch_{DateTime.UtcNow:yyyyMMdd_HHmmss}";
    
    await pipeline.Invoke(message);
}
```

This produces structured, queryable logs that include all the context needed for debugging, monitoring, and auditing - without requiring logging code scattered throughout your business logic.
