# Migration Guide: Traditional Architecture to DataPipe

This guide shows how to migrate from a traditional layered architecture (controller → service → repository) to vertical slices with DataPipe. Each section takes a recognisable "before" pattern and shows the equivalent "after" using DataPipe.

---

## 1. The traditional pattern

A typical .NET Web API has three horizontal layers:

```
Controller  →  Service  →  Repository
     ↓             ↓            ↓
  HTTP glue    Business logic   Data access
```

Each layer lives in its own folder or project. A single feature (e.g. "create order") touches all three layers plus interfaces, DTOs, and sometimes mapping code.

### Before — Controller

```csharp
[ApiController]
[Route("api/orders")]
public class OrdersController(IOrderService orderService) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> CreateOrder([FromBody] CreateOrderRequest request)
    {
        var result = await orderService.CreateOrderAsync(request);
        return result.Success
            ? CreatedAtAction(nameof(GetOrder), new { id = result.OrderId }, result)
            : BadRequest(result.Errors);
    }
}
```

### Before — Service

```csharp
public class OrderService(
    IOrderRepository repo,
    IInventoryService inventory,
    IEmailService email,
    ILogger<OrderService> logger) : IOrderService
{
    public async Task<CreateOrderResult> CreateOrderAsync(CreateOrderRequest request)
    {
        // Validation
        if (string.IsNullOrWhiteSpace(request.CustomerId))
            return CreateOrderResult.Fail("Customer ID required");

        if (!request.Lines.Any())
            return CreateOrderResult.Fail("At least one line required");

        // Business logic
        var order = new Order
        {
            CustomerId = request.CustomerId,
            Lines = request.Lines.Select(l => new OrderLine
            {
                ProductId = l.ProductId,
                Quantity = l.Quantity,
                UnitPrice = l.UnitPrice
            }).ToList(),
            Total = request.Lines.Sum(l => l.Quantity * l.UnitPrice)
        };

        // Persistence
        try
        {
            await repo.SaveAsync(order);
            await inventory.ReserveStockAsync(order.Lines);
            await email.SendConfirmationAsync(order);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to create order");
            return CreateOrderResult.Fail("Internal error");
        }

        return CreateOrderResult.Ok(order.Id);
    }
}
```

### Before — Repository

```csharp
public class OrderRepository(AppDbContext db) : IOrderRepository
{
    public async Task SaveAsync(Order order)
    {
        db.Orders.Add(order);
        await db.SaveChangesAsync();
    }
}
```

### What this produces

- Three classes for one feature
- An interface for each service and repository
- Validation, business logic, persistence, and error handling interleaved in the service method
- Cross-cutting concerns (logging, exception handling) coupled to the service
- Adding retry or circuit breaker requires refactoring the service or pulling in Polly

---

## 2. The DataPipe equivalent

With DataPipe, the same feature becomes a vertical slice: one message, one pipeline, purpose-built filters.

### After — Message

The message carries the request, the response, and any state the pipeline needs.

```csharp
public class CreateOrderMessage : BaseMessage, IAmCommittable, IAmRetryable
{
    // Input
    public string CustomerId { get; set; }
    public List<OrderLineDto> Lines { get; set; } = [];

    // Pipeline state
    public Order Order { get; set; }

    // Output
    public string OrderId { get; set; }

    // IAmCommittable
    public bool Commit { get; set; } = true;

    // IAmRetryable
    public int Attempt { get; set; }
    public int MaxRetries { get; set; }
    public Action<int> OnRetrying { get; set; }
}
```

### After — Filters

Each filter does exactly one thing. Each is independently testable.

```csharp
public sealed class ValidateCreateOrder : Filter<CreateOrderMessage>
{
    public Task Execute(CreateOrderMessage msg)
    {
        if (string.IsNullOrWhiteSpace(msg.CustomerId))
            msg.Execution.Stop("Customer ID required");

        if (!msg.Lines.Any())
            msg.Execution.Stop("At least one line required");

        return Task.CompletedTask;
    }
}

public sealed class BuildOrder : Filter<CreateOrderMessage>
{
    public Task Execute(CreateOrderMessage msg)
    {
        msg.Order = new Order
        {
            CustomerId = msg.CustomerId,
            Lines = msg.Lines.Select(l => new OrderLine
            {
                ProductId = l.ProductId,
                Quantity = l.Quantity,
                UnitPrice = l.UnitPrice
            }).ToList(),
            Total = msg.Lines.Sum(l => l.Quantity * l.UnitPrice)
        };
        return Task.CompletedTask;
    }
}

public sealed class SaveOrder : Filter<CreateOrderMessage>
{
    public async Task Execute(CreateOrderMessage msg)
    {
        msg.Command.CommandText = @"
            INSERT INTO Orders (CustomerId, Total, CreatedUtc)
            VALUES (@customerId, @total, SYSUTCDATETIME());
            SELECT SCOPE_IDENTITY();";
        msg.Command.Parameters.Clear();
        msg.Command.Parameters.AddWithValue("@customerId", msg.CustomerId);
        msg.Command.Parameters.AddWithValue("@total", msg.Order.Total);
        var id = await msg.Command.ExecuteScalarAsync(msg.CancellationToken);
        msg.OrderId = id.ToString();
    }
}

public sealed class ReserveStock : Filter<CreateOrderMessage>
{
    public async Task Execute(CreateOrderMessage msg)
    {
        foreach (var line in msg.Order.Lines)
        {
            msg.Command.CommandText = "UPDATE Inventory SET Reserved = Reserved + @qty WHERE ProductId = @pid";
            msg.Command.Parameters.Clear();
            msg.Command.Parameters.AddWithValue("@qty", line.Quantity);
            msg.Command.Parameters.AddWithValue("@pid", line.ProductId);
            await msg.Command.ExecuteNonQueryAsync(msg.CancellationToken);
        }
    }
}

public sealed class SendOrderConfirmation : Filter<CreateOrderMessage>
{
    public async Task Execute(CreateOrderMessage msg)
    {
        // Send email, push notification, etc.
        await Task.CompletedTask;
    }
}
```

