# Rate Limiting

`OnRateLimit<T>` provides the leaky bucket rate limiting pattern as a pipeline filter. It wraps one or more filters and enforces a maximum throughput rate. When the bucket is full, the filter either waits for capacity (backpressure) or rejects the request immediately with a 429 status code.

## The leaky bucket algorithm

The leaky bucket is a simple and effective rate limiting algorithm. Imagine a bucket that can hold a fixed number of tokens. Each incoming request places a token in the bucket. Tokens drain automatically after a fixed time interval — the "leak".

- When the bucket has space, the request is admitted immediately.
- When the bucket is full, the request must wait for a token to drain (Delay mode) or is rejected outright (Reject mode).

The combination of bucket capacity and leak interval determines the effective throughput limit. For example, a bucket with `capacity: 10` and `leakInterval: 100ms` allows a sustained rate of ~100 requests/second, while tolerating brief bursts of up to 10 simultaneous requests.

This makes the leaky bucket ideal for scenarios where you need to enforce a steady throughput rate while allowing short bursts of activity — exactly the behaviour needed when calling databases or external APIs with rate limits.

## Basic usage

```csharp
var rateLimiterState = new RateLimiterState();

var pipeline = new DataPipe<ImportMessage>();

pipeline.Add(new ValidateRecord());

pipeline.Add(new OnRateLimit<ImportMessage>(rateLimiterState,
    capacity: 100,
    leakInterval: TimeSpan.FromMilliseconds(10),
    filters: new InsertRecord()));

pipeline.Add(new SendConfirmation());

await pipeline.Invoke(msg);
```

The default behaviour is `Delay` — the pipeline pauses until bucket capacity is available.

## Custom configuration

```csharp
// Hard reject at 50 requests/second (no waiting)
pipeline.Add(new OnRateLimit<ApiMessage>(rateLimiterState,
    capacity: 50,
    leakInterval: TimeSpan.FromMilliseconds(20),
    behavior: RateLimitExceededBehavior.Reject,
    filters: new CallExternalApi()));
```

In Reject mode, when the bucket is full, `OnRateLimit` throws `RateLimitRejectedException`. The built-in `ExceptionAspect` catches this and sets `StatusCode = 429` (Too Many Requests).

## Shared state across pipelines

`RateLimiterState` is a plain object designed to be shared. In a Web API, register it as a singleton so all pipelines targeting the same resource share the same bucket:

```csharp
// DI registration
services.AddSingleton(new RateLimiterState());
```

```csharp
// Pipeline construction (state injected via constructor)
public class ImportService
{
    private readonly RateLimiterState _dbThrottle;

    public ImportService(RateLimiterState dbThrottle)
    {
        _dbThrottle = dbThrottle;
    }

    public async Task ProcessImport(ImportMessage msg)
    {
        var pipeline = new DataPipe<ImportMessage>();

        pipeline.Add(new ValidateRecord());

        pipeline.Add(new OnRateLimit<ImportMessage>(_dbThrottle,
            capacity: 200,
            leakInterval: TimeSpan.FromMilliseconds(5),
            filters: new InsertRecord()));

        pipeline.Add(new LogResult());

        await pipeline.Invoke(msg);
    }
}
```

When one pipeline fills the bucket, all pipelines sharing the same state are throttled. This prevents the entire application from overwhelming a resource during bulk operations.

## Combined with circuit breaker and retry

Place rate limiting outside circuit breaker and retry. This ensures the entire resilience stack operates within the rate limit — retries and circuit probe attempts all respect the same throughput constraint:

```csharp
pipeline.Add(new OnRateLimit<OrderMessage>(shopifyThrottle,
    capacity: 40,
    leakInterval: TimeSpan.FromMilliseconds(500),
    filters: new OnCircuitBreak<OrderMessage>(circuitState,
        new OnTimeoutRetry<OrderMessage>(maxRetries: 2,
            new CallShopifyApi()))));
```

If rate limiting was placed inside the circuit breaker, a circuit probe attempt could be blocked by the rate limiter, delaying recovery detection unnecessarily.

## Multiple rate limiters for different resources

Use separate `RateLimiterState` instances for each resource with its own throughput limit:

```csharp
var dbThrottle = new RateLimiterState();
var shopifyThrottle = new RateLimiterState();
var emailThrottle = new RateLimiterState();
```

```csharp
pipeline.Add(new OnRateLimit<OrderMessage>(dbThrottle,
    capacity: 200, leakInterval: TimeSpan.FromMilliseconds(5),
    filters: new SaveOrder()));

pipeline.Add(new OnRateLimit<OrderMessage>(shopifyThrottle,
    capacity: 40, leakInterval: TimeSpan.FromMilliseconds(500),
    filters: new SyncToShopify()));

pipeline.Add(new OnRateLimit<OrderMessage>(emailThrottle,
    capacity: 10, leakInterval: TimeSpan.FromMilliseconds(100),
    filters: new SendOrderConfirmation()));
```

