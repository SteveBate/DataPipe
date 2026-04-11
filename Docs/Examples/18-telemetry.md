# Telemetry Integration

DataPipe has a built-in telemetry system that tracks pipeline and filter execution. It captures start/end events, durations, outcomes, and custom attributes — then forwards them to an adapter of your choice.

## How it works

1. Set `TelemetryMode` on the pipeline to control what gets captured
2. Register a `TelemetryAspect` with an adapter that receives the events
3. The pipeline engine emits `TelemetryEvent` records as filters execute
4. The adapter batches and writes them to your chosen output

## TelemetryMode

Controls the granularity of event emission. Set it on the pipeline:

```csharp
var pipeline = new DataPipe<OrderMessage>
{
    Name = "CreateOrder",
    TelemetryMode = TelemetryMode.PipelineAndFilters
};
```

| Mode | What's emitted |
|------|---------------|
| `Off` | Nothing (default) |
| `PipelineOnly` | Pipeline start/end only |
| `PipelineAndErrors` | Pipeline start/end + any filter exceptions |
| `PipelineErrorsAndStops` | Pipeline start/end + exceptions + stopped filters |
| `PipelineAndFilters` | Everything — pipeline and all filter start/end events |

The mode is evaluated per event via `msg.ShouldEmitTelemetry(event)`. Events that don't match the current mode are silently dropped before reaching the adapter.

## Timing Without Telemetry

If you only need filter timing in logs, you can enable timings without emitting telemetry events.

Use `EnableTimings = true` with `TelemetryMode = Off`:

```csharp
var pipeline = new DataPipe<OrderMessage>
{
    Name = "CreateOrder",
    TelemetryMode = TelemetryMode.Off,
    EnableTimings = true
};
```

This keeps telemetry adapters inactive while still adding elapsed milliseconds to pipeline and filter completion logs.

Example log output:

```text
COMPLETED: ValidateOrder (4ms)
COMPLETED: SaveOrder (18ms)
PIPELINE: CreateOrder - Outcome: Success (27ms)
```

Notes:
- `EnableTimings` defaults to `false`.
- When telemetry is enabled, timings are always captured even if `EnableTimings` is `false`.
- If both telemetry and `EnableTimings` are off, completion logs are emitted without duration values.

## ServiceIdentity

When telemetry is enabled, a `ServiceIdentity` must be set on the message. The pipeline will throw if it's missing:

```csharp
var msg = new OrderMessage
{
    Actor = "user@example.com",
    Service = new ServiceIdentity
    {
        Name = "Orders.Api",
        Environment = "Production",
        Version = "2.1.0",
        InstanceId = Environment.MachineName  // optional
    }
};
```

## TelemetryAspect

The `TelemetryAspect` is registered like any other aspect. It subscribes to the message's `OnTelemetry` event and forwards captured events to an adapter:

```csharp
var adapter = new ConsoleTelemetryAdapter();

var pipeline = new DataPipe<OrderMessage>
{
    Name = "CreateOrder",
    TelemetryMode = TelemetryMode.PipelineAndFilters
};

pipeline.Use(new ExceptionAspect<OrderMessage>());
pipeline.Use(new LoggingAspect<OrderMessage>(logger, "CreateOrder", env));
pipeline.Use(new TelemetryAspect<OrderMessage>(adapter));

pipeline.Add(new ValidateOrder(), new SaveOrder());
```

Register aspects in this order: exception handling first, then logging, then telemetry.

## TelemetryEvent

Each event captured by the pipeline contains:

| Property | Description |
|----------|-------------|
| `Actor` | Who initiated the pipeline |
| `MessageId` | The message's `CorrelationId` |
| `Component` | Filter class name or pipeline name |
| `PipelineName` | The pipeline's `Name` property |
| `Service` | The `ServiceIdentity` from the message |
| `Role` | `Business`, `Structural`, or `None` (pipeline-level) |
| `Scope` | `Pipeline` or `Filter` |
| `Phase` | `Start` or `End` |
| `Outcome` | `Success`, `Stopped`, `Exception`, `Started`, or `None` |
| `Reason` | Stop reason or exception message |
| `DurationMs` | Elapsed milliseconds (end events only) |
| `Timestamp` | When the event occurred |
| `Attributes` | Custom key-value data from `TelemetryAnnotations` |

