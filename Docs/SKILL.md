# DataPipe Skill

> **Version:** SCB.DataPipe 4.5.7 / SCB.DataPipe.Sql 4.5.7  
> **Platform:** .NET 8+  
> **Packages:** `SCB.DataPipe`, `SCB.DataPipe.Sql` (optional, for SQL Server)

---

## 1. What Is DataPipe

DataPipe is a lightweight, in-memory pipeline library for .NET. It composes business logic as a sequence of **filters** that operate on a **message**. Cross-cutting concerns (logging, exception handling, telemetry) are handled by **aspects** that wrap the filter chain. There is no DI container integration, no hidden middleware, and no framework magic — pipelines are explicit, readable, and created locally per invocation.

### Core Principles

- Messages are mutable data carriers that flow through pipelines
- Filters are stateless, single-responsibility work units
- Aspects wrap filter execution for cross-cutting concerns
- Infrastructure boundaries (connections, transactions) are explicit pipeline steps
- Pipelines are always created locally per invocation — never injected via DI
- Execution is sequential, top-to-bottom, fully async

---

## 2. Installation

```bash
dotnet add package SCB.DataPipe
dotnet add package SCB.DataPipe.Sql    # only if using SQL Server with ADO.NET
```

---

## 3. Core API Surface

### 3.1 DataPipe\<T\> — The Pipeline

```csharp
public class DataPipe<T> where T : BaseMessage
```

| Member | Purpose |
|--------|---------|
| `Name` | Pipeline display name (default `"DataPipe"`) — always set this |
| `TelemetryMode` | Controls telemetry verbosity (`Off`, `PipelineOnly`, `PipelineAndErrors`, `PipelineErrorsAndStops`, `PipelineAndFilters`) |
| `DebugOn` | Enables debug-level logging of pipeline execution flow |
| `Use(Aspect<T>)` | Registers a cross-cutting aspect |
| `UseIf(bool, Aspect<T>, Aspect<T>?)` | Conditional aspect registration at build time |
| `Add(Filter<T>)` | Registers a pipeline filter |
| `Add(params Filter<T>[])` | Registers multiple filters at once |
| `Add(Func<T, Task>)` | Registers an inline lambda filter |
| `AddIf(bool, Filter<T>, Filter<T>?)` | Conditional filter registration at build time |
| `Finally(Filter<T>)` | Registers a guaranteed-execution filter (runs even on error/stop) |
| `Invoke(T msg)` | Executes the pipeline: Aspects → Filters → Finally |

### 3.2 Filter\<T\> — The Work Unit

```csharp
public interface Filter<T> where T : BaseMessage
{
    Task Execute(T msg);
}
```

All custom business logic is implemented as `Filter<T>`. Filters should be stateless and focused on a single responsibility. Filters can accept constructor-injected services when needed.

### 3.3 Aspect\<T\> — Cross-Cutting Concerns

```csharp
public interface Aspect<T> where T : BaseMessage
{
    Task Execute(T msg);
    Aspect<T> Next { get; set; }
}
```

Aspects form a chain-of-responsibility wrapping filter execution. They observe and intercept; filters do the actual work. Register order matters — first registered is outermost.

### 3.4 BaseMessage — The Foundation

```csharp
public abstract class BaseMessage : IDisposable
```

| Property | Type | Purpose |
|----------|------|---------|
| `Actor` | `string?` | Identity of the actor (user ID, "anonymous", etc.) |
| `CorrelationId` | `Guid` | Request correlation (auto-generated) |
| `Service` | `ServiceIdentity?` | Service identity for telemetry (required when telemetry is on) |
| `PipelineName` | `string` | Set automatically by `DataPipe<T>.Invoke()` |
| `StatusCode` | `int` | HTTP-style status code (default 200) |
| `StatusMessage` | `string` | Descriptive status message |
| `IsSuccess` | `bool` | `true` when `StatusCode < 400` |
| `CancellationToken` | `CancellationToken` | Async cancellation support |
| `Execution` | `ExecutionContext` | Flow control: `Stop(reason?)`, `Reset()`, `IsStopped`, `Reason`, `TelemetryAnnotations` |
| `ShouldStop` | `bool` | Convenience: `Execution.IsStopped \|\| CancellationToken.IsCancellationRequested` |
| `Tag` | `string` | General purpose tag for extra context in logs |
| `OnError` | `Action<BaseMessage, Exception>?` | Error lifecycle callback |
| `OnStart` | `Action<BaseMessage>?` | Start lifecycle callback |
| `OnComplete` | `Action<BaseMessage>?` | Completion lifecycle callback |
| `OnSuccess` | `Action<BaseMessage>?` | Success lifecycle callback |
| `OnLog` | `Action<string>?` | Log callback (wired by LoggingAspect) |
| `OnTelemetry` | `Action<TelemetryEvent>?` | Telemetry callback (wired by TelemetryAspect) |
| `TelemetryMode` | `TelemetryMode` | Set internally by the pipeline |
| `Fail(int, string)` | method | Sets `StatusCode` and `StatusMessage` in one call |
| `ShouldEmitTelemetry(TelemetryEvent)` | method | Evaluates mode to decide if event should be emitted |

---

## 4. The Context Pattern — Application-Specific Base Message

**This is the single most important architectural decision.** Every application defines a domain-specific "context" class that extends `BaseMessage` and implements the DataPipe contracts needed by its pipelines. All operation-specific messages then inherit from this context class, not from `BaseMessage` directly.

### 4.1 Designing Your Context

Implement contracts on the context class to enable built-in structural filters:

| Contract | Package | Purpose | Required Properties |
|----------|---------|---------|-------------------|
| `IUseSqlCommand` | DataPipe.Sql | Enables `OpenSqlConnection<T>` | `SqlCommand Command { get; set; }` |
| `IAmCommittable` | DataPipe.Core | Enables `StartTransaction<T>` commit/rollback | `bool Commit { get; set; }` |
| `IAmRetryable` | DataPipe.Core | Enables `OnTimeoutRetry<T>` | `int Attempt { get; set; }`, `int MaxRetries { get; set; }`, `Action<int> OnRetrying { get; set; }` |

### 4.2 Basic Context (No Strongly-Typed Result)

For simple applications or pipelines where the result is carried directly on the message:

```csharp
public class AppContext : BaseMessage, IUseSqlCommand, IAmRetryable
{
    // IUseSqlCommand
    public SqlCommand Command { get; set; }

    // IAmRetryable
    public int Attempt { get; set; }
    public int MaxRetries { get; set; }
    public Action<int> OnRetrying { get; set; } = attempt => { };

    // Domain properties common to all pipelines
    public UserPrincipal User { get; set; } = new();
}
```

Messages that don't need a strongly-typed result extend this directly:

```csharp
public class DeleteOrderMessage : AppContext
{
    public string OrderId { get; set; }
}
```

### 4.3 Result Pattern — Strongly-Typed Results

Most applications benefit from a standardized result pattern. Create a base result class, then a generic context subclass:

```csharp
// Base result
public class CommonResult
{
    public bool Success { get; set; } = true;
    public string StatusMessage { get; set; } = string.Empty;
    public int StatusCode { get; set; } = 200;
}

// Generic context with strongly-typed result
public class AppContext<TResult> : AppContext where TResult : CommonResult, new()
{
    public TResult Result { get; set; } = new TResult();
}
```

Now create operation-specific result types and messages:

```csharp
public class OrderResult : CommonResult
{
    public string OrderId { get; set; }
    public decimal Total { get; set; }
    public DateTime ProcessedAt { get; set; }
}

public class CreateOrderMessage : AppContext<OrderResult>
{
    public string CustomerId { get; set; }
    public List<OrderLine> Lines { get; set; } = new();
}
```

### 4.4 Adding Transactions to the Context

If **most** pipelines need transactions, implement `IAmCommittable` on the base context:

```csharp
public class AppContext : BaseMessage, IUseSqlCommand, IAmRetryable, IAmCommittable
{
    public bool Commit { get; set; } = true;
    // ... rest of properties
}
```

If only **some** pipelines need transactions, implement `IAmCommittable` on specific message types instead:

```csharp
public class SaveChargeMessage : AppContext<ChargeResult>, IAmCommittable
{
    public bool Commit { get; set; } = true;
    public ChargeData Charge { get; set; }
}
```

### 4.5 Multi-Level Context Hierarchy

For complex applications, build a deeper hierarchy enabling progressive opt-in:

```csharp
// Level 1: Base read operations
public class AppContext : BaseMessage, IUseSqlCommand, IAmRetryable
{
    public SqlCommand Command { get; set; }
    public int Attempt { get; set; }
    public int MaxRetries { get; set; }
    public Action<int> OnRetrying { get; set; } = _ => { };
    public UserPrincipal User { get; set; } = new();
    public CommonResult Result { get; set; } = new();
}

// Level 2: Strongly-typed results
public class AppContext<TResult> : AppContext where TResult : CommonResult, new()
{
    public new TResult Result { get; set; } = new TResult();
}

// Level 3: Write operations with transactions and audit
public class AppCommandContext<TResult> : AppContext<TResult>, IAmCommittable
    where TResult : CommonResult, new()
{
    public bool Commit { get; set; } = true;
    public string Operation { get; set; }
    public string EntityName { get; set; }
    public string EntityId { get; set; }
    public List<Change> Changes { get; set; } = new();
}

// Level 4: Optimistic concurrency
public class AppConcurrencyCommand<TResult> : AppCommandContext<TResult>
    where TResult : CommonResult, new()
{
    public DateTime OriginalChangeDate { get; set; }
    public DateTime CurrentChangeDate { get; set; }
    public bool OverwriteConflict { get; set; }
}
```

### 4.6 Custom Contracts

Define your own contracts for domain-specific infrastructure:

```csharp
public interface IQueueMessages
{
    Queue<string> MessageQueue { get; set; }
}

public interface ITrackChanges
{
    string Operation { get; set; }
    string EntityName { get; set; }
    string EntityId { get; set; }
    List<Change> Changes { get; set; }
}
```

### 4.7 Important Rule — Aspects Use Non-Generic Context

Custom aspects should subclass the **non-generic** context to work across all message types:

```csharp
// CORRECT — works for all messages
public class ErrorResponseAspect<T> : Aspect<T> where T : AppContext { ... }

// WRONG — would only work for messages with a specific result type
public class ErrorResponseAspect<T> : Aspect<T> where T : AppContext<SomeResult> { ... }
```

### 4.8 Message Property Design — Inter-Filter Data Flow

Filters communicate **exclusively through message properties**. There is no other channel — no return values, no shared static state, no service locator. One filter sets a property; a downstream filter reads it. This is a fundamental DataPipe design principle.

When designing messages, categorise properties by their role:

#### Input Properties — Set Before Pipeline Invocation

These are the request parameters, set by the controller or caller. They are the "inputs" to the pipeline:

```csharp
public class CreateOrderMessage : AppContext<OrderResult>
{
    // Input: set by the controller before pipe.Invoke()
    public string CustomerId { get; set; }
    public List<OrderLineRequest> Lines { get; set; } = new();
}
```

#### Output Properties — The Result

The strongly-typed `Result` property (from the generic context) carries the final output. Filters populate it progressively:

```csharp
// Filter 1 sets the order ID
msg.Result.OrderId = generatedId;

// Filter 2 sets the total
msg.Result.Total = calculatedTotal;

// Filter 3 sets the timestamp
msg.Result.ProcessedAt = DateTime.UtcNow;
```

#### Internal / Transient Properties — Inter-Filter State

**This is the most important pattern for filter implementors.** Many pipelines need intermediate state that is produced by one filter and consumed by another, but is not part of the request input or the final result. These properties live on the message and exist solely for the duration of the pipeline execution.

Use `internal` accessibility to prevent external code (controllers, tests of other layers) from accessing implementation details:

```csharp
public class RefreshTokenMessage : AuthContext<RefreshTokenResult>
{
    // Input: set by controller
    public string RefreshToken { get; set; }

    // Internal transient state: used between filters only
    internal string OldTokenHash { get; set; }
    internal string OldTokenSalt { get; set; }
    internal DateTime OldTokenCreatedAt { get; set; }
    internal RefreshTokenRotationState RotationState { get; set; } = new();
}
```

Or group transient state into an internal class for clarity:

```csharp
public class CreateOrderMessage : SalesCommandContext<OrderResult>
{
    // Input
    public string CustomerId { get; set; }
    public List<OrderLineRequest> Lines { get; set; } = new();

    // Transient inter-filter state
    internal bool IsValid { get; set; }
    internal int GeneratedOrderNumber { get; set; }
    internal List<PurchaseOrder> GroupedPurchaseOrders { get; set; } = new();
    internal int CurrentContainerId { get; set; }
    internal string CurrentPoNumber { get; set; }
}
```

#### Using `dynamic` for Ad-Hoc Transient Data

Some projects add a `dynamic Data` property to the context for loosely-typed transient storage when you don't want to declare named properties:

```csharp
public class AppContext : BaseMessage
{
    public dynamic Data { get; set; } = new System.Dynamic.ExpandoObject();
}

// In a filter:
msg.Data.TempCalculation = 42.5;

// In a later filter:
var calc = (double)msg.Data.TempCalculation;
```

Prefer named internal properties over `dynamic` for type safety, but `dynamic` can be useful for rapid prototyping.

#### Data Flow Example — How Filters Chain Through Properties

The pipeline below shows how each filter produces state that the next consumes:

```
Filter chain for SignIn pipeline:

1. LoadUserDetails       → reads msg.User.Name from DB
                         → sets msg.Result.Salt, msg.Result.UserId,
                               msg.Result.TrustedLogin, msg.User.IsSuperuser

2. HashIncomingPassword  → reads msg.Password + msg.Result.Salt
                         → sets msg.Result.HashedPassword

3. Authenticate          → reads msg.User.Name + msg.Result.HashedPassword
                         → validates against DB, sets msg.StatusCode on failure

4. CreateTokens          → reads msg.Result.UserId, msg.Result.UserName, etc.
                         → sets msg.Result.Token, msg.Result.RefreshToken,
                               msg.Result.AccessExpiresAt, msg.Result.RefreshExpiresAt

5. SetAuthCookies        → reads msg.Result.Token, msg.Result.RefreshToken
                         → writes cookies to msg.Response
```

Each filter reads what it needs from the message and writes what it produces back to the message. No filter knows about the others — they only know the message shape.

#### Guidelines for Message Property Design

1. **Input properties** — public, set before `Invoke()`
2. **Result properties** — on the strongly-typed `Result`, populated by filters
3. **Inter-filter state** — use `internal` properties on the message for intermediate data
4. **Sensitive state** — use `internal` classes/properties to prevent leakage outside the assembly
5. **Flags and control properties** — booleans like `IsValid`, `NothingToDo`, `RequiresSpecialProcessing` that drive `IfTrue` / `Policy` branching
6. **Never use static or shared mutable state** — messages are the only communication channel

---

## 5. Filters

### 5.1 Creating Filters

Filters implement `Filter<T>` for a specific message type:

```csharp
public class ValidateOrder : Filter<CreateOrderMessage>
{
    public async Task Execute(CreateOrderMessage msg)
    {
        if (string.IsNullOrEmpty(msg.CustomerId))
        {
            msg.Fail(400, "Customer ID required");
            msg.Execution.Stop("Validation failed");
            return;
        }
        msg.OnLog?.Invoke($"Order validated for customer {msg.CustomerId}");
    }
}
```

### 5.2 Generic / Reusable Filters

Create filters with generic constraints to reuse across message types:

```csharp
// Reusable across any message that extends AppCommandContext
public class CaptureChanges<T> : Filter<T> where T : AppCommandContext<CommonResult>
{
    public async Task Execute(T msg)
    {
        // Capture audit trail — works for any message with changes
        msg.OnLog?.Invoke($"Captured {msg.Changes.Count} changes for {msg.EntityName}");
    }
}

// Reusable across any message with a specific interface
public class DownloadBlob<T> : Filter<T> where T : AppContext, IHandleAzureMessages
{
    public async Task Execute(T msg)
    {
        // Download from Azure Blob Storage
    }
}
```

### 5.3 Filters With Constructor Injection

Filters can accept services passed from the service class constructor via closure:

```csharp
public class AuditLoginStart : Filter<LogInMessage>
{
    private readonly IAuditWriter _auditWriter;

    public AuditLoginStart(IAuditWriter auditWriter)
    {
        _auditWriter = auditWriter;
    }

    public async Task Execute(LogInMessage msg)
    {
        await _auditWriter.WriteAsync(msg.User.Name, "Login attempt started");
    }
}
```

### 5.4 Filters Implementing Multiple Message Types

A single filter class can implement `Filter<T>` for multiple message types:

```csharp
public class ClearAuthCookies : Filter<LogOutMessage>, Filter<RotationSignalMessage>
{
    public Task Execute(LogOutMessage msg) => ClearCookies(msg);
    public Task Execute(RotationSignalMessage msg) => ClearCookies(msg);

    private Task ClearCookies(AuthContext msg)
    {
        msg.Response.Cookies.Delete("access_token");
        msg.Response.Cookies.Delete("refresh_token");
        return Task.CompletedTask;
    }
}
```

### 5.5 Lambda Filters

For quick prototyping or simple one-off steps:

```csharp
pipeline.Add(async msg =>
{
    await SaveToDatabase(msg);
});
```

Lambda filters and class-based filters are interchangeable. Use lambdas for simple steps; use classes for reusable, testable logic.

### 5.6 Filter Implementation Patterns

#### Pattern: Validate-and-Stop

Validation filters check preconditions and halt the pipeline on failure. Always validate first:

```csharp
public class ValidateCreateOrderMessage : Filter<CreateOrderMessage>
{
    public Task Execute(CreateOrderMessage msg)
    {
        if (string.IsNullOrEmpty(msg.CustomerId))
        {
            msg.Fail(400, "Customer ID is required");
            msg.Execution.Stop(msg.StatusMessage);
            return Task.CompletedTask;
        }

        if (!msg.Lines.Any())
        {
            msg.Fail(400, "Order must contain at least one line item");
            msg.Execution.Stop(msg.StatusMessage);
            return Task.CompletedTask;
        }

        msg.OnLog?.Invoke($"Validated order for customer {msg.CustomerId} with {msg.Lines.Count} lines");
        return Task.CompletedTask;
    }
}
```

#### Pattern: Lookup / Enrichment

A filter reads from a data source and populates message properties that downstream filters depend on:

```csharp
public class LoadUserDetails : Filter<LogInMessage>
{
    public async Task Execute(LogInMessage msg)
    {
        msg.Command.CommandText = "SELECT Id, Salt, DisplayName, IsTrusted, IsSuperuser FROM dbo.Users WHERE UserName = @UserName";
        msg.Command.Parameters.Clear();
        msg.Command.Parameters.AddWithValue("@UserName", msg.User.Name);
        using var rdr = await msg.Command.ExecuteReaderAsync(msg.CancellationToken);

        if (!await rdr.ReadAsync(msg.CancellationToken))
        {
            msg.Fail(401, "Invalid credentials");
            msg.Execution.Stop(msg.StatusMessage);
            return;
        }

        // Populate transient state for downstream filters
        msg.Result.UserId = (int)rdr["Id"];
        msg.Result.Salt = (string)rdr["Salt"];
        msg.Result.TrustedLogin = (bool)rdr["IsTrusted"];
        msg.User.IsSuperuser = (bool)rdr["IsSuperuser"];
        msg.Result.DisplayName = (string)rdr["DisplayName"];

        msg.OnLog?.Invoke($"Loaded user {msg.User.Name} (Id: {msg.Result.UserId})");
    }
}
```

#### Pattern: Mutation with Audit

A filter performs a write and records the change for audit capture:

```csharp
public class UpdateOrderStatus : Filter<SubmitOrderMessage>
{
    public async Task Execute(SubmitOrderMessage msg)
    {
        var previousStatus = msg.CurrentStatus;

        msg.Command.CommandText = @"
            UPDATE dbo.Orders SET Status = @Status, ModifiedBy = @Actor, ModifiedDate = GETUTCDATE()
            WHERE OrderId = @OrderId";
        msg.Command.Parameters.Clear();
        msg.Command.Parameters.AddWithValue("@Status", "Submitted");
        msg.Command.Parameters.AddWithValue("@Actor", msg.Actor);
        msg.Command.Parameters.AddWithValue("@OrderId", msg.OrderId);
        await msg.Command.ExecuteNonQueryAsync(msg.CancellationToken);

        // Record changes for audit trail (consumed by CaptureChanges filter later)
        msg.Changes.Add(new Change
        {
            Field = "Status",
            OldValue = previousStatus,
            NewValue = "Submitted"
        });

        msg.OnLog?.Invoke($"Order {msg.OrderId} status changed from {previousStatus} to Submitted");
    }
}
```

#### Pattern: Computation that Sets Internal State

A filter performs a computation and stores the result on internal message properties for later filters:

```csharp
public class GroupBySupplier : Filter<CreateOrderMessage>
{
    public Task Execute(CreateOrderMessage msg)
    {
        // Group input lines into purchase orders per supplier
        msg.GroupedPurchaseOrders = msg.Lines
            .GroupBy(l => l.SupplierId)
            .Select(g => new PurchaseOrder
            {
                SupplierId = g.Key,
                Lines = g.ToList()
            })
            .ToList();

        msg.OnLog?.Invoke($"Grouped {msg.Lines.Count} lines into {msg.GroupedPurchaseOrders.Count} purchase orders");
        return Task.CompletedTask;
    }
}
```

#### Pattern: Conditional Flag Setting for Downstream IfTrue/Policy

A filter sets boolean flags or enum values that drive branching later in the pipeline:

```csharp
public class EvaluateInvoiceRules : Filter<InvoiceMessage>
{
    public Task Execute(InvoiceMessage msg)
    {
        msg.RequiresManualReview = msg.Total > 10000m || msg.Supplier.IsNew;
        msg.IsHighPriority = msg.DueDate <= DateTime.UtcNow.AddDays(3);

        msg.OnLog?.Invoke($"Invoice evaluation: ManualReview={msg.RequiresManualReview}, HighPriority={msg.IsHighPriority}");
        return Task.CompletedTask;
    }
}

// Later in the pipeline:
pipeline.Add(new IfTrue<InvoiceMessage>(
    msg => msg.RequiresManualReview,
    new RouteToApprovalQueue()));
```

#### Pattern: External API Call (Outside SQL Scope)

Some operations need to happen outside the database connection scope. Register them as separate top-level filters:

```csharp
var pipe = new DataPipe<CommodityMessage>();
pipe.Use(new ExceptionAspect<CommodityMessage>());
pipe.Use(new LoggingAspect<CommodityMessage>(logger));

// Database lookup first
pipe.Add(new OnTimeoutRetry<CommodityMessage>(3,
    new OpenSqlConnection<CommodityMessage>(connectionString,
        new LoadCommodityCode())));

// Then call external API (outside SQL scope)
pipe.Add(new IfTrue<CommodityMessage>(
    msg => msg.NeedsExternalLookup,
    new CallGovUkCommodityApi()));
```

#### Pattern: Debug/Diagnostic Filter (Development Only)

Serialise the entire message for diagnostic purposes, gated by environment:

```csharp
public class DumpMessageState : Filter<T> where T : BaseMessage
{
    public Task Execute(T msg)
    {
        var json = JsonSerializer.Serialize(msg, new JsonSerializerOptions { WriteIndented = true });
        msg.OnLog?.Invoke($"MESSAGE STATE:\n{json}");
        return Task.CompletedTask;
    }
}

// Only added in development
pipeline.Add(new IfTrue<OrderMessage>(
    msg => Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") == "Development",
    new DumpMessageState<OrderMessage>()));
```

---

## 6. Built-in Structural Filters

Structural filters are infrastructure wrappers that manage resources (connections, transactions, retries, loops, branching). They accept child filters executed within their scope.

### 6.1 IfTrue\<T\> — Runtime Conditional

Evaluates a predicate at execution time. If true, executes the nested filter(s). If false, skips (or executes the optional else filter).

```csharp
// Simple conditional
pipeline.Add(new IfTrue<OrderMessage>(
    msg => msg.RequiresApproval,
    new SendForApproval()
));

// With else branch
pipeline.Add(new IfTrue<OrderMessage>(
    msg => msg.IsValid,
    new ProcessOrder(),       // if true
    new RejectOrder()         // if false
));

// Nested in pipeline
pipeline.Add(
    new ValidateOrder(),
    new IfTrue<OrderMessage>(msg => msg.RequiresApproval,
        new SendForApproval()),
    new IfTrue<OrderMessage>(msg => msg.IsConfidential,
        new EncryptDocument()),
    new SaveOrder()
);
```

### 6.2 Policy\<T\> — Multi-Branch Runtime Dispatch

Select exactly one filter path from multiple options based on message state. Return `null` to skip entirely:

```csharp
pipeline.Add(new Policy<OrderMessage>(msg => msg.ImportKind switch
{
    "accounts" => new ValidateOrderAccounts(),
    "addresses" => new ValidateOrderAddress(),
    "both" => new ValidateOrderAccountAndAddresses(),
    _ => null  // skip if no match
}));
```

Use `Sequence<T>` inside Policy branches to group multiple filters:

```csharp
pipeline.Add(new Policy<InvoiceMessage>(msg =>
{
    return msg.InvoiceType switch
    {
        InvoiceType.Standard => new ProcessStandardInvoice(),
        InvoiceType.CreditNote => new Sequence<InvoiceMessage>(
            new ReverseOriginalInvoice(),
            new ApplyCreditToAccount()
        ),
        _ => null
    };
}));
```

### 6.3 Sequence\<T\> — Group Related Filters

Groups multiple filters as a single logical step. Useful inside `IfTrue` or `Policy` branches:

```csharp
pipeline.Add(new Sequence<CustomerMessage>(
    new CreateCustomerRecord(),
    new GenerateAccountNumber(),
    new AssignDefaultPreferences()
));
```

### 6.4 OnTimeoutRetry\<T\> — Retry on Timeout

Requires `IAmRetryable` on the message. Retries child filters on transient timeout exceptions:

```csharp
// Retry only the external API call
pipeline.Add(new OnTimeoutRetry<OrderMessage>(maxRetries: 3,
    new FetchDataFromExternalApi()
));

// Retry the database operation
pipeline.Add(new OnTimeoutRetry<OrderMessage>(maxRetries: 2,
    new OpenSqlConnection<OrderMessage>(connectionString,
        new SaveOrder()
    )
));
```

