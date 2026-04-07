# DataPipe

DataPipe is a lightweight, composable message pipeline framework for .NET.

It is designed for teams building enterprise systems with vertical slices, where each use-case is explicit, testable, and easy to evolve over time.

Instead of spreading behavior across controllers, handlers, decorators, middleware, and hidden framework hooks, DataPipe keeps a feature in one readable flow:

- Message
- Ordered filters
- Optional aspects for cross-cutting behavior

No hidden middleware. No global magic. No surprises. It's a mini-DSL for execution flow that works in any domain.

---

## Why DataPipe

DataPipe is a practical alternative to heavy layered "clean architecture" implementations when your real goal is maintainable business behavior.

With DataPipe + vertical slices:

- Each feature owns its own pipeline
- Control flow is explicit in code order
- Cross-cutting concerns are opt-in and local
- Changes happen by composition, not by framework gymnastics

The result is code that is self-documenting:

- You can read the pipeline top-to-bottom and understand the use-case
- You can see where validation, permission checks, retries, and transactions happen
- You can reason about behavior without hunting through global registrations

Unlike many recent frameworks, DataPipe did *not* spring from a marketing cycle or a buzzword trend. I have been experimenting with the core ideas behind it - explicit message handling and composable execution in real systems since at least **2013**, long before today’s discussions about AI or cloud observability. My original blog exploring **messaging as a programming model** is still there to read:

> *Messaging as a Programming Model (2013)*  
> https://stevebate.wordpress.com/2013/08/12/messaging-as-a-programming-model-part-1/

That implementation was simple and in truth, a bit naive - but the principles it embodied are recognizable in DataPipe today. What’s changed in the last decade is not the *ideas*, but the *experience*, refinement, and operational knowledge encoded into the design. DataPipe has been used in production systems in one form or another for more than 13 years, and has evolved to meet the needs of real teams building real applications.

---

## SOLID-Friendly by Construction

DataPipe naturally supports SOLID principles in day-to-day enterprise work:

- Single Responsibility: each filter does one thing well
- Open/Closed: extend behavior by adding/reordering filters, not rewriting existing ones
- Liskov Substitution: filters share a consistent contract and are swappable
- Interface Segregation: messages opt into small contracts only when needed (`IUseSqlCommand`, `IAmCommittable`, `IAmRetryable`)
- Dependency Inversion: business flows depend on abstractions (`Filter<T>`, aspects, adapters), not monolithic frameworks

---

## Enterprise Resilience Without Heavy Dependencies

DataPipe includes first-class structural filters for resilience and control:

- `OnTimeoutRetry` for automatic customizable retries
- `OnCircuitBreak` for fail-fast protection when dependencies are unhealthy
- `OnRateLimit` for backpressure or rejection strategies
- `IfTrue` and `Policy` for conditional behavior and dynamic routing
- `OpenSqlConnection` and `StartTransaction` for explicit database scoping
- `ForEach` for sequential processing over child messages
- `ParallelForEach` for fan-out and concurrent execution

You get robust behavior without pulling in a stack of large third-party frameworks.

---

## Walkthrough: From Simple to Powerful

Start with one question: what is the smallest useful slice?

### 1. One message, one filter

A message carries the data a slice needs. Name it after what it represents.

```csharp
public sealed class RegisterCustomerMessage : BaseMessage
{
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
}
```

A filter performs a single operation on that message.

```csharp
public sealed class ValidateCustomerInput : Filter<RegisterCustomerMessage>
{
    public Task Execute(RegisterCustomerMessage msg)
    {
        if (string.IsNullOrWhiteSpace(msg.Name))
            msg.Execution.Stop("Name is required.");

        if (string.IsNullOrWhiteSpace(msg.Email))
            msg.Execution.Stop("Email is required.");    

        if (!msg.Email.Contains('@'))
            msg.Execution.Stop("A valid email address is required.");

        return Task.CompletedTask;
    }
}
```

We can then construct a pipeline for that message type with that filter and invoke it:

```csharp
var pipe = new DataPipe<RegisterCustomerMessage>();
pipe.Add(new ValidateCustomerInput());
await pipe.Invoke(message);
```

That is the core mental model: a message flows through filters, each one doing exactly one thing.

### 2. Need another step? Add another filter

```csharp
public sealed class NormalizeEmail : Filter<RegisterCustomerMessage>
{
    public Task Execute(RegisterCustomerMessage msg)
    {
        msg.Email = msg.Email.Trim().ToLowerInvariant();
        return Task.CompletedTask;
    }
}
```

Choose the best place within the flow to insert it.

```csharp
pipe.Add(new ValidateCustomerInput());
pipe.Add(new NormalizeEmail());
```

Filters run in order. If `ValidateCustomerInput` stops the pipeline, no further filters execute. No ceremony. Just clear behavior.