## Built-in Adapters

### ConsoleTelemetryAdapter

Batches events in memory and writes them as JSON to `Console.WriteLine` when the pipeline completes. Useful for development:

```csharp
var adapter = new ConsoleTelemetryAdapter();
```

### StructuredJsonTelemetryAdapter

Batches events and writes them to an `ILogger` instance as structured JSON. Designed for ingestion into log management systems like Splunk or Snowflake:

```csharp
var adapter = new StructuredJsonTelemetryAdapter(logger);
```

Both adapters batch events into a `TelemetryBatch` on flush, which wraps the event list with pipeline-level context (name, duration, outcome, service, actor).

## Custom Adapters

Implement `ITelemetryAdapter` to write telemetry anywhere:

```csharp
public interface ITelemetryAdapter
{
    void Handle(TelemetryEvent evt);
    void Flush();
}
```

`Handle` is called per event during execution. `Flush` is called once when the pipeline completes. See `Docs/Patterns/Telemetry/Adapters` for examples including a file-based adapter.

## Telemetry Policies

Policies filter events at the adapter level, **after** the mode filter. An adapter accepts an optional `ITelemetryPolicy`:

```csharp
public interface ITelemetryPolicy
{
    bool ShouldInclude(TelemetryEvent evt);
}
```

### Built-in Policies

| Policy | Purpose |
|--------|---------|
| `DefaultCaptureEverythingPolicy` | Includes all events (applied when no policy is specified) |
| `MinimumDurationPolicy(ms)` | Excludes filter events shorter than the threshold |
| `RolePolicy(role)` | Includes only `Business`, `Structural`, or `All` role events |
| `ExcludeStartEventsPolicy(exclude)` | Drops `Start` phase events to reduce volume |
| `SuppressAllExceptErrorsPolicy(name)` | Suppresses all events for a named pipeline except exceptions |
| `CompositeTelemetryPolicy(policies...)` | Combines multiple policies — all must pass for an event to be included |

### Composing Policies

Use `CompositeTelemetryPolicy` to combine multiple rules:

```csharp
var policy = new CompositeTelemetryPolicy(
    new MinimumDurationPolicy(50),
    new RolePolicy(TelemetryRole.Business),
    new ExcludeStartEventsPolicy(true)
);

var adapter = new StructuredJsonTelemetryAdapter(logger, policy);
```

This captures only business filter end events that took at least 50ms.

## Structural Filter Attributes

Structural filters automatically attach custom attributes to their telemetry events via `TelemetryAnnotations`. These appear in the `Attributes` dictionary of the emitted `TelemetryEvent`. The following table lists every attribute emitted by each built-in structural filter.

### Core Structural Filters