**Key:** Retries are scoped — only the wrapped filters are retried, not the entire pipeline.

### 6.5 RepeatUntil\<T\> — Conditional Loop

Loops child filters until the predicate returns `true`:

```csharp
pipeline.Add(new RepeatUntil<BatchMessage>(
    msg => msg.NothingToDo,      // stop when true
    new ResetState(),
    new GetNextBatch(),
    new ProcessBatch()
));
```

### 6.6 Repeat\<T\> — Unconditional Loop

Loops indefinitely. Use `msg.Execution.Stop()` inside a filter to break:

```csharp
pipeline.Add(new Repeat<QueueMessage>(
    new ResetState(),             // Reset() clears the stopped state
    new GetNextItem(),
    new IfTrue<QueueMessage>(msg => msg.QueueEmpty,
        new StopExecution()),     // calls msg.Execution.Stop()
    new ProcessItem()
));
```

`Execution.Reset()` clears the stopped state between loop iterations so `Stop()` can be used for loop control without permanently halting the pipeline.

### 6.7 ForEach\<TMessage, TItem\> — Iterate Over Collection

Iterates over an enumerable, executing child filters for each item:

```csharp
pipeline.Add(new ForEach<OrderMessage, OrderLine>(
    msg => msg.Lines,             // selector: collection to iterate
    (msg, item) => msg.CurrentLine = item,  // setter: assign current item to message
    new ValidateLineItem(),
    new CalculateLineTotal()
));
```

### 6.8 OpenSqlConnection\<T\> — Scoped SQL Connection

Requires `IUseSqlCommand` on the message. Opens a SQL connection for child filters:

```csharp
pipeline.Add(new OpenSqlConnection<OrderMessage>(connectionString,
    new LoadOrderDetails(),
    new LoadOrderLines()
));
```

Inside child filters, access the connection via `msg.Command`:

```csharp
public async Task Execute(LoadUserMessage msg)
{
    msg.Command.CommandText = "SELECT * FROM dbo.Users WHERE Id = @Id";
    msg.Command.Parameters.Clear();
    msg.Command.Parameters.AddWithValue("@Id", msg.Id);
    using var rdr = await msg.Command.ExecuteReaderAsync(msg.CancellationToken);
    while (await rdr.ReadAsync(msg.CancellationToken))
    {
        msg.Result.DisplayName = (string)rdr["DisplayName"];
    }
}
```

### 6.9 StartTransaction\<T\> — SQL Transaction

Requires `IAmCommittable` + `IUseSqlCommand` on the message. Wraps child filters in a SQL transaction. Commits if `msg.Commit` is true and no errors; rolls back otherwise:

```csharp
// Default isolation level (ReadCommitted)
pipeline.Add(new StartTransaction<OrderMessage>(
    new OpenSqlConnection<OrderMessage>(connectionString,
        new SaveOrder(),
        new UpdateInventory()
    )
));

// Explicit isolation level
pipeline.Add(new StartTransaction<OrderMessage>(IsolationLevel.Serializable,
    new OpenSqlConnection<OrderMessage>(connectionString,
        new SaveOrder()
    )
));
```

**Transaction is rolled back when:**
- `msg.Commit` is `false`
- An exception is thrown
- `msg.Execution.Stop()` was called

### 6.10 Standard Nesting Convention

The production-proven nesting pattern for data mutation pipelines:

```
OnTimeoutRetry<T>(maxRetries,                    ← retry transient failures
    StartTransaction<T>(                          ← optional, for mutations
        OpenSqlConnection<T>(connectionString,    ← database scope
            ValidationFilter(),                   ← business logic
            BusinessFilter(),
            AuditFilter())))
```

For read-only operations, omit `StartTransaction`:

```
OnTimeoutRetry<T>(maxRetries,
    OpenSqlConnection<T>(connectionString,
        ValidationFilter(),
        LoadData()))
```

For multiple independent database scopes (e.g., dual databases):

```csharp
// Audit on local DB
pipe.Add(new OpenSqlConnection<T>(localConnectionString,
    new AuditStart()));

// Business logic on main DB
pipe.Add(new OnTimeoutRetry<T>(maxRetries,
    new OpenSqlConnection<T>(mainConnectionString,
        new BusinessFilter1(),
        new BusinessFilter2())));

// Audit end on local DB
pipe.Add(new OpenSqlConnection<T>(localConnectionString,
    new AuditEnd()));
```

---

## 7. Built-in Aspects

### 7.1 ExceptionAspect\<T\>

Catches unhandled exceptions and sets error status on the message. Use this or a custom error-handling aspect — always register first:

```csharp
pipeline.Use(new ExceptionAspect<OrderMessage>());
```

### 7.2 BasicConsoleLoggingAspect\<T\>

Simple console-based pipeline logging. Useful for development and console apps:

```csharp
pipeline.Use(new BasicConsoleLoggingAspect<OrderMessage>("OrderProcessing"));
```

### 7.3 LoggingAspect\<T\>

Structured pipeline execution logging via `ILogger`. This is the production-grade logging aspect:

```csharp
pipeline.Use(new LoggingAspect<OrderMessage>(
    logger,                              // ILogger (required)
    title: "CreateOrder",                // Friendly pipeline name (optional)
    env: "Production",                   // Environment name (optional)
    startEndLevel: LogLevel.Information, // Log level for START/END (optional)
    mode: PipeLineLogMode.Full           // Verbosity mode (optional)
));
```

**PipeLineLogMode values:**

| Mode | Behavior |
|------|----------|
| `Full` | Logs start, all `msg.OnLog?.Invoke()` calls, end, and errors |
| `StartEndOnly` | Logs only pipeline start, end, and errors |
| `ErrorsOnly` | Logs only exceptions |

**Log scope includes:** Environment, PipelineName, CorrelationId, Tag, IsTelemetry=false

**Logging from inside filters:**

```csharp
msg.OnLog?.Invoke($"Order {msg.OrderId} validated successfully");
```

When mode is `Full`, these messages are captured with the same scope and context.

### 7.4 TelemetryAspect\<T\>

