# DataPipe

**Lightweight, flexible, and composable message pipeline framework for .NET**

DataPipe lets you orchestrate complex workflows in a simple, maintainable way. Build pipelines with reusable filters, apply cross-cutting concerns via aspects, and handle conditional logic, iterations, and retries all in a consistent, readable style. v3.0 has been updated to include new scoped filters for policy decsisions, async-ready retry logic, and SQL connection management (DataPipe.Sql), and more. All the while being lightweight and easy to learn.

Whether you're integrating with client systems, processing batches, building readable controller endpoints, or creating background services, DataPipe keeps your codebase clean and predictable. Spend less time wrestling with architecture and more time delivering functionality.

---

## Features

- **Composable Filters** chain together any number of filters to build workflows that are clear and modular.  
- **Aspects for Cross-Cutting Concerns** logging, exception handling, and more. Add your own too! 
- **Scoped Resource Management**: Open SQL connections, transactions, or other resources exactly where needed.  
- **Conditional Execution**: `Policy`, `IfTrue`, `RepeatUntil`, and `ForEach` filters make complex workflows readable. 
- **Async-Ready Retries** `OnTimeoutRetry` supports fully asynchronous retry logic with custom delay strategies.  
- **inherently concurrent** process multiple messages in parallel safely.  
- **Flow Control** stop pipeline execution safely with `PipelineExecution.Stop()`.  
- **Integration Friendly** works well with APIs, console apps, background services, WinForms, and more.  
- **Minimal Footprint** small, maintainable, and easy for teams to learn.  

DataPipe is **fully open-source** and ready to plug into your .NET projects today.

---

## Installation

Install via NuGet:

```bash
dotnet add package SCB.DataPipe
```

Or via the Package Manager Console:

```bash
Install-Package SCB.DataPipe
```

## Basic Usage

Start simple, grow as needed.

1. simple lambda filter:
```csharp
   pipe.Run(async m => { await Task.Delay(0); });
```

2. custom filter class to house more complex logic:
```csharp
    pipe.Run(new AddOrderHeader());
```

3. execute conditionally based on message state:
```csharp
    pipe.Run(
        new IfTrue<TestMessage>(msg => msg.Number > 0, new IncrementingNumberFilter())
    );
```

4. compose filters to group together or manage resources:
```csharp
    pipe.Run(new StartTransaction(
        new OpenSqlConnection(...)));
```


### Define a message type:

```csharp
public class TestMessage : BaseMessage
{
    public int Number { get; set; }
}
```

### Create a pipeline:

```csharp
var pipe = new DataPipe<TestMessage>();

// Add aspects
pipe.Use(new ExceptionAspect<TestMessage>());
pipe.Use(new BasicLoggingAspect<TestMessage>("TestPipeline"));

// Register filters
pipe.Run(new IncrementingNumberFilter());
pipe.Run(new Policy<TestMessage>(msg =>
{
    return msg.Number == 0 
	? new IncrementingNumberFilter()
	: new DecrementingNumberFilter();
}));

// Execute pipeline
var message = new TestMessage { Number = 0 };

await pipe.Invoke(message);
```

## Examples

See the `DataPipe.Tests` project for more examples and test cases.

### IfTrue:

```csharp
pipe.Run(
	new IfTrue<TestMessage>(msg => msg.Number > 0,
		new IncrementingNumberFilter()
	));
```

### Policy:

```csharp
pipe.Run(new Policy<TestMessage>(msg =>
{
    return msg.Number switch
    {
        0 => new IncrementingNumberFilter(),
        1 => new DecrementingNumberFilter(),
        _ => null
    };
}));
```

### Retry Example with OnTimeoutRetry

```csharp
pipe.Run(
	new OnTimeoutRetry<TestMessage>(maxRetries: 3,
		new MockHttpErroringFilter()
	));
```

## Flow Control

### Stop the pipeline gracefully:

```csharp
public class CancelFilter : Filter<TestMessage>
{
    public Task Execute(TestMessage msg)
    {
        msg.Execution.Stop("User requested cancellation");
        return Task.CompletedTask;
    }
}
```

## When to Use

DataPipe is ideal for:

- Complex API integrations
- Simplifying controller logic
- Batch processing pipelines
- Background services or scheduled tasks
- Scenarios requiring retries, conditional logic, or transaction scoping
- Teams looking to reduce boilerplate and improve maintainability

## Real-World Usage 

DataPipe has been successfully used since its inception more than a decade ago in production systems for:

- Processing incoming API requests with complex validation and transformation logic.
- Implementing background jobs that require retries and error handling.
- Managing stateful workflows in long-running processes.
- Simplifying data processing pipelines in ETL scenarios.

## An at-a-glance example

Below is a more realistic example pipeline for processing incoming orders from an external system, handling retries, 
conditional logic, and logging, all in a readable, composable structure.

```csharp

using DataPipe.Core;
using DataPipe.Core.Sql;
using System;
using System.Threading.Tasks;

// 1. Base infrastructure message - inherits the built-in BaseMessage
public class ClientContext : BaseMessage, ISqlCommand, IAmCommittable, IAmRetryable
{
    public SqlCommand SqlCommand { get; set; }
    public bool Commit { get; set; }
    public int Attempt { get; set; }
    public int MaxRetries { get; set; }
    public Action<int> OnRetrying { get; set; } = attempt => Console.WriteLine($"Retrying attempt {attempt}...");
}

// 2. Domain message inherits that infrastructure
public class OrderMessage : ClientContext
{
    public string OrderId { get; set; }
    public bool IsValid { get; set; }
    public bool RequiresSpecialProcessing { get; set; }
}

// 3. Filters
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

// 4. Pipeline
public async Task RunOrderPipeline()
{
    var msg = new OrderMessage
    {
        OrderId = "ORD123",
        RequiresSpecialProcessing = true,

        // Optional: override OnRetrying for custom behavior
        OnRetrying = attempt =>
        {
            Console.WriteLine($"Custom retry hook: attempt {attempt} for order ORD123");
        }
    };

    var pipeline = new DataPipe<OrderMessage>();

    // Cross-cutting concerns
    pipeline.Use(new BasicLoggingAspect<OrderMessage>());
    pipeline.Use(new ExceptionAspect<OrderMessage>());

    // Retryable filter: OnTimeoutRetry will automatically call msg.OnRetrying on each retry
    pipeline.Run(new OnTimeoutRetry<OrderMessage>(
        maxRetries: 3,
        new DownloadOrderFromClient<OrderMessage>(
            new ValidateOrder()
        )
    ));

    // Conditional processing
    pipeline.Run(new Policy<OrderMessage>(m =>
    {
        if (!m.IsValid) return new MarkOrderAsFailed();

        return m.RequiresSpecialProcessing
            ? new RaiseSpecialOrderHandlingRequiredNotification()
            : new OnTimeoutRetry<OrderMessage>(
                maxRetries: 3,
                new StartTransaction<OrderMessage>(
                    new OpenSqlConnection<OrderMessage>(
                        new ProcessStandardOrder()
                    )
                )
            );
    }));

    // Execute pipeline
    await pipeline.Invoke(msg);
}

```

## License
[MIT License](http://opensource.org/licenses/MIT)