| Filter | Attribute | Type | Phase | Description |
|--------|-----------|------|-------|-------------|
| **OnTimeoutRetry** | `max-attempts` | `int` | Start, End | Total maximum attempts (retries + 1) |
| | `final-attempt` | `int` | End | Which attempt execution completed on |
| | `retry` | `int` | End | Number of retries that occurred (only if > 0) |
| | `retry-reason` | `string` | End | Last exception message that triggered retry (only if retries > 0) |
| **OnCircuitBreak** | `circuit-state` | `string` | Start, End | Current state: `Closed`, `Open`, or `HalfOpen` |
| | `failure-threshold` | `int` | Start | Consecutive failures before the circuit trips |
| | `break-duration-seconds` | `double` | Start | Duration the circuit stays open |
| | `failure-count` | `int` | End | Current consecutive failure count |
| | `circuit-tripped` | `bool` | End | `true` if circuit transitioned to Open during this execution |
| **OnRateLimit** | `capacity` | `int` | Start, End | Maximum tokens in the bucket |
| | `queue-depth` | `int` | Start, End | Queue depth at time of emission |
| | `leak-interval-ms` | `double` | Start | Time between token leaks (ms) |
| | `behavior` | `string` | Start | `Delay` or `Reject` |
| | `wait-time-ms` | `long` | End | Milliseconds spent waiting for capacity |
| | `rejected` | `bool` | End | `true` if the request was rejected |
| **Timeout** | `timeout-ms` | `long` | Start | Configured timeout duration (ms) |
| | `timed-out` | `bool` | End | `true` if execution exceeded the timeout |
| **TryCatch** | `caught-exception` | `bool` | End | `true` if an exception was caught and handled |
| **Policy** | `decision` | `string` | Start, End | Selected filter name, or `null` if no selection |
| | `exception` | `string` | End | Exception type name (only on exception pathway) |
| **IfTrue** | `condition` | `bool` | Start | Result of the predicate evaluation |
| **IfTrueElse** | `condition` | `bool` | Start | Result of the predicate evaluation |
| | `branch` | `string` | Start | `then` or `else` — which branch will execute |
| **ParallelForEach** | `branches` | `int` | Start, End | Number of child messages to process |
| | `max-parallelism` | `int` | Start | Maximum degree of parallelism (`-1` = unlimited) |
| **RepeatUntil** | `condition` | `bool` | End | Whether the until-condition was met |

### SQL Filters (DataPipe.Sql)

| Filter | Attribute | Type | Phase | Description |
|--------|-----------|------|-------|-------------|
| **OpenSqlConnection** | `database` | `string` | Start | Database name from the opened connection |
| **StartSqlTransaction** | `isolation-level` | `string` | Start, End | Transaction isolation level |
| | `database` | `string` | Start, End | Database name |
| | `committed` | `bool` | End | `true` if committed; `false` if rolled back |
| **StartTransactionScope** | `isolation-level` | `string` | Start, End | Transaction isolation level |
| | `timeout` | `string` | Start | Transaction timeout |
| | `committed` | `bool` | End | `true` if scope completed; `false` if disposed |

> **Note:** `ForEach`, `Repeat`, `Sequence`, `DelayExecution`, and `LambdaFilter` emit standard telemetry events but do not add custom attributes.

## TelemetryAnnotations

In addition to the automatic attributes above, filters can attach custom domain metadata to telemetry events via `msg.Execution.TelemetryAnnotations`. Annotations are included in the next event's `Attributes` dictionary and automatically cleared after emission:

```csharp
msg.Execution.TelemetryAnnotations["DatabaseName"] = "OrdersDb";
msg.Execution.TelemetryAnnotations["IsolationLevel"] = "ReadCommitted";
```

## Complete Example

```csharp
public async Task CreateOrder(OrderMessage msg)
{
    var env = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Development";
    var policy = new CompositeTelemetryPolicy(
        new MinimumDurationPolicy(50),
        new ExcludeStartEventsPolicy(true));
    var adapter = new StructuredJsonTelemetryAdapter(logger, policy);

    var pipeline = new DataPipe<OrderMessage>
    {
        Name = "CreateOrder",
        TelemetryMode = TelemetryMode.PipelineAndFilters
    };

    pipeline.Use(new ExceptionAspect<OrderMessage>());
    pipeline.Use(new LoggingAspect<OrderMessage>(logger, "CreateOrder", env));
    pipeline.UseIf(env != "Development", new TelemetryAspect<OrderMessage>(adapter));

    pipeline.Add(
        new ValidateOrder(),
        new OnTimeoutRetry<OrderMessage>(maxRetries: 2,
            new OpenSqlConnection<OrderMessage>(connectionString,
                new SaveOrder(),
                new UpdateInventory()
            )
        ),
        new SendConfirmation()
    );

    await pipeline.Invoke(msg);
}
```

Telemetry is explicit, composable, and completely opt-in. You control what gets captured, where it goes, and how it's filtered.