Forwards telemetry events to an `ITelemetryAdapter`. See [Section 10: Telemetry](#10-telemetry).

```csharp
pipeline.Use(new TelemetryAspect<OrderMessage>(adapter));
```

### 7.5 Aspect Registration Order

**Always register aspects in this exact order:**

1. **Error/Exception handling** — first (outermost wrapper)
2. **Logging** — second
3. **Telemetry** — third
4. **Infrastructure aspects** (custom, e.g., MongoDbClientAspect) — last before filters

```csharp
pipe.Use(new ExceptionAspect<T>());                       // 1. Error handling
pipe.Use(new LoggingAspect<T>(logger, "Name", env));      // 2. Logging
pipe.Use(new TelemetryAspect<T>(adapter));                // 3. Telemetry
pipe.Use(new MongoDbClientAspect<T>());                   // 4. Infrastructure
```

### 7.6 Custom Aspects

Create custom aspects by implementing `Aspect<T>`:

```csharp
public class ErrorResponseAspect<T> : Aspect<T> where T : AppContext
{
    public Aspect<T> Next { get; set; }

    public async Task Execute(T msg)
    {
        try
        {
            await Next.Execute(msg);
        }
        catch (ValidationException ex)
        {
            msg.Fail(422, ex.Message);
        }
        catch (NotFoundException ex)
        {
            msg.Fail(404, ex.Message);
        }
        catch (ConflictException ex)
        {
            msg.Fail(409, ex.Message);
        }
        catch (Exception ex)
        {
            msg.Fail(500, "An internal error occurred");
            msg.OnError?.Invoke(msg, ex);
        }
    }
}
```

---

## 8. Conditional Registration: AddIf / UseIf vs IfTrue

These serve different purposes:

| | `AddIf` / `UseIf` | `IfTrue<T>` |
|---|---|---|
| **Evaluates** | At **pipeline construction** time | At **message execution** time |
| **Effect** | Filter is physically added or not to the pipeline | Filter is always registered, conditionally executed |
| **Use for** | Environment, configuration, feature flags | Message-specific branching |

```csharp
// AddIf: build-time decision
var isProd = env == "Production";
pipeline.AddIf(isProd, new CallRealGateway(), new CallMockGateway());

// UseIf: build-time aspect choice
pipeline.UseIf(env == "Development", new BasicConsoleLoggingAspect<T>());
pipeline.UseIf(env != "Development", new LoggingAspect<T>(logger, "Name", env));

// IfTrue: runtime decision based on message state
pipeline.Add(new IfTrue<OrderMessage>(
    msg => msg.RequiresApproval,
    new SendForApproval()
));
```

---

## 9. Stopping Execution and Failure Handling

### 9.1 Stopping the Pipeline

Call `msg.Execution.Stop()` to halt execution. Remaining filters are skipped, but aspects and `Finally` filters still complete:

```csharp
public class ValidateApiKey : Filter<RequestMessage>
{
    public Task Execute(RequestMessage msg)
    {
        if (string.IsNullOrEmpty(msg.ApiKey))
        {
            msg.Fail(401, "API key is required");
            msg.Execution.Stop(msg.StatusMessage);
        }
        return Task.CompletedTask;
    }
}
```

The optional reason string is stored on `msg.Execution.Reason` and included in logs and telemetry.

### 9.2 Stopping With Success

`Stop()` is not only for errors — use it for early exits when work is already done:

```csharp
public class CheckCache : Filter<OrderMessage>
{
    public Task Execute(OrderMessage msg)
    {
        var cached = GetFromCache(msg.OrderId);
        if (cached != null)
        {
            msg.Result.Order = cached;
            msg.StatusCode = 200;
            msg.Execution.Stop();  // cached response ready, skip remaining filters
        }
        return Task.CompletedTask;
    }
}
```

### 9.3 Fail() Convenience Method

`Fail(code, message)` sets both `StatusCode` and `StatusMessage`. It does **not** stop the pipeline — combine with `Execution.Stop()` when you want to halt:

```csharp
msg.Fail(404, "User not found");
msg.Execution.Stop(msg.StatusMessage);
```

### 9.4 Checking Results After Invocation

```csharp
await pipe.Invoke(msg);

if (msg.IsSuccess)  // StatusCode < 400
{
    return Ok(msg.Result);
}

return Problem(detail: msg.StatusMessage, statusCode: msg.StatusCode);
```

### 9.5 What Happens When Execution Stops

- The current filter completes normally
- All subsequent filters are skipped
- `Finally` filters still execute
- Aspects unwind as usual
- Telemetry records the outcome as `Stopped` with the reason
- Transactions are rolled back

### 9.6 Finally Filters

Guaranteed-execution filters run even on error or stop — useful for cleanup or flushing:

```csharp
pipeline.Finally(new FlushLog<OrderMessage>());
pipeline.Finally(new AuditRefundAttempt());
```

### 9.7 Execution.Reset()

`Reset()` clears the stopped state and reason. Used internally by `Repeat<T>` and `RepeatUntil<T>` for loop control:

```csharp
// Inside a loop, a filter stops: msg.Execution.Stop()
// The loop filter calls: msg.Execution.Reset() before next iteration
```

---

## 10. Telemetry

DataPipe has a built-in telemetry system that tracks pipeline and filter execution with start/end events, durations, outcomes, and custom attributes.

### 10.1 Setup

```csharp
var pipeline = new DataPipe<OrderMessage>
{
    Name = "CreateOrder",
    TelemetryMode = TelemetryMode.PipelineAndErrors  // control granularity
};
```

### 10.2 TelemetryMode

| Mode | Value | What's Emitted |
|------|-------|----------------|
| `Off` | 0 | Nothing (default) |
| `PipelineOnly` | 1 | Pipeline start/end only |
| `PipelineAndErrors` | 2 | Pipeline start/end + filter exceptions |
| `PipelineErrorsAndStops` | 3 | + stopped filters |
| `PipelineAndFilters` | 4 | Everything including per-filter start/end events |

### 10.3 ServiceIdentity

**Required when telemetry is enabled.** Set on every message:

```csharp
var msg = new OrderMessage
{
    Actor = "user@example.com",
    Service = new ServiceIdentity
    {
        Name = "Orders.Api",
        Environment = "Production",
        Version = "2.1.0",
        InstanceId = Environment.MachineName
    }
};
```

### 10.4 TelemetryAspect and Adapters

```csharp
var adapter = new StructuredJsonTelemetryAdapter(logger, policy);
pipeline.Use(new TelemetryAspect<OrderMessage>(adapter));
```

**Built-in adapters:**

| Adapter | Purpose |
|---------|---------|
| `ConsoleTelemetryAdapter(policy?)` | Batched JSON output to console (development) |
| `StructuredJsonTelemetryAdapter(logger, policy?)` | Batched structured JSON to ILogger (production) |

### 10.5 Telemetry Policies

Policies filter events at the adapter level after the mode filter:

```csharp
public interface ITelemetryPolicy
{
    bool ShouldInclude(TelemetryEvent evt);
}
```

**Built-in policies:**

| Policy | Purpose |
|--------|---------|
| `DefaultCaptureEverythingPolicy` | Includes all events (default) |
| `MinimumDurationPolicy(ms)` | Excludes filter events shorter than threshold |
| `RolePolicy(role)` | Includes only `Business`, `Structural`, or `All` role events |
| `ExcludeStartEventsPolicy(exclude)` | Drops `Start` phase events to reduce volume |
| `SuppressAllExceptErrorsPolicy(name)` | Suppresses everything except exceptions for a named pipeline |
| `CompositeTelemetryPolicy(policies...)` | Combines multiple policies — **all** must pass |

**Standard production policy:**

```csharp
var policy = new CompositeTelemetryPolicy(
    new MinimumDurationPolicy(AppSettings.Instance.TelemetryMinDuration),
    new RolePolicy(AppSettings.Instance.TelemetryRole),
    new ExcludeStartEventsPolicy(AppSettings.Instance.TelemetryExcludeStartEvents)
);
```

**Reduced policy for SSE/status/health endpoints:**

```csharp
var policy = new SuppressAllExceptErrorsPolicy("CheckHealth");
```

### 10.6 TelemetryRole

| Value | Meaning |
|-------|---------|
| `All` (0) | Capture everything |
| `Business` (1) | Business events only |
| `Structural` (2) | Structural/infrastructure events only |

### 10.7 TelemetryEvent Structure

```csharp
new TelemetryEvent
{
    Actor = msg.Actor,
    MessageId = msg.CorrelationId,
    Component = "FilterName",
    PipelineName = msg.PipelineName,
    Service = msg.Service,
    Role = FilterRole.Business,          // Business, Structural
    Scope = TelemetryScope.Filter,       // Filter, Pipeline
    Phase = TelemetryPhase.Start,        // Start, End
    Outcome = TelemetryOutcome.Success,  // None, Started, Success, Stopped, Exception
    Reason = "...",
    DurationMs = 42,                     // end events only
    Timestamp = DateTimeOffset.UtcNow,
    Attributes = new Dictionary<string, object>()
}
```

### 10.8 TelemetryAnnotations

Structural filters attach custom metadata via `msg.Execution.TelemetryAnnotations`. Annotations are consumed and cleared after each event emission:

```csharp
msg.Execution.TelemetryAnnotations["DatabaseName"] = "OrdersDb";
msg.Execution.TelemetryAnnotations["IsolationLevel"] = "ReadCommitted";
```

### 10.9 IAmStructural Interface

Structural filters (infrastructure wrappers) implement this internal interface:

```csharp
public interface IAmStructural
{
    bool EmitTelemetryEvent { get; }
}
```

When `EmitTelemetryEvent` is `false`, the filter manages its own start/end telemetry events rather than relying on the parent.

### 10.10 Complete Telemetry Example

```csharp
public async Task CreateOrder(CreateOrderMessage msg)
{
    var env = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Development";
    var policy = new CompositeTelemetryPolicy(
        new MinimumDurationPolicy(50),
        new ExcludeStartEventsPolicy(true));
    var adapter = new StructuredJsonTelemetryAdapter(logger, policy);

    var pipe = new DataPipe<CreateOrderMessage>
    {
        Name = "CreateOrder",
        TelemetryMode = TelemetryMode.PipelineAndErrors
    };

    pipe.Use(new ExceptionAspect<CreateOrderMessage>());
    pipe.Use(new LoggingAspect<CreateOrderMessage>(logger, "CreateOrder", env));
    pipe.Use(new TelemetryAspect<CreateOrderMessage>(adapter));

    pipe.Add(
        new ValidateOrder(),
        new OnTimeoutRetry<CreateOrderMessage>(2,
            new OpenSqlConnection<CreateOrderMessage>(connectionString,
                new SaveOrder(),
                new UpdateInventory())));

    await pipe.Invoke(msg);
}
```

---

## 11. Logging

### 11.1 LoggingAspect Configuration

```csharp
// Development: verbose
pipeline.Use(new LoggingAspect<T>(logger, "PipelineName", env, mode: PipeLineLogMode.Full));

// Production: errors only
pipeline.Use(new LoggingAspect<T>(logger, "PipelineName", env, mode: PipeLineLogMode.ErrorsOnly));

// Status/health endpoints: minimal
pipeline.Use(new LoggingAspect<T>(logger, "CheckHealth", env, mode: PipeLineLogMode.ErrorsOnly));
```

### 11.2 Log Scope Data

Every log entry includes structured scope data:

```
Environment: Production
PipelineName: CreateOrder
CorrelationId: {guid}
Tag: {custom-tag}
IsTelemetry: false
```

Use `msg.Tag` to attach custom context to all logs for a pipeline execution.

### 11.3 Serilog Integration

Configure Serilog to separate telemetry from application logs:

```json
{
  "Serilog": {
    "WriteTo": [
      {
        "Name": "Logger",
        "Args": {
          "configureLogger": {
            "Filter": [
              { "Name": "ByExcluding", "Args": { "expression": "IsTelemetry = true" } }
            ],
            "WriteTo": [
              {
                "Name": "Console",
                "Args": {
                  "outputTemplate": "[{Environment}] [{CorrelationId}] [{PipelineName}] {Message:lj}{NewLine}{Exception}"
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

### 11.4 DebugOn

Set `pipe.DebugOn = true` for detailed pipeline execution traces during development:

```csharp
var pipe = new DataPipe<T> { DebugOn = true };
```

---

## 12. Controllers and Services — ASP.NET Web API Integration

### 12.1 Service Interface Pattern

Define one `Invoke` overload per operation:

```csharp
public interface IOrderService
{
    Task Invoke(ListOrdersMessage msg);
    Task Invoke(CreateOrderMessage msg);
    Task Invoke(UpdateOrderMessage msg);
    Task Invoke(DeleteOrderMessage msg);
}
```

### 12.2 Service Implementation

Services are registered as **singletons** in DI. `DataPipe<T>` instances are always created **locally per invocation** — never injected:

```csharp
public class OrderService(ILogger<OrderService> logger) : IOrderService
{
    public async Task Invoke(CreateOrderMessage msg)
    {
        var env = AppSettings.Instance.DeploymentEnvironment;
        var policy = new CompositeTelemetryPolicy(
            new MinimumDurationPolicy(AppSettings.Instance.TelemetryMinDuration),
            new RolePolicy(AppSettings.Instance.TelemetryRole),
            new ExcludeStartEventsPolicy(AppSettings.Instance.TelemetryExcludeStartEvents));
        var adapter = new StructuredJsonTelemetryAdapter(logger, policy);

        var pipe = new DataPipe<CreateOrderMessage>
        {
            Name = "CreateOrder",
            TelemetryMode = AppSettings.Instance.TelemetryMode
        };

        pipe.Use(new ErrorResponseAspect<CreateOrderMessage>());
        pipe.Use(new LoggingAspect<CreateOrderMessage>(logger, "CreateOrder", env));
        pipe.Use(new TelemetryAspect<CreateOrderMessage>(adapter));

        pipe.Add(
            new ValidateOrder(),
            new OnTimeoutRetry<CreateOrderMessage>(AppSettings.Instance.MaxRetries,
                new StartTransaction<CreateOrderMessage>(
                    new OpenSqlConnection<CreateOrderMessage>(AppSettings.Instance.ConnectionString,
                        new SaveOrder(),
                        new SaveOrderLines(),
                        new CaptureChanges<CreateOrderMessage>()))));

        await pipe.Invoke(msg);
    }

    public async Task Invoke(ListOrdersMessage msg)
    {
        var pipe = new DataPipe<ListOrdersMessage> { Name = "ListOrders" };
        pipe.Use(new ErrorResponseAspect<ListOrdersMessage>());
        pipe.Use(new LoggingAspect<ListOrdersMessage>(logger));

        pipe.Add(
            new ValidateListRequest(),
            new OnTimeoutRetry<ListOrdersMessage>(3,
                new OpenSqlConnection<ListOrdersMessage>(AppSettings.Instance.ConnectionString,
                    new LoadOrders())));

        await pipe.Invoke(msg);
    }
}
```

### 12.3 Controller Pattern

```csharp
[ApiController]
[Route("api/orders")]
public class OrdersController(IOrderService orderService) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> CreateOrder([FromBody] CreateOrderRequest request)
    {
        var msg = new CreateOrderMessage
        {
            CancellationToken = HttpContext.RequestAborted,
            Actor = User.Identity?.Name ?? "anonymous",
            Service = new ServiceIdentity
            {
                Name = "Orders.Api",
                Environment = "Production",
                Version = "1.0.0",
                InstanceId = Environment.MachineName
            },
            CustomerId = request.CustomerId,
            Lines = request.Lines
        };

        await orderService.Invoke(msg);

        if (msg.IsSuccess)
            return Ok(msg.Result);

        return Problem(detail: msg.StatusMessage, statusCode: msg.StatusCode);
    }

    [HttpGet]
    public async Task<IActionResult> ListOrders([FromQuery] ListOrdersRequest request)
    {
        var msg = new ListOrdersMessage { Request = request };
        await orderService.Invoke(msg);
        return Ok(msg.Result);
    }
}
```

### 12.4 Message Hydration Patterns

**Pattern 1: Manual (in controller)**

```csharp
var msg = new CreateOrderMessage
{
    CancellationToken = HttpContext.RequestAborted,
    Actor = User.Identity?.Name,
    Service = ServiceIdentity,
    CorrelationId = Guid.NewGuid()
};
```

**Pattern 2: Request.ToMessage() extension**

```csharp
var msg = request.ToMessage(User, ServiceIdentity, HttpContext.RequestAborted);
```

**Pattern 3: [InitMessage] ActionFilter**

An attribute that runs before the action, hydrating the message from JWT claims:

```csharp
[InitMessage]
[HttpPost]
public async Task<IActionResult> CreateOrder(CreateOrderMessage msg)
{
    await orderService.Invoke(msg);
    return msg.Result.Response();  // extension method returning ActionResult
}
```

### 12.5 DI Registration

```csharp
builder.Services.AddSingleton<IOrderService, OrderService>();
builder.Services.AddSingleton<AppSettings>();
// DataPipe<T> is NEVER registered — always created locally per invocation
```

---

## 13. Console Application Integration

### 13.1 Console Pipeline Pattern

```csharp
public static class OrderPipelines
{
    public static async Task ProcessOrders(OrderMessage msg)
    {
        var policy = new CompositeTelemetryPolicy(
            new MinimumDurationPolicy(50),
            new ExcludeStartEventsPolicy(true));
        var adapter = new StructuredJsonTelemetryAdapter(msg.Logger, policy);

        var pipe = new DataPipe<OrderMessage>
        {
            Name = "ProcessOrders",
            TelemetryMode = AppSettings.Instance.TelemetryMode
        };

        // Custom aspect: logs errors to SQL database
        pipe.Use(new DatabaseLoggingAspect<OrderMessage>());
        pipe.Use(new LoggingAspect<OrderMessage>(msg.Logger, "PROCESS_ORDERS", mode: PipeLineLogMode.Full));
        pipe.Use(new TelemetryAspect<OrderMessage>(adapter));

        pipe.Add(
            new PurgeOldFiles<OrderMessage>(path),
            new OnTimeoutRetry<OrderMessage>(maxRetries,
                new OpenSqlConnection<OrderMessage>(connectionString,
                    new RepeatUntil<OrderMessage>(msg => msg.NothingToDo,
                        new ResetState(),
                        new GetNextBatch(),
                        new ProcessBatch()))));

        // Guaranteed cleanup
        pipe.Finally(new FlushLog<OrderMessage>());

        await pipe.Invoke(msg);
    }
}
```

### 13.2 Lifecycle Callbacks (Console Apps)

Console apps often use lifecycle callbacks instead of HTTP status codes:

```csharp
var msg = new OrderMessage
{
    OnStart = m => logger.LogInformation("Pipeline started"),
    OnSuccess = m => logger.LogInformation("Pipeline completed successfully"),
    OnError = (m, ex) => logger.LogError(ex, "Pipeline failed"),
    OnComplete = m => logger.LogInformation("Pipeline finished")
};