**Note** that DataPipe is fully async/await friendly. The two filters above are simple and synchronous and therefore return `Task.CompletedTask`. In the next sections, we will see filters that perform asynchronous work and return real tasks.

### 3. Need to persist the customer? Introduce SQL

Up to now the message held only business data. But to write to a database, a filter needs a `SqlCommand`*. Where does that come from?

`OpenSqlConnection` opens a connection, creates a `SqlCommand`, and assigns it to the message automatically. To receive it, the message opts into the `IUseSqlCommand` interface which is found in the `SCB.DataPipe.Sql` package:

```csharp
public sealed class RegisterCustomerMessage : BaseMessage, IUseSqlCommand
{
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;

    // Added for OpenSqlConnection - provides the SqlCommand to filters
    public SqlCommand Command { get; set; } = default!;
}
```

Now filters can use `msg.Command` inside an `OpenSqlConnection` scope:

```csharp
public sealed class InsertCustomer : Filter<RegisterCustomerMessage>
{
    public async Task Execute(RegisterCustomerMessage msg)
    {
        msg.Command.CommandText = "INSERT INTO Customers (Name, Email) VALUES (@name, @email)";
        msg.Command.Parameters.Clear();
        msg.Command.Parameters.AddWithValue("@name", msg.Name);
        msg.Command.Parameters.AddWithValue("@email", msg.Email);
        await msg.Command.ExecuteNonQueryAsync(msg.CancellationToken);
    }
}
```

Again, we insert the new filter at the right place in the flow but now scoped to the connection:

```csharp
pipe.Add(new NormalizeEmail());
pipe.Add(new ValidateCustomerInput());
pipe.Add(
    new OpenSqlConnection<RegisterCustomerMessage>(connectionString,
        new InsertCustomer()));
```

\*`IUseSqlCommand` is optional and geared toward adhoc sql or stored procedures.

DataPipe is equally at home with other data access approaches including Entity Framework but is not built in to avoid package dependencies. If you prefer Entity Framework or another data access approach, model your filters around that instead. See the `Docs/Patterns` directory for examples you can drop straight in to your project.

### 4. Need atomic writes? Add `StartTransaction`

Suppose we now also need to record an audit trail when a customer is registered:

```csharp
public sealed class InsertRegistrationAudit : Filter<RegisterCustomerMessage>
{
    public async Task Execute(RegisterCustomerMessage msg)
    {
        msg.Command.CommandText = "INSERT INTO RegistrationAudit (Email, RegisteredAtUtc) VALUES (@email, SYSUTCDATETIME())";
        msg.Command.Parameters.Clear();
        msg.Command.Parameters.AddWithValue("@email", msg.Email);
        await msg.Command.ExecuteNonQueryAsync(msg.CancellationToken);
    }
}
```

Two writes should succeed or fail together. `StartTransaction` provides that, and requires the message to implement `IAmCommittable`:

```csharp
public sealed class RegisterCustomerMessage : BaseMessage, IUseSqlCommand, IAmCommittable
{
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public SqlCommand Command { get; set; } = default!;

    // Added for StartTransaction - controls whether the transaction commits
    public bool Commit { get; set; } = true;
}
```

```csharp
pipe.Add(new NormalizeEmail());
pipe.Add(new ValidateCustomerInput());
pipe.Add(
    new StartTransaction<RegisterCustomerMessage>(
        new OpenSqlConnection<RegisterCustomerMessage>(connectionString,
            new InsertCustomer(),
            new InsertRegistrationAudit())));
```

Both writes share the same connection and transaction. If either filter fails, the transaction rolls back automatically.

As you can see we are building our business use-case from inside out, composing the behavior we need step by step. The result is a clear, readable slice that does exactly what we want.


### 5. Need automatic retries? Add `OnTimeoutRetry`

Database connections time out. Networks hiccup. `OnTimeoutRetry` wraps filters and retries them on transient failures. It requires the message to implement `IAmRetryable`, so we go back and add it:

```csharp
public sealed class RegisterCustomerMessage : BaseMessage, IUseSqlCommand, IAmCommittable, IAmRetryable
{
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public SqlCommand Command { get; set; } = default!;
    public bool Commit { get; set; } = true;

    // Added for OnTimeoutRetry - tracks retry state
    public int Attempt { get; set; }
    public int MaxRetries { get; set; }
    public Action<int> OnRetrying { get; set; } = _ => { };
}
```

We wrap the transaction and its writes inside the retry filter:

```csharp
pipe.Add(new NormalizeEmail());
pipe.Add(new ValidateCustomerInput());
pipe.Add(
    new OnTimeoutRetry<RegisterCustomerMessage>(maxRetries: 3,
        new StartTransaction<RegisterCustomerMessage>(
            new OpenSqlConnection<RegisterCustomerMessage>(connectionString,
                new InsertCustomer(),
                new InsertRegistrationAudit()))));
```

