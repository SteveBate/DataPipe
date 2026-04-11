# Circuit Breaker

`OnCircuitBreak<T>` provides the circuit breaker pattern as a pipeline filter. It wraps one or more filters and tracks their failure rate. After a threshold of consecutive failures, the circuit opens and all subsequent calls fail fast without executing the wrapped filters. After a cooldown period, exactly one probe attempt is allowed to test recovery — all other concurrent requests continue to fail fast until the probe completes.

## Basic usage

```csharp
var circuitState = new CircuitBreakerState();

var pipeline = new DataPipe<OrderMessage>();

pipeline.Add(new ValidateOrder());

pipeline.Add(new OnCircuitBreak<OrderMessage>(circuitState,
    new CallPaymentGateway()));

pipeline.Add(new SendConfirmation());

await pipeline.Invoke(msg);
```

The default configuration trips after 5 consecutive failures and stays open for 30 seconds.

## Custom thresholds

```csharp
pipeline.Add(new OnCircuitBreak<OrderMessage>(circuitState,
    failureThreshold: 3,
    breakDuration: TimeSpan.FromMinutes(1),
    new CallPaymentGateway()));
```

## Shared state across pipelines

`CircuitBreakerState` is a plain object designed to be shared. In a Web API, register it as a singleton so all pipelines targeting the same external resource share the same circuit:

```csharp
// DI registration
services.AddSingleton(new CircuitBreakerState());
```

```csharp
// Pipeline construction (state injected via constructor)
public class OrderService
{
    private readonly CircuitBreakerState _paymentCircuit;

    public OrderService(CircuitBreakerState paymentCircuit)
    {
        _paymentCircuit = paymentCircuit;
    }

    public async Task ProcessOrder(OrderMessage msg)
    {
        var pipeline = new DataPipe<OrderMessage>();
        
        pipeline.Add(new ValidateOrder());
        
        pipeline.Add(new OnCircuitBreak<OrderMessage>(_paymentCircuit,
            failureThreshold: 10,
            breakDuration: TimeSpan.FromSeconds(60),
            new CallPaymentGateway()));
        
        pipeline.Add(new SendConfirmation());
        
        await pipeline.Invoke(msg);
    }
}
```

When one pipeline trips the circuit, all pipelines sharing the same state fail fast immediately. This shields the entire application from hammering a resource that is already down.

## Combined with retry

Place retry inside the circuit breaker. Retries count as a single attempt from the circuit's perspective - if all retries fail, it counts as one failure:

```csharp
pipeline.Add(new OnCircuitBreak<OrderMessage>(circuitState,
    new OnTimeoutRetry<OrderMessage>(maxRetries: 2,
        new CallPaymentGateway())));
```

If retry was placed outside the circuit breaker, each retry attempt would independently check the circuit, which defeats the purpose.

## Multiple circuits for different resources

Use separate `CircuitBreakerState` instances for each external dependency:

```csharp
var paymentCircuit = new CircuitBreakerState();
var inventoryCircuit = new CircuitBreakerState();
var shippingCircuit = new CircuitBreakerState();
```

```csharp
pipeline.Add(new OnCircuitBreak<OrderMessage>(paymentCircuit,
    new ChargePayment()));

pipeline.Add(new OnCircuitBreak<OrderMessage>(inventoryCircuit,
    new ReserveInventory()));

pipeline.Add(new OnCircuitBreak<OrderMessage>(shippingCircuit,
    new ScheduleShipment()));
```

Each circuit operates independently. A payment gateway outage won't prevent inventory checks.

## Jitter — preventing thundering herd on recovery

When multiple instances share a circuit and it trips, they all recover at the same instant by default — creating a thundering herd against the recovering resource. The `jitterRatio` parameter adds a random offset to the break duration to spread recovery probes:

```csharp
pipeline.Add(new OnCircuitBreak<OrderMessage>(circuitState,
    failureThreshold: 5,
    breakDuration: TimeSpan.FromSeconds(30),
    jitterRatio: 0.2,   // ±20% → break of 24–36 seconds
    new CallPaymentGateway()));
```

- `jitterRatio: 0.0` (default) — no jitter, exact break duration
- `jitterRatio: 0.2` — ±20% of the base duration
- `jitterRatio: 1.0` — ±100% (maximum spread)
- Values above 1.0 are clamped to 1.0

Each time the circuit trips, a fresh random offset is computed, so different instances recover at different times.

## Handling open circuit rejections

When the circuit is open (or half-open with a probe already in progress), `OnCircuitBreak` throws `CircuitBreakerOpenException`. The built-in `ExceptionAspect` catches this and sets `StatusCode = 503` (Service Unavailable) automatically:

```csharp
var pipeline = new DataPipe<OrderMessage>();

pipeline.Use(new ExceptionAspect<OrderMessage>());

pipeline.Add(new OnCircuitBreak<OrderMessage>(circuitState,
    new CallPaymentGateway()));

await pipeline.Invoke(msg);

// msg.StatusCode == 503 when circuit is open
// msg.StatusMessage == "Execution blocked by open circuit breaker."
```

You can combine this with a `Finally` filter for custom recovery logic:

```csharp
pipeline.Finally(async msg =>
{
    if (msg.StatusCode == 503)
    {
        msg.StatusMessage = "Payment service is temporarily unavailable. Please try again later.";
    }
});
```

## Concurrency and the HTML scraping example

Because `CircuitBreakerState` is thread-safe and shared, it works naturally with parallel pipeline invocations:

```csharp
var circuitState = new CircuitBreakerState();

var pipeline = BuildHtmlScrapePipeline(circuitState);

var messages = [
    new HtmlScrapeMessage { Url = "https://example.com/page1" },
    new HtmlScrapeMessage { Url = "https://example.com/page2" },
    new HtmlScrapeMessage { Url = "https://example.com/page3" },
];

await Parallel.ForEachAsync(messages, async (msg, ct) =>
{
    await pipeline.Invoke(msg);
});
```

If the target site goes down, the circuit trips and all remaining parallel invocations fail fast - protecting both the pipeline and the remote server.

## Circuit states

| State | Behavior |
|-------|----------|
| Closed | Normal execution. Failures are counted. |
| Open | All calls fail fast with `CircuitBreakerOpenException` (503). |
| Half-Open | Exactly one probe attempt is allowed. All other concurrent requests fail fast. Success closes the circuit. Failure re-opens it. |

## Telemetry attributes

When telemetry is enabled, `OnCircuitBreak` emits start and end events with these attributes:

| Attribute | Phase | Description |
|-----------|-------|-------------|
| `circuit-state` | Start, End | The circuit state (`Closed`, `Open`, `HalfOpen`) |
| `failure-threshold` | Start | The configured failure threshold |
| `break-duration-seconds` | Start | The configured break duration in seconds |
| `failure-count` | End | Current consecutive failure count |
| `circuit-tripped` | End | Whether this invocation tripped the circuit to Open |

## What this demonstrates

- External resource failures are isolated by the circuit breaker
- Shared state enables application-wide protection via DI
- Circuit state is visible in telemetry for monitoring and alerting
- Retry and circuit breaker compose naturally - retry inside, circuit outside
- Different resources get independent circuits
- Thread-safe state transitions support concurrent pipeline invocations