await OrderPipelines.ProcessOrders(msg);
```

### 13.3 Command-Line Dispatch

```csharp
// Program.cs
var command = args[0];
switch (command)
{
    case "/orders":
        await OrderPipelines.FetchOrders(new OrderMessage { ... });
        await OrderPipelines.ProcessOrders(new CrmOrderMessage { ... });
        break;
    case "/stock":
        await StockPipelines.SendUpdates(new StockMessage { ... });
        break;
}
```

---

## 14. Concurrency and Parallel Execution

Pipelines are stateless and messages are isolated per invocation. Standard .NET concurrency tools work naturally:

```csharp
// Parallel.ForEachAsync
var messages = urls.Select(url => new ScrapeMessage { Url = url }).ToList();
await Parallel.ForEachAsync(messages, async (msg, ct) =>
{
    await pipeline.Invoke(msg);
});

// Task.WhenAll
await Task.WhenAll(messages.Select(msg => pipeline.Invoke(msg)));
```

**DataPipe scales by processing more messages, not by making individual pipelines more complex.** Thread scheduling, degree-of-parallelism, and work distribution are left to the host application.

### 14.1 Optimistic Concurrency

Implement optimistic concurrency checking as a custom filter pattern, not a built-in feature. Define properties on your context or message:

```csharp
public class ConcurrencyCommand<TResult> : AppCommandContext<TResult>
    where TResult : CommonResult, new()
{
    public DateTime OriginalChangeDate { get; set; }
    public DateTime CurrentChangeDate { get; set; }
    public bool OverwriteConflict { get; set; }
}
```

Then create a `CheckConcurrency` filter that compares dates and either stops execution or allows overwrite.

---

## 15. Entity Framework Integration

DataPipe does not ship EF filters in the core package. Use these ready-made patterns from the Docs/Patterns directory.

### 15.1 IUseDbContext Contract

```csharp
public interface IUseDbContext
{
    DbContext DbContext { get; set; }
}
```

### 15.2 Context Setup for EF

```csharp
public class AppContext<TResult> : BaseMessage, IUseDbContext, IAmCommittable
    where TResult : CommonResult, new()
{
    public DbContext DbContext { get; set; } = default!;
    public bool Commit { get; set; } = false;
    public TResult Result { get; set; } = new TResult();
}
```

### 15.3 OpenDbContext\<T\> Filter

Opens a `DbContext` for the duration of child filter execution, then disposes it:

```csharp
pipe.Add(new OpenDbContext<OrderMessage>(
    msg => new AppDbContext(options),   // factory creates context per invocation
    new ProcessOrder(),
    new UpdateInventory()
));
```

**Implementation:** The `OpenDbContext<T>` filter implements `IAmStructural` with `EmitTelemetryEvent = false`. It creates the DbContext via the factory, assigns it to `msg.DbContext`, executes child filters, then disposes the context. Full implementation is in `Docs/Patterns/EntityFramework/OpenDbContext.md`.

### 15.4 StartEfTransaction\<T\> Filter

Wraps child filters in an EF Core transaction. Commits if `msg.Commit` is true and execution succeeds; rolls back otherwise:

```csharp
pipe.Add(new OpenDbContext<OrderMessage>(msg => new AppDbContext(options),
    new StartEfTransaction<OrderMessage>(
        new ProcessOrder(),
        new UpdateInventory(),
        new MarkCommit()  // Sets msg.Commit = true
    )));
