# Validation Collector

When a pipeline validates input, you often want to report **all** validation failures at once rather than stopping at the first one. The validation collector pattern lets multiple filters add errors independently, then a single gate filter stops the pipeline if any errors were found.

## The problem

With the basic `Stop()` approach, only the first validation failure is reported:

```csharp
pipeline.Add(new ValidateName());   // stops → "Name is required"
pipeline.Add(new ValidateEmail());  // never runs
pipeline.Add(new ValidateAge());    // never runs
pipeline.Add(new SaveCustomer());
```

The caller only sees "Name is required" and must fix and resubmit to discover "Email is required". This creates a frustrating back-and-forth.

## Collecting errors on the message

Every `BaseMessage` has a built-in `ValidationErrors` list and a `HasValidationErrors` property:

```csharp
public List<string> ValidationErrors { get; }   // starts empty
public bool HasValidationErrors => ValidationErrors.Count > 0;
```

Validation filters add errors without stopping the pipeline:

```csharp
public class ValidateName : Filter<RegisterCustomerMessage>
{
    public Task Execute(RegisterCustomerMessage msg)
    {
        if (string.IsNullOrWhiteSpace(msg.Name))
            msg.ValidationErrors.Add("Name is required");

        return Task.CompletedTask;
    }
}

public class ValidateEmail : Filter<RegisterCustomerMessage>
{
    public Task Execute(RegisterCustomerMessage msg)
    {
        if (string.IsNullOrWhiteSpace(msg.Email))
            msg.ValidationErrors.Add("Email is required");
        else if (!msg.Email.Contains("@"))
            msg.ValidationErrors.Add("Email is not valid");

        return Task.CompletedTask;
    }
}
```

Because none of these filters call `Stop()`, every validator runs and the full set of errors is collected.

## Stopping after validation

Place `StopIfValidationErrors<T>` after your validation filters. If any errors have been collected, it joins them into a combined message, sets the status code, and stops the pipeline:

```csharp
var pipeline = new DataPipe<RegisterCustomerMessage>();

pipeline.Use(new ExceptionAspect<RegisterCustomerMessage>());

pipeline.Add(new ValidateName());
pipeline.Add(new ValidateEmail());
pipeline.Add(new ValidateAge());
pipeline.Add(new StopIfValidationErrors<RegisterCustomerMessage>());

// Everything below only runs if all validations pass
pipeline.Add(new SaveCustomer());
pipeline.Add(new SendWelcomeEmail());

await pipeline.Invoke(msg);
```

If `Name` and `Email` both fail, the message ends up with:

- `msg.StatusCode` → `400`
- `msg.Execution.Reason` → `"Name is required; Email is required"`
- `msg.ValidationErrors` → `["Name is required", "Email is required"]`
- `msg.Execution.IsStopped` → `true`

The `SaveCustomer` and `SendWelcomeEmail` filters are skipped.

If no errors were collected, `StopIfValidationErrors` does nothing and the pipeline continues.

## Returning validation errors to the caller

After `Invoke()`, inspect the message as you normally would:

```csharp
await pipeline.Invoke(msg);

if (msg.HasValidationErrors)
{
    // Return all errors to the API consumer
    return BadRequest(new
    {
        errors = msg.ValidationErrors
    });
}

return Ok(msg.Result);
```

The `ValidationErrors` list is available after invocation, giving you full control over how errors are presented.

## Custom status code and separator

The default status code is `400` and errors are joined with `"; "`. Both are configurable:

```csharp
pipeline.Add(new StopIfValidationErrors<OrderMessage>(
    statusCode: 422,
    separator: " | "));
```

Use `422 Unprocessable Entity` when you prefer it over `400 Bad Request`. Use a custom separator when the consuming application parses the combined string.

## Mixed validation — some errors stop, some collect

You can mix both approaches. Critical validations that should halt immediately can still call `Stop()` directly, while softer field-level validations use the collector:

```csharp
pipeline.Add(new RejectIfUnauthorized());         // calls Stop() — hard gate
pipeline.Add(new ValidateName());                  // adds to ValidationErrors
pipeline.Add(new ValidateEmail());                 // adds to ValidationErrors
pipeline.Add(new ValidateAge());                   // adds to ValidationErrors
pipeline.Add(new StopIfValidationErrors<Msg>());   // soft gate
pipeline.Add(new SaveCustomer());
```

If the authorization check stops the pipeline, the field validators never run. If authorization passes, all field validators run and their errors are collected.

## Multiple validation gates

For complex pipelines, you can place multiple `StopIfValidationErrors` filters at different stages:

```csharp
// Phase 1: Input validation
pipeline.Add(new ValidateRequiredFields());
pipeline.Add(new ValidateFieldFormats());
pipeline.Add(new StopIfValidationErrors<OrderMessage>());

// Phase 2: Business rule validation (only if input is valid)
pipeline.Add(new CheckInventoryAvailable());
pipeline.Add(new CheckCreditLimit());
pipeline.Add(new StopIfValidationErrors<OrderMessage>());

// Phase 3: Processing (only if business rules pass)
pipeline.Add(new ReserveInventory());
pipeline.Add(new ChargePayment());
```

Each gate checks the accumulated errors up to that point. If Phase 1 finds errors, Phase 2 never runs.

> **Note:** `ValidationErrors` accumulate across the entire pipeline lifetime. A second `StopIfValidationErrors` will see errors from all preceding filters, not just those since the last gate.

## Using with ForEach

When validating child items inside a `ForEach`, each child message has its own `ValidationErrors` list. Use the collector inside the child pipeline:

```csharp
pipeline.Add(new ForEach<BatchMessage, OrderMessage>(
    msg => msg.Orders,
    new ValidateOrderNumber(),
    new ValidateOrderAmount(),
    new StopIfValidationErrors<OrderMessage>(),
    new ProcessOrder()));
```

Each order is validated independently. An invalid order stops its own processing without affecting other orders in the batch.