If the transaction fails with a transient error, `OnTimeoutRetry` will retry up to 3 times with a sliding delay ensuring a new transaction is started each time. No external library required.

### 6. Need fail-fast protection? Add `OnCircuitBreak`

If the database is down, retrying every request wastes time and resources. `OnCircuitBreak` monitors failures and trips open after a threshold, causing subsequent requests to fail fast until the dependency recovers.

We wrap it around the retry:

```csharp
var circuit = new CircuitBreakerState();

pipe.Add(new NormalizeEmail());
pipe.Add(new ValidateCustomerInput());
pipe.Add(
    new OnCircuitBreak<RegisterCustomerMessage>(circuit,
        failureThreshold: 5,
        breakDuration: TimeSpan.FromSeconds(30),
        new OnTimeoutRetry<RegisterCustomerMessage>(maxRetries: 3,            
            new StartTransaction<RegisterCustomerMessage>(
                new OpenSqlConnection<RegisterCustomerMessage>(connectionString,
                    new InsertCustomer(),
                    new InsertRegistrationAudit())))));
```

`CircuitBreakerState` is designed to be shared. In a web API, register it as a singleton so all requests hitting the same resource share the same circuit.

### 7. Need throughput control? Add `OnRateLimit`

Under heavy load you may want to throttle how fast requests reach the database. `OnRateLimit` implements a leaky-bucket strategy that can either delay requests until capacity is available or reject them outright.

In this example we wrap it around the circuit breaker:

```csharp
var circuit = new CircuitBreakerState();
var limiter = new RateLimiterState();

pipe.Add(new NormalizeEmail());
pipe.Add(new ValidateCustomerInput());
pipe.Add(
    new OnRateLimit<RegisterCustomerMessage>(limiter,
        capacity: 200,
        leakInterval: TimeSpan.FromSeconds(1),
        behavior: RateLimitExceededBehavior.Delay,
        new OnCircuitBreak<RegisterCustomerMessage>(circuit,
            failureThreshold: 5,
            breakDuration: TimeSpan.FromSeconds(30),
            new OnTimeoutRetry<RegisterCustomerMessage>(maxRetries: 3,                
                new StartTransaction<RegisterCustomerMessage>(
                    new OpenSqlConnection<RegisterCustomerMessage>(connectionString,
                        new InsertCustomer(),
                        new InsertRegistrationAudit()))))));
```

#### What just happened?

In a small number of composable steps we have seen how we can build production-grade resilience: rate limiting, circuit breaking, and automatic retries - all without a single external dependency. Note that all of these patterns are optional and composable. If you don't need rate limiting, just leave it out. If you want to reject instead of delay when the limit is exceeded, change the behavior. You have full control over how these patterns work together.

### 8. Put it inside a full DataPipe with aspects

All that remains is to add cross-cutting concerns like logging and exception handling as aspects. Aspects wrap the entire pipeline and can be added or removed without modifying the core behavior. See the documentation for built-in aspects, what they offer out of the box, and how to create your own.

```csharp
var env = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Development";
var circuit = new CircuitBreakerState();
var limiter = new RateLimiterState();

var pipe = new DataPipe<RegisterCustomerMessage>
{
    Name = "RegisterCustomer",
    TelemetryMode = TelemetryMode.Off
};

pipe.Use(new ExceptionAspect<RegisterCustomerMessage>());
pipe.Use(new LoggingAspect<RegisterCustomerMessage>(logger, "Register Customer", env));

pipe.Add(new NormalizeEmail());
pipe.Add(new ValidateCustomerInput());
pipe.Add(
    new OnRateLimit<RegisterCustomerMessage>(limiter, 200, TimeSpan.FromSeconds(1),
        RateLimitExceededBehavior.Delay,
        new OnCircuitBreak<RegisterCustomerMessage>(circuit,
            failureThreshold: 5,
            breakDuration: TimeSpan.FromSeconds(30),
            new OnTimeoutRetry<RegisterCustomerMessage>(3,
                new StartTransaction<RegisterCustomerMessage>(
                    new OpenSqlConnection<RegisterCustomerMessage>(connectionString,
                        new InsertCustomer(),
                        new InsertRegistrationAudit()))))));

await pipe.Invoke(message);
```

Read it top to bottom. Every step is visible. Every decision is explicit.

---

## Evolving the Slice Without Rewriting It

Now imagine requirements arrive one by one.

### "The boss says: guard writes behind database.modify permission"

Insert a permission guard filter. No existing filter is modified.