```

Supports explicit isolation levels:

```csharp
new StartEfTransaction<OrderMessage>(IsolationLevel.Serializable,
    new ProcessOrder())
```

Full implementation is in `Docs/Patterns/EntityFramework/StartEfTransaction.md`.

### 15.5 Using EF in Filters

```csharp
public class SaveOrder : Filter<OrderMessage>
{
    public async Task Execute(OrderMessage msg)
    {
        var context = (AppDbContext)msg.DbContext;
        context.Orders.Add(new OrderEntity
        {
            CustomerId = msg.CustomerId,
            Total = msg.Lines.Sum(l => l.Price * l.Quantity)
        });
        await context.SaveChangesAsync(msg.CancellationToken);
    }
}
```

---

## 16. SFTP Integration

Use the `OpenSftpConnection<T>` pattern from `Docs/Patterns/Sftp/OpenSftpConnection.md`.

### 16.1 IUseSftp Contract

```csharp
public interface IUseSftp
{
    SftpClient SftpClient { get; set; }
}
```

### 16.2 Usage

```csharp
var pipe = new DataPipe<FileTransferMessage>();
pipe.Use(new ExceptionAspect<FileTransferMessage>());
pipe.Add(new OpenSftpConnection<FileTransferMessage>(
    msg => new Credentials
    {
        Host = "sftp.example.com",
        Port = 22,
        UserName = "user",
        AuthMethod = new PasswordAuthenticationMethod("user", "pass")
    },
    new UploadFile(),
    new DownloadFile()
));
```

The `OpenSftpConnection<T>` filter implements `IAmStructural`, manages connection lifecycle, and emits telemetry events. Full implementation is in the Docs.

---

## 17. OpenTelemetry Integration

Use the `OpenTelemetryAdapter` pattern from `Docs/Patterns/Telemetry/OpenTelemetryAspect.md` to export DataPipe telemetry as OpenTelemetry spans.

### 17.1 Setup

```csharp
var tracerProvider = Sdk.CreateTracerProviderBuilder()
    .AddSource("DataPipe")
    .AddConsoleExporter()           // or AddOtlpExporter(), AddJaegerExporter(), etc.
    .Build();

var tracer = tracerProvider.GetTracer("DataPipe");
var adapter = new OpenTelemetryAdapter(tracer);

var pipe = new DataPipe<OrderMessage>
{
    Name = "CreateOrder",
    TelemetryMode = TelemetryMode.PipelineAndFilters
};

pipe.Use(new ExceptionAspect<OrderMessage>());
pipe.Use(new TelemetryAspect<OrderMessage>(adapter));
pipe.Add(new SaveOrder());
```

### 17.2 How It Works

The `OpenTelemetryAdapter` implements `ITelemetryAdapter`:

- On `TelemetryPhase.Start` events → starts a new OTel span with pipeline attributes
- On `TelemetryPhase.End` events → ends the span, sets status and duration
- Spans include: `pipeline.name`, `component.role`, `telemetry.scope`, `service.name`, `service.environment`, `service.version`, `service.instance.id`, `duration.ms`
- Error outcomes set `Status.Error` with the reason

### 17.3 Custom Adapters

Implement `ITelemetryAdapter` for any telemetry destination:

```csharp
public interface ITelemetryAdapter
{
    void Handle(TelemetryEvent evt);
    void Flush();
}
```

`Handle` is called per event during execution. `Flush` is called once when the pipeline completes.

---

## 18. Inner / Nested Pipelines

A filter can create and invoke a sub-pipeline inside its `Execute()` method:

```csharp
public class GetDayStats : Filter<StatsMessage>
{
    private readonly ILogger _logger;

    public GetDayStats(ILogger logger) { _logger = logger; }

    public async Task Execute(StatsMessage msg)
    {
        var innerMsg = new PalletAndBoxesMessage();
        innerMsg.CopyClaimsFrom(msg);  // propagate user context

        var innerPipe = new DataPipe<PalletAndBoxesMessage>();
        innerPipe.Use(new ExceptionAspect<PalletAndBoxesMessage>());
        innerPipe.Use(new LoggingAspect<PalletAndBoxesMessage>(_logger));
        innerPipe.Add(new ValidatePalletAndBoxesMessage());
        innerPipe.Add(new GetPalletAndBoxesStats());
        await innerPipe.Invoke(innerMsg);

        // Copy results back to outer message
        msg.Result.PalletStats = innerMsg.Result;
    }
}
```

**Guidelines for inner pipelines:**
- Use `CopyClaimsFrom(msg)` to propagate user context
- Inner pipes typically have aspects (error + logging) but often skip telemetry
- Inner pipes don't need a `Name` (not tracked at the outer telemetry level)
- Keep nesting shallow — one level of inner pipeline is the practical limit

---

## 19. Complete Real-World API Example

This demonstrates building a full-featured API endpoint from scratch.

### 19.1 Context and Result

```csharp
// Shared result base
public class CommonResult
{
    public bool Success { get; set; } = true;
    public string StatusMessage { get; set; } = string.Empty;
    public int StatusCode { get; set; } = 200;
}

// Application context base
public class SalesContext : BaseMessage, IUseSqlCommand, IAmRetryable
{
    public SqlCommand Command { get; set; }
    public int Attempt { get; set; }
    public int MaxRetries { get; set; }
    public Action<int> OnRetrying { get; set; } = _ => { };
    public UserPrincipal User { get; set; } = new();
}

// Generic with strongly-typed result
public class SalesContext<TResult> : SalesContext where TResult : CommonResult, new()
{
    public TResult Result { get; set; } = new TResult();
}

// Write operations base with transactions
public class SalesCommandContext<TResult> : SalesContext<TResult>, IAmCommittable
    where TResult : CommonResult, new()
{
    public bool Commit { get; set; } = true;
    public List<Change> Changes { get; set; } = new();
}
```

### 19.2 Specific Message and Result

```csharp
public class InvoiceResult : CommonResult
{
    public string InvoiceNumber { get; set; }
    public decimal Total { get; set; }
    public bool RequiresManualReview { get; set; }
}

public class CreateInvoiceMessage : SalesCommandContext<InvoiceResult>
{
    public string SupplierId { get; set; }
    public List<InvoiceLine> Lines { get; set; } = new();
    public bool IsValid { get; set; }
}
```

### 19.3 Custom Error Aspect

```csharp
public class ErrorResponseAspect<T> : Aspect<T> where T : SalesContext
{
    public Aspect<T> Next { get; set; }

    public async Task Execute(T msg)
    {
        try
        {
            await Next.Execute(msg);
        }
        catch (ValidationException ex)
        {
            msg.Fail(422, ex.Message);
        }
        catch (ConflictException ex)
        {
            msg.Fail(409, ex.Message);
        }
        catch (NotFoundException ex)
        {
            msg.Fail(404, ex.Message);
        }
        catch (Exception ex)
        {
            msg.Fail(500, "An internal error occurred");
        }
    }
}
```

### 19.4 Filters

```csharp
public class ValidateInvoice : Filter<CreateInvoiceMessage>
{
    public Task Execute(CreateInvoiceMessage msg)
    {
        if (string.IsNullOrEmpty(msg.SupplierId))
        {
            msg.Fail(400, "Supplier ID is required");
            msg.Execution.Stop("Validation failed");
            return Task.CompletedTask;
        }
        msg.IsValid = msg.Lines.Any();
        if (!msg.IsValid)
        {
            msg.Fail(400, "Invoice must have at least one line");
            msg.Execution.Stop("Validation failed");
        }
        return Task.CompletedTask;
    }
}

