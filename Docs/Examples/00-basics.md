# Basics of DataPipe

DataPipe is a lightweight library for building in-memory processing pipelines in .NET applications. It enables you to define a sequence of processing steps (filters) that operate on messages as they flow through the pipeline. This document shows how to start creating messages and pipelines for your applications.

## The BaseMessage class

Messages in DataPipe are mutable data carriers that flow through pipelines. This is by design to keep things simple and efficient.

Every message inherits from the built-in `BaseMessage` class, which provides logging and error handling hooks. While you can create custom message types by direcrtly inheriting from `BaseMessage`, it's best to extend it with a custom base class that has meaning for your domain, and then extend that for specific pipelines.

```csharp
public class SalesContext : BaseMessage
{
    // ... common properties for a sales domain
}
```

If you want to use infrastructure features like retries, transactions, or sql connections, your custom 'context' class should implement the relevant interfaces such as `ISqlCommand`, `IAmRetryable`, `IAmCommittable`, etc. You can add your own interfaces for custom behaviors as needed. 

See DataPipe.Sql and DataPipe.EntiryFramework for ready-made filters that work with these interfaces.

```csharp
public class SalesContext : BaseMessage, ISqlCommand, IAmCommittable, IAmRetryable
{
    // ... common properties for a sales domain

    // ISqlCommand
    public SqlCommand Command { get; set; }

    // IAmCommittable
    public bool Commit { get; set; }

    // IAmRetryable
    public int Attempt { get; set; }
    public int MaxRetries { get; set; }
    public Action<int> OnRetrying { get; set; } = attempt => Console.WriteLine($"Retrying attempt {attempt}...");
}
```

## Custom message types

Now you're set to begin thinking in terms of business process flows. Create message types that represent specific operations in your domain by extending your custom base message class.

```csharp
public class OrderMessage : SalesContext
{
    public string OrderId { get; set; }
    public bool IsValid { get; set; }
    public bool RequiresSpecialProcessing { get; set; }
}
```

With your message types defined, you can now create a series of filters, each responsible for a specific task in the workflow.

### Filters

A pipeline is made up of filters that process messages. You define the filters that meet the business use-case for (in this example) order processing. Each filter is stateless and focuses on a single responsibility. This allows for easy testing, reuse, re-ordering, and composition.

```csharp
public class ValidateOrder : Filter<OrderMessage>
{
    public async Task Execute(OrderMessage msg)
    {
        msg.IsValid = !string.IsNullOrEmpty(msg.OrderId);
        msg.OnLog?.Invoke($"Order {msg.OrderId} validation: {msg.IsValid}");
        await Task.CompletedTask;
    }
}

public class ProcessStandardOrder : Filter<OrderMessage>
{
    public async Task Execute(OrderMessage msg)
    {
        msg.OnLog?.Invoke($"Processing standard order {msg.OrderId}");

        // msg.Command.SqlCommandText = "...";
        // Execute command...
        
        await Task.CompletedTask;
    }
}

public classs AddOrderCreatedOutboxEvent : Filter<OrderMessage>
{
    public async Task Execute(OrderMessage msg)
    {
        msg.OnLog?.Invoke($"Adding order created event to outbox for order {msg.OrderId}");
        await Task.CompletedTask;
    }
}

public class MarkOrderAsFailed : Filter<OrderMessage>
{
    public async Task Execute(OrderMessage msg)
    {
        msg.OnLog?.Invoke($"Order {msg.OrderId} marked as failed.");
        await Task.CompletedTask;
    }
}
```

## Aspects

Aspects are cross-cutting concerns that can be applied to pipelines. Common aspects include logging, exception handling, and performance monitoring. You can create custom aspects by implementing the `IAspect<T>` interface.

In general, you will always want to add at least the ExceptionAspect to your pipelines to ensure unhandled exceptions are caught so as not to crash your application. You could also create your own, for example, to write exceptions to a monitoring system, etc.