### After — Service

The service constructs the pipeline. Each method is a self-contained vertical slice.

```csharp
public class OrderService(ILogger<OrderService> logger) : IOrderService
{
    private static readonly CircuitBreakerState _circuit = new();

    public async Task Invoke(CreateOrderMessage msg)
    {
        var pipe = new DataPipe<CreateOrderMessage> { Name = "CreateOrder" };
        pipe.Use(new ExceptionAspect<CreateOrderMessage>());
        pipe.Use(new LoggingAspect<CreateOrderMessage>(logger, "CreateOrder", "Production"));

        pipe.Add(new ValidateCreateOrder());
        pipe.Add(new BuildOrder());
        pipe.Add(
            new OnCircuitBreak<CreateOrderMessage>(_circuit,
                failureThreshold: 5,
                breakDuration: TimeSpan.FromSeconds(30),
                new OnTimeoutRetry<CreateOrderMessage>(maxRetries: 2,
                    new OpenSqlConnection<CreateOrderMessage>(connectionString,
                        new StartSqlTransaction<CreateOrderMessage>(
                            new SaveOrder(),
                            new ReserveStock())))));
        pipe.Add(new SendOrderConfirmation());

        await pipe.Invoke(msg);
    }
}
```

### After — Controller

The controller is unchanged in shape. It creates the message, calls the service, and maps the result to HTTP.

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
            CustomerId = request.CustomerId,
            Lines = request.Lines
        };

        await orderService.Invoke(msg);

        return msg.IsSuccess
            ? CreatedAtAction(nameof(GetOrder), new { id = msg.OrderId }, new { msg.OrderId })
            : BadRequest(msg.StatusMessage);
    }
}
```

---

## 3. Layer-by-layer migration map

| Traditional Layer | DataPipe Equivalent | Notes |
|---|---|---|
| **Controller** | Controller (unchanged) | Creates message, calls service, maps result to HTTP |
| **Service interface** | Service interface (unchanged) | `Task Invoke(TMessage msg)` per operation |
| **Service method body** | Pipeline construction | Aspects + filters replace imperative code |
| **Validation logic** | Validation filter | `msg.Execution.Stop()` halts the pipeline |
| **Business logic** | One or more filters | Each filter does one thing |
| **Repository / data access** | Filter inside `OpenSqlConnection` | SQL is scoped to the connection lifetime |
| **Transaction management** | `StartSqlTransaction` structural filter | Wraps multiple data-access filters atomically |
| **try/catch error handling** | `ExceptionAspect` | Catches all exceptions, sets `StatusCode = 500` |
| **Logging** | `LoggingAspect` | Wraps entire pipeline, logs start/end/error |
| **Retry logic (Polly etc.)** | `OnTimeoutRetry` structural filter | Built-in, no external dependency |
| **Circuit breaker (Polly etc.)** | `OnCircuitBreak` structural filter | Shared state across pipeline instances |
| **Conditional branching** | `Policy<T>`, `IfTrue<T>`, `IfTrueElse<T>` | Message state drives routing |
| **Background / async work** | `ForEach<T>` / `ParallelForEach<T>` | Fan-out over child messages |

---

## 4. Step-by-step migration approach

### Step 1 — Pick one feature

Choose a single, well-understood feature (e.g. "create order"). Do not try to migrate the entire application at once.

### Step 2 — Define the message

Create a message class extending `BaseMessage`. Move the request properties from the controller input DTO onto the message. Add output properties for the response. Implement `IAmCommittable` if the feature uses transactions; implement `IAmRetryable` if it needs retry.

### Step 3 — Extract filters from the service method

Read the existing service method top to bottom. Each distinct responsibility becomes a filter:
- Input validation → `ValidateX` filter
- Object construction / mapping → `BuildX` filter
- Database writes → one filter per logical write
- External API calls → one filter per call
- Notifications → `SendX` filter

### Step 4 — Compose the pipeline

In the service class, replace the method body with pipeline construction. Follow this order:
1. **Aspects** — `ExceptionAspect`, then `LoggingAspect`, then `TelemetryAspect` (if needed)
2. **Validation filters** — these run first; if they stop the pipeline, nothing else executes
3. **Business logic filters** — mapping, calculation, enrichment
4. **Infrastructure filters** — wrapped in structural filters for connection, transaction, retry, circuit breaker as needed
5. **Post-processing filters** — notifications, response mapping

### Step 5 — Update the controller

Change the controller to create the message, call `service.Invoke(msg)`, and check `msg.IsSuccess` / `msg.StatusCode` for the HTTP response.

### Step 6 — Delete the old code

Remove the repository class, its interface, and any Polly/retry infrastructure that the pipeline now replaces.

### Step 7 — Test

Test each filter in isolation (see [24-testing.md](24-testing.md)). Test the pipeline end-to-end with test doubles. The message carries all assertions — no mocking framework required for verifying outcomes.

---

## 5. Common migration patterns

### 5.1 Repository → Filter

**Before:**
```csharp
public class OrderRepository(AppDbContext db) : IOrderRepository
{
    public async Task SaveAsync(Order order)
    {
        db.Orders.Add(order);
        await db.SaveChangesAsync();
    }
}
```

**After:**
```csharp
public sealed class SaveOrder : Filter<CreateOrderMessage>
{
    public async Task Execute(CreateOrderMessage msg)
    {
        msg.Command.CommandText = "INSERT INTO Orders ...";
        await msg.Command.ExecuteNonQueryAsync(msg.CancellationToken);
    }
}
```

If you prefer Entity Framework, pass a `DbContext` through the message or constructor. See the `Docs/Patterns/EntityFramework` directory for ready-made patterns.

### 5.2 Polly retry → OnTimeoutRetry

**Before:**
```csharp
var retryPolicy = Policy
    .Handle<SqlException>(ex => ex.IsTransient)
    .WaitAndRetryAsync(3, attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt)));