public class SaveInvoice : Filter<CreateInvoiceMessage>
{
    public async Task Execute(CreateInvoiceMessage msg)
    {
        msg.OnLog?.Invoke($"Saving invoice for supplier {msg.SupplierId}");
        msg.Command.CommandText = @"
            INSERT INTO dbo.Invoices (SupplierId, Total, CreatedBy)
            VALUES (@SupplierId, @Total, @Actor);
            SELECT CAST(SCOPE_IDENTITY() as int);";
        msg.Command.Parameters.Clear();
        msg.Command.Parameters.AddWithValue("@SupplierId", msg.SupplierId);
        msg.Command.Parameters.AddWithValue("@Total", msg.Lines.Sum(l => l.Amount));
        msg.Command.Parameters.AddWithValue("@Actor", msg.Actor);
        var id = (int)await msg.Command.ExecuteScalarAsync(msg.CancellationToken);
        msg.Result.InvoiceNumber = $"INV-{id:D6}";
        msg.Result.Total = msg.Lines.Sum(l => l.Amount);
    }
}
```

### 19.5 Service

```csharp
public class InvoiceService(ILogger<InvoiceService> logger) : IInvoiceService
{
    public async Task Invoke(CreateInvoiceMessage msg)
    {
        var env = AppSettings.Instance.DeploymentEnvironment;
        var policy = new CompositeTelemetryPolicy(
            new MinimumDurationPolicy(AppSettings.Instance.TelemetryMinDuration),
            new RolePolicy(AppSettings.Instance.TelemetryRole),
            new ExcludeStartEventsPolicy(AppSettings.Instance.TelemetryExcludeStartEvents));
        var adapter = new StructuredJsonTelemetryAdapter(logger, policy);

        var pipe = new DataPipe<CreateInvoiceMessage>
        {
            Name = "CreateInvoice",
            TelemetryMode = AppSettings.Instance.TelemetryMode
        };

        pipe.Use(new ErrorResponseAspect<CreateInvoiceMessage>());
        pipe.Use(new LoggingAspect<CreateInvoiceMessage>(logger, "CreateInvoice", env));
        pipe.Use(new TelemetryAspect<CreateInvoiceMessage>(adapter));

        pipe.Add(
            new ValidateInvoice(),
            new VerifySupplierExists(),
            new Policy<CreateInvoiceMessage>(msg =>
            {
                if (!msg.IsValid) return new RejectInvoice();
                return msg.Result.RequiresManualReview
                    ? new RouteToApprovalQueue()
                    : new OnTimeoutRetry<CreateInvoiceMessage>(AppSettings.Instance.MaxRetries,
                        new StartTransaction<CreateInvoiceMessage>(
                            new OpenSqlConnection<CreateInvoiceMessage>(AppSettings.Instance.ConnectionString,
                                new SaveInvoice(),
                                new SaveInvoiceLines(),
                                new UpdateAccountsPayable())));
            }),
            new SendSupplierNotification());

        await pipe.Invoke(msg);
    }
}
```

### 19.6 Controller

```csharp
[ApiController]
[Route("api/invoices")]
public class InvoiceController(IInvoiceService invoiceService) : ControllerBase
{
    private static readonly ServiceIdentity ServiceId = new()
    {
        Name = "Sales.Api",
        Environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Development",
        Version = typeof(InvoiceController).Assembly.GetName().Version?.ToString() ?? "1.0.0",
        InstanceId = Environment.MachineName
    };

    [HttpPost]
    public async Task<IActionResult> CreateInvoice([FromBody] CreateInvoiceRequest request)
    {
        var msg = new CreateInvoiceMessage
        {
            CancellationToken = HttpContext.RequestAborted,
            Actor = User.Identity?.Name ?? "anonymous",
            Service = ServiceId,
            SupplierId = request.SupplierId,
            Lines = request.Lines.Select(l => new InvoiceLine { ... }).ToList()
        };

        await invoiceService.Invoke(msg);

        if (msg.IsSuccess)
            return Ok(msg.Result);

        return Problem(detail: msg.StatusMessage, statusCode: msg.StatusCode);
    }
}
```

### 19.7 DI Registration

```csharp
// Program.cs
builder.Services.AddSingleton<AppSettings>();
builder.Services.AddSingleton<IInvoiceService, InvoiceService>();
```

---

## 20. Execution Flow Summary

```
Pipeline.Invoke(msg)
  → msg.OnStart?.Invoke(msg)
  → Aspects execute (outermost first, chained inward)
    → Main filters execute in registration order
      → Each filter can:
        - Read/modify msg properties
        - Call msg.OnLog?.Invoke() for logging
        - Call msg.Fail(code, message) for error status
        - Call msg.Execution.Stop(reason?) to halt pipeline
        - Throw exceptions (caught by error aspect)
    → Finally filters execute (even on error/stop)
  → Aspects complete (innermost first)
  → msg.OnComplete?.Invoke(msg)
  → msg.OnSuccess?.Invoke(msg) or msg.OnError?.Invoke(msg, ex)
→ msg reflects final state
→ Check msg.IsSuccess / msg.StatusCode / msg.Result
```

---

## 21. Quick Reference — All Built-in Components

### Filters (DataPipe.Core)

| Filter | Constraint | Purpose |
|--------|-----------|---------|
| `IfTrue<T>(predicate, ifTrue, ifFalse?)` | `BaseMessage` | Runtime conditional execution |
| `Policy<T>(selector)` | `BaseMessage` | Multi-branch runtime dispatch |
| `Sequence<T>(filters...)` | `BaseMessage` | Group filters as one logical step |
| `OnTimeoutRetry<T>(max, filters...)` | `IAmRetryable` | Retry on timeout |
| `RepeatUntil<T>(predicate, filters...)` | `BaseMessage` | Loop until predicate true |
| `Repeat<T>(filters...)` | `BaseMessage` | Infinite loop (break via Stop) |
| `ForEach<TMsg, TItem>(selector, setter, filters...)` | `BaseMessage` | Iterate collection |

### Filters (DataPipe.Sql)

| Filter | Constraint | Purpose |
|--------|-----------|---------|
| `OpenSqlConnection<T>(connStr, filters...)` | `IUseSqlCommand` | Scoped SQL connection |
| `StartTransaction<T>(filters...)` | `IAmCommittable` + `IUseSqlCommand` | SQL transaction |
| `StartTransaction<T>(isolationLevel, filters...)` | `IAmCommittable` + `IUseSqlCommand` | Transaction with isolation level |

### Aspects

| Aspect | Purpose |
|--------|---------|
| `ExceptionAspect<T>` | Basic exception handling |
| `BasicConsoleLoggingAspect<T>(title?)` | Console logging |
| `LoggingAspect<T>(logger, title?, env?, level?, mode?)` | Structured ILogger logging |
| `TelemetryAspect<T>(adapter)` | Telemetry forwarding |

### Contracts

| Contract | Package | Properties |
|----------|---------|-----------|
| `IUseSqlCommand` | DataPipe.Sql | `SqlCommand Command` |
| `IAmCommittable` | DataPipe.Core | `bool Commit` |
| `IAmRetryable` | DataPipe.Core | `int Attempt`, `int MaxRetries`, `Action<int> OnRetrying` |

### Telemetry Adapters

| Adapter | Package | Purpose |
|---------|---------|---------|
| `ConsoleTelemetryAdapter` | Core | JSON to console |
| `StructuredJsonTelemetryAdapter` | Core | JSON to ILogger |

### Telemetry Policies

| Policy | Purpose |
|--------|---------|
| `DefaultCaptureEverythingPolicy` | Include all events |
| `MinimumDurationPolicy(ms)` | Filter by duration |
| `RolePolicy(role)` | Filter by Business/Structural |
| `ExcludeStartEventsPolicy(bool)` | Drop start events |
| `SuppressAllExceptErrorsPolicy(name)` | Errors only for named pipeline |
| `CompositeTelemetryPolicy(policies...)` | Combine policies (all must pass) |

### Pattern Implementations (from Docs/Patterns — copy into your project)

| Pattern | Purpose |
|---------|---------|
| `OpenDbContext<T>` | Entity Framework DbContext lifecycle |
| `StartEfTransaction<T>` | Entity Framework transaction |
| `OpenSftpConnection<T>` | SFTP connection lifecycle (Renci.SshNet) |
| `OpenTelemetryAdapter` | OpenTelemetry span export |
| `FileLoggingTelemetryAdapter` | Batched JSON telemetry to ILogger/file |
