# Stopping Execution and Failure Handling

Any filter can stop the pipeline early. This is how you handle validation failures, authorization checks, short-circuit responses, and graceful exits.

## Stopping the pipeline

Call `msg.Execution.Stop()` to halt execution. Remaining filters are skipped, but aspects still complete normally.

```csharp
public class ValidateApiKey : Filter<RequestMessage>
{
    public Task Execute(RequestMessage msg)
    {
        if (string.IsNullOrEmpty(msg.ApiKey))
        {
            msg.StatusCode = 401;
            msg.StatusMessage = "API key is required";
            msg.Execution.Stop(msg.StatusMessage);
        }

        return Task.CompletedTask;
    }
}
```

The optional reason string is stored on `msg.Execution.Reason` and included in log output and telemetry events.

## Stopping with success

`Stop()` isn't only for errors. You can stop the pipeline when the work is already done:

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
            msg.Execution.Stop(); // No need to continue — cached response is ready
        }

        return Task.CompletedTask;
    }
}
```

## Setting failure status with Fail()

`Fail(code, message)` is a convenience method that sets both `StatusCode` and `StatusMessage` in one call. It does **not** stop the pipeline — combine it with `Execution.Stop()` when you want to halt:

```csharp
public class LoadUser : Filter<AuthMessage>
{
    public async Task Execute(AuthMessage msg)
    {
        var user = await FindUser(msg.Username);

        if (user == null)
        {
            msg.Fail(401, "Invalid credentials");
            msg.Execution.Stop(msg.StatusMessage);
            return;
        }

        msg.Result.UserId = user.Id;
    }
}
```

## Checking execution state

The pipeline engine uses `msg.ShouldStop` internally to decide whether to continue. This property combines both checks:

```csharp
public bool ShouldStop => Execution.IsStopped || CancellationToken.IsCancellationRequested;
```

You can use it in your own code when needed:

```csharp
if (msg.ShouldStop) return;
```

## Checking the result after invocation

After `pipe.Invoke(msg)` completes, inspect the message to determine what happened:

```csharp
await pipe.Invoke(msg);

if (msg.IsSuccess)
{
    return new OkObjectResult(msg.Result);
}

return Problem(detail: msg.StatusMessage, statusCode: msg.StatusCode);
```

`IsSuccess` returns `true` when `StatusCode < 400`.

## What happens when execution stops

- The current filter completes normally
- All subsequent filters are skipped
- `Finally` filters still execute
- Aspects unwind as usual (logging, telemetry, exception handling)
- Telemetry records the outcome as `Stopped` with the reason

## Execution.Reset()

`ExecutionContext` also exposes `Reset()`, which clears the stopped state and reason. This is used internally by structural filters like `Repeat<T>` and `RepeatUntil<T>` to allow loop control via `Stop()` without permanently halting the pipeline.

## Pipeline with stop and finally

```csharp
public async Task ProcessRefund(RefundMessage msg)
{
    var pipeline = new DataPipe<RefundMessage>();

    pipeline.Use(new ExceptionAspect<RefundMessage>());
    pipeline.Use(new LoggingAspect<RefundMessage>(logger, "Refund", env));

    pipeline.Add(
        new ValidateRefundRequest(),
        new CheckRefundEligibility(),
        new ProcessPaymentReversal(),
        new NotifyCustomer()
    );

    // Always log the audit record, even if the pipeline stopped early
    pipeline.Finally(new AuditRefundAttempt());

    await pipeline.Invoke(msg);
}
```

Stopping is explicit, visible, and traceable. No hidden exception-based flow control.