await retryPolicy.ExecuteAsync(() => repository.SaveAsync(order));
```

**After:**
```csharp
pipe.Add(
    new OnTimeoutRetry<CreateOrderMessage>(maxRetries: 3,
        new OpenSqlConnection<CreateOrderMessage>(connectionString,
            new SaveOrder())));
```

No external package. No policy configuration object. The retry is visible in the pipeline definition.

### 5.3 Polly circuit breaker → OnCircuitBreak

**Before:**
```csharp
var circuitPolicy = Policy
    .Handle<SqlException>()
    .CircuitBreakerAsync(5, TimeSpan.FromSeconds(30));
```

**After:**
```csharp
// Shared state — register as singleton in DI
private static readonly CircuitBreakerState _circuit = new();

// In the pipeline
pipe.Add(
    new OnCircuitBreak<CreateOrderMessage>(_circuit,
        failureThreshold: 5,
        breakDuration: TimeSpan.FromSeconds(30),
        new SaveOrder()));
```

### 5.4 if/else branching → Policy or IfTrueElse

**Before:**
```csharp
if (order.RequiresApproval)
    await routeToApproval(order);
else
    await processImmediately(order);
```

**After:**
```csharp
pipe.Add(new Policy<OrderMessage>(msg =>
    msg.RequiresApproval
        ? new RouteToApproval()
        : new ProcessImmediately()));
```

### 5.5 foreach loop → ForEach / ParallelForEach

**Before:**
```csharp
foreach (var line in order.Lines)
{
    await processLine(line);
}
```

**After (sequential):**
```csharp
pipe.Add(new ForEach<OrderMessage, OrderLineMessage>(
    msg => msg.Lines,
    (parent, child) => child.ConnectionString = parent.ConnectionString,
    new ProcessOrderLine()));
```

**After (parallel — one-line swap):**
```csharp
pipe.Add(new ParallelForEach<OrderMessage, OrderLineMessage>(
    msg => msg.Lines,
    (parent, child) => child.ConnectionString = parent.ConnectionString,
    new ProcessOrderLine()));
```

---

## 6. What you gain

| Concern | Traditional | DataPipe |
|---|---|---|
| **Execution flow** | Scattered across layers; requires mental assembly | Visible top to bottom in one pipeline definition |
| **Testability** | Mock interfaces, DI containers, integration tests | Instantiate filter, call Execute, assert on message |
| **Resilience** | External libraries (Polly), separate configuration | Built-in, composable, visible in the pipeline |
| **Transaction scope** | Manual `using` blocks or `TransactionScope` | Declarative structural filter wrapping |
| **Adding a step** | Touch service, possibly repository and interface | Add one filter, insert at the right position |
| **Removing a step** | Touch service, possibly repository and interface | Remove one `Add` call |
| **Cross-cutting concerns** | Scattered or buried in middleware | Aspects wrap the pipeline explicitly |

---

## 7. When to keep the traditional pattern

DataPipe is not a universal replacement. Keep traditional patterns when:

- **CRUD with no business logic** — if the operation is a straight passthrough to the database with no validation, branching, or side effects, a simple repository call is simpler
- **Complex domain logic** — if the feature is primarily complex domain logic with rich invariants, a DDD-style domain model should still be used but can still be instantiated and utilised within a filter in the pipeline

For everything else — features with validation, branching, persistence, error handling, and cross-cutting concerns — DataPipe produces clearer, more testable, more resilient code.
