# DataPipe

DataPipe is a **lightweight, composable message pipeline framework for .NET**.  
It lets you express application use-cases as explicit, composable pipelines built from small filters and aspects, keeping behavior readable and intentional.  
Designed for real production systems, DataPipe has been refined over more than a decade of use.

## Why DataPipe?

Many frameworks rely on hidden middleware, global handlers, or opaque execution paths that make behavior hard to reason about. DataPipe takes the opposite approach:

- **Explicit control flow** - execution order is visible and intentional  
- **Composability** - build complex workflows from small, focused filters  
- **Clear cross-cutting behavior** - logging, retries, telemetry and policies are applied explicitly via aspects  
- **No hidden middleware, no surprises**

Unlike many recent frameworks, DataPipe did *not* spring from a marketing cycle or a buzzword trend. I have been experimenting with the core ideas behind it - explicit message handling and composable execution - in real systems since at least **2013**, long before today’s discussions about AI or cloud observability. My original article exploring **messaging as a programming model** is still there to read:

> *Messaging as a Programming Model (2013)*  
> https://stevebate.wordpress.com/2013/08/12/messaging-as-a-programming-model-part-1/

That implementation was simple and in truth, a bit naive - but the principles it embodied are recognizable in DataPipe today. What’s changed in the last decade is not the *ideas*, but the *experience*, refinement, and operational knowledge encoded into the design. DataPipe has been used in production systems for more than 10 years, and has evolved to meet the needs of real teams building real applications.

---

## Vertical Slice Architecture

DataPipe aligns naturally with **vertical slice architecture**, where:
- Each feature owns its own pipeline
- Behavior is local and explicit
- Observability and cross-cutting concerns are composed by choice, not by accident

This makes DataPipe a great choice for teams that value clarity, testability, and operational transparency.

### Example: Authorize a user

There are a few things going on in the example below, but they’re all explicit, intentional and highlight important DataPipe concepts:

- The `SuppressAllExceptErrorsPolicy` policy ensures we only capture errors in production for this pipeline
- `SqlServerTelemetryAdapter` sends telemetry to SQL Server. This is a custom adapter that implements `ITelemetryAdapter` (see Docs/Patterns/Telemetry/Adapters for more)
- The pipeline is named "Authorize" (for logging) and is configured to capture pipeline start/stop events and errors
- An `ExceptionAspect` captures any unhandled exceptions to ensure the pipeline fails gracefully
- A `LoggingAspect` logs every step of the pipeline to a configured sink (console, file, etc) with environment context and can be used with any library that supports `ILogger`
- The `TelemetryAspect` is conditionally applied only in non-development environments and forwards events to the adapter, which in this case is SQL Server, and is governed by the policy defined earlier i.e only errors are captured
- `OnTimeoutRetry` is a structural filter that scopes retries to the inner filters only 
- `OpenSqlConnection` is another structural filter that opens a SQL connection to a database ensuring the connection is available for the inner filters then automatically disposes it afterwards
- `GetUserClaims` is a business logic filter that retrieves user claims from the database by way of a `SqlCommand` object available on the message (the `IUseSqlCommand` interface implementation)

The pipeline is invoked asynchronously with the passed in `AuthorizeMessage` message and is routed through each aspect and filter in order. At the end of the pipeline, the message has been fully processed, mutated, and enriched as needed.

```csharp
// Build an authorization pipeline
public async Task Authorize(AuthorizeMessage msg)
{
    var env = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Development";
    var policy = new SuppressAllExceptErrorsPolicy("Authorize");
    var adapter = new SqlServerTelemetryAdapter(TelemetryMode.PipelineAndErrors, AppSettings.Instance.TelemetryDb, policy);

    var pipe = new DataPipe<AuthorizeMessage> { Name = "Authorize", TelemetryMode = TelemetryMode.PipelineAndErrors };
    pipe.Use(new ExceptionAspect<AuthorizeMessage>());
    pipe.Use(new LoggingAspect<AuthorizeMessage>(logger, "Authorize Request", env));
    pipe.UseIf(env != "Development", new TelemetryAspect<AuthorizeMessage>(adapter));
    pipe.Add(
        new OnTimeoutRetry<AuthorizeMessage>(maxRetries: 3,
            new OpenSqlConnection<AuthorizeMessage>(connectionString: "Data Source=...",
                new GetUserClaims())));

    await pipe.Invoke(msg);
}
```

Everything that happens in this slice:

- Is explicit
- Is ordered
- Can be reasoned about in isolation

There’s no global behavior to discover or debug.

---

## Cross-Cutting Concerns via Aspects

Cross-cutting behaviors like logging, retries, metrics, and telemetry are applied explicitly using aspects:

```csharp
var adapter = new FileLoggingTelemetryAdapter(TelemetryMode.PipelineAndFilters, connectionString);
pipe.Use(new BasicConsoleLoggingAspect());
pipe.Use(new TelemetryAspect(fileAdapter));
```

Or conditionally, for example based on environment:

```csharp
var adapter = new JsonConsoleTelemetryAdapter(new MinimumDurationPolicy(50));
pipe.UseIf(isDevelopment, new TelemetryAspect<OrderMessage>(adapter));
```

This allows:

- Different behavior per pipeline
- Different behavior per environment
- Clear and safe evolution of operational concerns

---

## Observability You Control

DataPipe does not assume:

- A cloud provider
- A subscription or hosted telemetry service
- A vendor-defined data model

Instead:

- Pipelines emit telemetry events
- Policies shape what is captured
- Adapters decide where it goes

Out of the box you can:

- Capture business events only
- Capture structural events only
- Capture errors only
- Capture everything in development
- Capture minimal telemetry in production

And you can send telemetry to:

- SQL Server
- File systems
- OpenTelemetry

Or skip telemetry entirely

This gives teams application insights without external dependencies, cost, or lock-in.

---

## Key Features

- Composable filters for clear workflows
- Aspects for cross-cutting behavior
- Conditional pipeline construction (AddIf, UseIf)
- Conditional runtime behavior (IfTrue, Policy, etc)
- Policy-driven telemetry
- Async-first design
- Minimal footprint, zero hidden magic
- Works in ANY .NET host: ASP.NET, console apps, services, and more

---

## Getting Started

Install from NuGet:
```bash
dotnet add package SCB.DataPipe
```

Explore the examples and tests in the repo for real usage patterns. These are all used and tested in production systems right now.

---

## Design Philosophy

DataPipe is intentionally:

- Small and focused
- Business-first
- Clear and maintainable

It orchestrates in-process business workflows - not queues, not middleware, not frameworks. Pipelines describe behavior as code that a team can read and reason about months or years later.

---

## What DataPipe Is Not

DataPipe deliberately does not attempt to be:

- A message bus or mediator
- A workflow engine
- A rules engine
- A scheduler
- A parallel execution framework

Its goal is simple and powerful: describe what happens to a message, step by step, in a way that remains clear and intentional.

---

## Learn More

See Docs for real sample pipelines and patterns.

---

## License
[MIT License](http://opensource.org/licenses/MIT)