## Creating and invoking a pipeline

With your message types and filters defined, you can now compose a pipeline that processes messages through these filters in sequence. The following example demonstrates a pipeline that processes an `OrderMessage`, applying aspects for exception handling and logging, handling transient network errors with retries, and using conditional logic to determine the processing path based on message properties.

```csharp
    var pipeline = new DataPipe<OrderMessage>();
    pipeline.Use(new ExceptionAspect<OrderMessage>());
    pipeline.Use(new BasicLoggingAspect<OrderMessage>());
    pipeline.Add(
        new OnTimeoutRetry<OrderMessage>(maxRetries: 3,
            new DownloadOrderFromClient<OrderMessage>(
                new ValidateOrder()
            )));

    // Conditional processing
    pipeline.Add(new Policy<OrderMessage>(m =>
    {
        if (!m.IsValid) return new MarkOrderAsFailed();

        return m.RequiresSpecialProcessing
            ? new RaiseSpecialOrderHandlingRequiredNotification()
            : new OnTimeoutRetry<OrderMessage>(maxRetries: 3,
                new StartTransaction<OrderMessage>(
                    new OpenSqlConnection<OrderMessage>(
                        new ProcessStandardOrder(),
                        new AddOrderCreatedOutboxEvent()
                    )
                )
            );
    }));

    // Execute pipeline
    var msg = new OrderMessage { OrderId = "ORD123", RequiresSpecialProcessing = true };
    await pipeline.Invoke(msg);
    return Ok(msg.Result);
```

When you call invoke, the message flows through each filter in the order they were added. Each filter can modify the message, perform actions, and log information as needed. The exact path taken through the pipeline can vary based on message properties and conditions defined in the filters and tested by filters such as Policy and IfTrue, etc. At the end of execution, the message reflects the final state after all processing steps have been applied. But where does the Result property come from in this example?

## Implementing the Result pattern

Many applications benefit from a standardized way to represent the outcome of operations. The Result pattern encapsulates success and failure states, along with any relevant data or error messages. Implementing this pattern in your DataPipe messages can enhance clarity and consistency across your pipelines.

The way to implement the Result pattern in DataPipe is to create a generic subclass of your custom base message class that includes a strongly typed Result property. This allows each message to carry its own result information, making it easy to check the outcome of operations as messages flow through the pipeline.

First,create a base result class that encapsulates common result properties and behaviors.

```csharp
public class CommonResult
{
    public bool Success { get; set; } = true;
    public string StatusMessage { get; set; } = string.Empty;
    internal int StatusCode { get; set; } = 200;
}
```

Then create a generic subclass of your custom base message class that includes a strongly typed Result property which itself must decend from CommonResult.

```csharp
// Generic version to provide strongly typed Result
public class SalesContext<TResult> : SalesContext where TResult : CommonResult, new()
{
    private TResult _result = new TResult();

    public override TResult Result => _result;

    public SalesContext() { }
}
```

Now, our oringal OrderMessage example can be updated to inherit from SalesContext with a specific Result type.

```csharp
public class OrderResult : CommonResult
{
    public string OrderReferenceNumber { get; set; }
    public string PlacedBy { get; set; }
    public decimal TotalAmount { get; set; }
    public DateTime ProcessedAt { get; set; }
}

// OrderMessage type with strongly typed Result
public class OrderMessage : SalesContext<OrderResult>
{
    public string OrderId { get; set; }
    public bool IsValid { get; set; }
    public bool RequiresSpecialProcessing { get; set; }
}
```

The common properties in CommonResult provide a consistent way to indicate success or failure in every pipeline. Filters are free to set the status code or StatusMessage, etc. as needed. The strongly typed Result property in OrderMessage allows filters to set order-specific result data, such as OrderReferenceNumber and TotalAmount.

**Note:** Any custom aspects would continue to subclass the non-generic SalesContext to ensure they can operate on all messages regardless of their specific Result type.