```csharp
pipe.Add(new NormalizeEmail());
pipe.Add(new ValidateCustomerInput());

pipe.Add(new RequirePermission("database.modify")); <- new filter added here

pipe.Add(
    new StartTransaction<RegisterCustomerMessage>(
        new OpenSqlConnection<RegisterCustomerMessage>(connectionString,
            new InsertCustomer(),
            new InsertRegistrationAudit())));
```

### "Turn this feature on and off with a config flag"

Add a property to the message and wrap the pipeline body with `IfTrue`:

```csharp
public sealed class RegisterCustomerMessage : BaseMessage, IUseSqlCommand, IAmCommittable, IAmRetryable
{
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public SqlCommand Command { get; set; } = default!;
    public bool Commit { get; set; } = true;
    public int Attempt { get; set; }
    public int MaxRetries { get; set; }
    public Action<int> OnRetrying { get; set; } = _ => { };

    // Feature flag - controlled by configuration
    public bool IsRegistrationEnabled { get; init; }
}
```

```csharp
pipe.Add(
    new IfTrue<RegisterCustomerMessage>(m => m.IsRegistrationEnabled, <- new conditional wrapper
        new NormalizeEmail(),
        new ValidateCustomerInput(),
        new RequirePermission("database.modify"),        
        new StartTransaction<RegisterCustomerMessage>(
            new OpenSqlConnection<RegisterCustomerMessage>(connectionString,
                new InsertCustomer(),
                new InsertRegistrationAudit()))));
```

This is where DataPipe shines: behavior grows by composition, not by invasive rewrites.

---

## 9. Need to process a collection? Add `ForEach` or `ParallelForEach`

To really drive home the power of composition in DataPipe, let's say we need to process a collection of child messages in the middle of our flow. You could do it sequentially with `ForEach<TParent, TChild>` like so:

```csharp
pipe.Add(
    new ForEach<BatchMessage, OrderMessage>(msg => msg.Orders,
        mapper: (parent, child) => child.ConnectionString = parent.ConnectionString,
        new ValidateOrder(),
        new ProcessOrder(),
        new SaveOrderResult()
));
```

But what if we want to process those child messages in parallel? No problem. Doing this in a traditional architecture would require a significant rewrite and the introduction of a new framework or library. With DataPipe, it's just one simple change:

```csharp
pipe.Add(
    new ParallelForEach<BatchMessage, OrderMessage>(msg => msg.Orders,
        mapper: (parent, child) => child.ConnectionString = parent.ConnectionString,
        new ValidateOrder(),
        new ProcessOrder(),
        new SaveOrderResult()
));
```

Here we have simply renamed `ForEach` to `ParallelForEach` and everything else remains the same. The child messages will now be processed concurrently, and the parent pipeline will wait for all of them to complete before moving on! This is a powerful example of how DataPipe's composable design allows you to evolve your behavior with minimal changes to your codebase. You can of course combine with other filters as needed, and even add resilience patterns like try/catch, rate limits, retries or circuit breakers at the child level if desired.

---

## First-Class Support for Telemetry

Telemetry is important and DataPipe fully supports it, but like most features, it is optional. When you need it, like other cross-cutting concerns, you add it as an aspect but with powerful policies and adapters that let you control what gets captured and where it goes.

This quick example covers the basics but see the documentation for more on telemetry policies, adapters, and aspects, and how to create your own.

```csharp
var policy = new SuppressAllExceptErrorsPolicy("RegisterCustomer");
var adapter = new ConsoleTelemetryAdapter(policy);

pipe.TelemetryMode = TelemetryMode.PipelineAndErrors;
pipe.UseIf(env != "Development", new TelemetryAspect<RegisterCustomerMessage>(adapter));
```

DataPipe does not force a vendor or cloud path. Telemetry adapters and policies let you shape what to capture and where it goes.

---

## Why This Works in Real Enterprise Teams

- Features are vertical slices, not accidental call chains
- Pipelines are readable and testable
- Cross-cutting concerns are explicit and local
- Resilience patterns are composable and built-in
- New requirements are usually insert/reorder operations, not rewrites

These ideas have been refined in production systems for more than a decade.

---

## Getting Started

Install from NuGet:

```bash
dotnet add package SCB.DataPipe
```

Optional:

```bash
dotnet add package SCB.DataPipe.Sql
```

Then explore examples and tests in this repository for real composition patterns.

---

## What DataPipe Is Not

DataPipe deliberately does not attempt to be:

- A message bus or mediator
- A workflow engine
- A rules engine
- A scheduler

DataPipe supports concurrent and parallel execution natively — see the documentation for patterns and examples.

Its goal is simple: describe what happens to a message, step by step, in a way that remains clear and intentional.

---

## Learn More

See Docs for sample pipelines and patterns.

---

## License
[MIT License](http://opensource.org/licenses/MIT)