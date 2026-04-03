# Timeout

`Timeout<T>` enforces a maximum execution duration on child filters. If the filters do not complete within the specified time, a `TimeoutException` is thrown.

## Basic usage

```csharp
var pipeline = new DataPipe<OrderMessage>();

pipeline.Add(new ValidateOrder());

pipeline.Add(new Timeout<OrderMessage>(TimeSpan.FromSeconds(30),
    new CallExternalApi()));

pipeline.Add(new SaveOrder());

await pipeline.Invoke(msg);
```

If `CallExternalApi` takes longer than 30 seconds, it is cancelled and a `TimeoutException` is thrown.

## How it works

- Creates a linked `CancellationToken` that fires after the specified duration
- Child filters see the timeout-aware token via `msg.CancellationToken`
- If the timeout elapses, any async operation respecting the token is cancelled
- The original cancellation token is restored after execution (whether successful or not)
- External cancellation (e.g. from the caller) still works — the timeout does not mask it

## Protecting against unresponsive APIs

```csharp
pipeline.Add(new Timeout<OrderMessage>(TimeSpan.FromSeconds(10),
    new FetchInventoryFromSupplier(),
    new MapSupplierResponse()));
```

If the supplier API hangs, the pipeline fails fast rather than waiting indefinitely.

## Combined with retry

Use `Timeout` inside `OnTimeoutRetry` so each attempt has its own time limit:

```csharp
pipeline.Add(new OnTimeoutRetry<OrderMessage>(maxRetries: 3,
    new Timeout<OrderMessage>(TimeSpan.FromSeconds(5),
        new CallPaymentGateway())));
```

Each retry attempt gets a fresh 5-second window. Without `Timeout`, the retry would only fire if the call itself threw a timeout — a hanging connection would block forever.

## Combined with circuit breaker

Place `Timeout` inside the circuit breaker so that timeouts count as failures toward the circuit threshold:

```csharp
pipeline.Add(new OnCircuitBreak<OrderMessage>(circuitState,
    failureThreshold: 3,
    breakDuration: TimeSpan.FromMinutes(1),
    filters: new Timeout<OrderMessage>(TimeSpan.FromSeconds(10),
        new CallExternalApi())));
```

## Error handling

`Timeout<T>` throws a standard `TimeoutException`. The built-in `ExceptionAspect<T>` catches this and sets `StatusCode = 500`. If you want to distinguish timeouts from other errors, handle `TimeoutException` in a custom error-handling aspect.