Each rate limiter operates independently. Hitting the Shopify rate limit won't slow down database inserts.

## Handling rate limit rejections

When using Reject mode, `OnRateLimit` throws `RateLimitRejectedException`. The built-in `ExceptionAspect` catches this and sets `StatusCode = 429` automatically:

```csharp
var pipeline = new DataPipe<ApiMessage>();

pipeline.Use(new ExceptionAspect<ApiMessage>());

pipeline.Add(new OnRateLimit<ApiMessage>(rateLimiterState,
    capacity: 50,
    leakInterval: TimeSpan.FromMilliseconds(20),
    behavior: RateLimitExceededBehavior.Reject,
    filters: new CallExternalApi()));

await pipeline.Invoke(msg);

// msg.StatusCode == 429 when rate limit exceeded
// msg.StatusMessage == "Execution blocked by rate limiter."
```

You can combine this with a `Finally` filter for custom recovery logic:

```csharp
pipeline.Finally(async msg =>
{
    if (msg.StatusCode == 429)
    {
        msg.StatusMessage = "Request rate limit exceeded. Please try again shortly.";
    }
});
```

## Use case examples

### Protecting your own database from bulk operations

During data imports, thousands of records may arrive in rapid succession. Without rate limiting, the application can overwhelm the database with concurrent writes, causing timeouts, lock contention, and degraded performance for other users:

```csharp
var dbThrottle = new RateLimiterState();

// Process each import record through the pipeline
foreach (var record in importBatch)
{
    var msg = new ImportMessage { Record = record };
    var pipeline = new DataPipe<ImportMessage>();
    pipeline.Use(new ExceptionAspect<ImportMessage>());
    pipeline.Add(new OnRateLimit<ImportMessage>(dbThrottle,
        capacity: 100,
        leakInterval: TimeSpan.FromMilliseconds(10),
        filters: new OpenSqlConnection<ImportMessage>(connectionString,
            new InsertRecord())));
    await pipeline.Invoke(msg);
}
```

The rate limiter ensures a steady flow of inserts without saturating the database.

### Respecting external API rate limits (Shopify)

External APIs like Shopify enforce rate limits (e.g. 40 requests per bucket that leaks at 2 per second). Exceeding these limits results in 429 responses and potential API key suspension. `OnRateLimit` lets you proactively stay within the provider's limits:

```csharp
var shopifyThrottle = new RateLimiterState();

pipeline.Add(new OnRateLimit<OrderMessage>(shopifyThrottle,
    capacity: 38,                                    // stay slightly under Shopify's 40
    leakInterval: TimeSpan.FromMilliseconds(500),    // 2 tokens/second leak rate
    filters: new OnCircuitBreak<OrderMessage>(circuitState,
        new OnTimeoutRetry<OrderMessage>(maxRetries: 2,
            new CallShopifyApi()))));
```

### Being a responsible caller to unprotected APIs

Some external APIs have no rate limiting of their own. Without self-imposed throttling, a bug or bulk operation in your code could overwhelm the service. Rate limiting makes your application a responsible consumer:

```csharp
var partnerApiThrottle = new RateLimiterState();

pipeline.Add(new OnRateLimit<SyncMessage>(partnerApiThrottle,
    capacity: 20,
    leakInterval: TimeSpan.FromMilliseconds(50),
    filters: new CallPartnerApi()));
```

## Telemetry attributes

When telemetry is enabled, `OnRateLimit` emits start and end events with these attributes:

| Attribute | Phase | Description |
|-----------|-------|-------------|
| `rate-limit-capacity` | Start, End | The configured bucket capacity |
| `rate-limit-leak-interval-ms` | Start | The configured leak interval in milliseconds |
| `rate-limit-behavior` | Start | The configured behaviour (`Delay` or `Reject`) |
| `rate-limit-queue-depth` | End | Current number of tokens in the bucket |
| `rate-limit-wait-time-ms` | End | Time spent waiting for bucket capacity (0 if admitted immediately) |
| `rate-limit-rejected` | End | Whether the request was rejected (`true`/`false`) |

## What this demonstrates

- Throughput is controlled by the leaky bucket algorithm without blocking the entire pipeline
- Shared state enables application-wide rate limiting via DI
- Two modes (Delay and Reject) serve different use cases: backpressure for internal resources, fail-fast for API quotas
- Rate limiter state is visible in telemetry for monitoring and capacity planning
- Rate limiting, circuit breaker, and retry compose naturally — rate limit outside, circuit in the middle, retry innermost
- Different resources get independent rate limiters
- Thread-safe bucket state supports concurrent pipeline invocations
