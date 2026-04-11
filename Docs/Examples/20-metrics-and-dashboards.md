# Metrics and Dashboard Integration

DataPipe's telemetry events contain all the data needed for real-time metrics. By writing a custom `ITelemetryAdapter` that translates events into .NET `System.Diagnostics.Metrics` counters, you can feed pipeline metrics directly to Prometheus, Azure Monitor, Grafana, Datadog, or any OpenTelemetry-compatible system — no log parsing required.

## CompositeTelemetryAdapter

Before building a metrics adapter, you need a way to use it alongside your existing logging adapter. `CompositeTelemetryAdapter` fans out events to multiple adapters:

```csharp
var loggingAdapter = new StructuredJsonTelemetryAdapter(logger, policy);
var metricsAdapter = new MetricsTelemetryAdapter(meterFactory);

var adapter = new CompositeTelemetryAdapter(loggingAdapter, metricsAdapter);
pipeline.Use(new TelemetryAspect<OrderMessage>(adapter));
```

Both adapters receive every event. Both are flushed when the pipeline completes. You can compose as many adapters as you need.

## MetricsTelemetryAdapter

This adapter lives in **your application**, not in the DataPipe framework. It implements `ITelemetryAdapter` and uses .NET's `System.Diagnostics.Metrics` API:

```csharp
using System.Diagnostics.Metrics;
using DataPipe.Core.Contracts;
using DataPipe.Core.Telemetry;

public sealed class MetricsTelemetryAdapter : ITelemetryAdapter
{
    private readonly Counter<long> _pipelineInvocations;
    private readonly Counter<long> _pipelineErrors;
    private readonly Counter<long> _filterExecutions;
    private readonly Histogram<double> _pipelineDuration;
    private readonly Counter<long> _circuitBreakerTrips;
    private readonly Counter<long> _rateLimiterRejections;

    public MetricsTelemetryAdapter(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create("DataPipe");
        _pipelineInvocations    = meter.CreateCounter<long>("datapipe.pipeline.invocations");
        _pipelineErrors         = meter.CreateCounter<long>("datapipe.pipeline.errors");
        _filterExecutions       = meter.CreateCounter<long>("datapipe.filter.executions");
        _pipelineDuration       = meter.CreateHistogram<double>("datapipe.pipeline.duration_ms");
        _circuitBreakerTrips    = meter.CreateCounter<long>("datapipe.circuitbreaker.trips");
        _rateLimiterRejections  = meter.CreateCounter<long>("datapipe.ratelimiter.rejections");
    }

    public void Handle(TelemetryEvent evt)
    {
        var tags = new TagList
        {
            { "pipeline", evt.PipelineName },
            { "service", evt.Service?.Name }
        };

        // Pipeline-level metrics (invocations, errors, duration)
        if (evt.Scope == TelemetryScope.Pipeline && evt.Phase == TelemetryPhase.End)
        {
            _pipelineInvocations.Add(1, tags);
            if (evt.DurationMs.HasValue)
                _pipelineDuration.Record(evt.DurationMs.Value, tags);
            if (evt.Outcome == TelemetryOutcome.Exception)
                _pipelineErrors.Add(1, tags);
        }

        // Filter-level metrics
        if (evt.Scope == TelemetryScope.Filter && evt.Phase == TelemetryPhase.End)
        {
            _filterExecutions.Add(1, in tags);
        }

        // Resilience metrics from structural filter attributes
        if (evt.Attributes?.TryGetValue("circuit-state", out var state) == true
            && state?.ToString() == "Open")
            _circuitBreakerTrips.Add(1, tags);

        if (evt.Attributes?.TryGetValue("rate-limit-action", out var action) == true
            && action?.ToString() == "Rejected")
            _rateLimiterRejections.Add(1, tags);
    }

    public void Flush() { } // Metrics are pushed in real-time per event
}
```

## How the metrics flow to dashboards

```
Filter executes → TelemetryEvent emitted
  → MetricsTelemetryAdapter.Handle() increments counter
    → .NET Metrics API exposes via OpenTelemetry exporter
      → Scraper (Prometheus, Azure Monitor agent, Datadog) collects
        → Dashboard (Grafana, Azure Monitor Workbooks, Datadog) displays
```

## Registering in Program.cs

```csharp
// 1. Register the OpenTelemetry metrics pipeline
builder.Services.AddOpenTelemetry()
    .WithMetrics(metrics =>
    {
        metrics.AddMeter("DataPipe");           // Subscribe to the DataPipe meter
        metrics.AddPrometheusExporter();         // or .AddOtlpExporter() for Azure/Datadog
    });

// 2. Register the metrics adapter as a singleton
builder.Services.AddSingleton<MetricsTelemetryAdapter>();
```

Then in your service:

```csharp
public class OrderService
{
    private readonly ILogger<OrderService> _logger;
    private readonly MetricsTelemetryAdapter _metricsAdapter;

    public OrderService(ILogger<OrderService> logger, MetricsTelemetryAdapter metricsAdapter)
    {
        _logger = logger;
        _metricsAdapter = metricsAdapter;
    }

    public async Task CreateOrder(CreateOrderMessage msg)
    {
        var policy = new CompositeTelemetryPolicy(
            new MinimumDurationPolicy(50),
            new ExcludeStartEventsPolicy(true));

        var adapter = new CompositeTelemetryAdapter(
            new StructuredJsonTelemetryAdapter(_logger, policy),
            _metricsAdapter);

        var pipe = new DataPipe<CreateOrderMessage>
        {
            Name = "CreateOrder",
            TelemetryMode = TelemetryMode.PipelineAndErrors
        };

        pipe.Use(new ExceptionAspect<CreateOrderMessage>());
        pipe.Use(new LoggingAspect<CreateOrderMessage>(_logger, "CreateOrder", "Production"));
        pipe.Use(new TelemetryAspect<CreateOrderMessage>(adapter));

        pipe.Add(new ValidateOrder(), new SaveOrder());
        await pipe.Invoke(msg);
    }
}
```

## Available metrics

| Metric | Type | What it tells you |
|--------|------|-------------------|
| `datapipe.pipeline.invocations` | Counter | Throughput per pipeline per minute |
| `datapipe.pipeline.errors` | Counter | Error rate — alert when this exceeds a threshold |
| `datapipe.pipeline.duration_ms` | Histogram | P50, P95, P99 latency — spot slow pipelines |
| `datapipe.filter.executions` | Counter | Identify hot filters and bottlenecks |
| `datapipe.circuitbreaker.trips` | Counter | Dependency health — how often circuit breakers open |
| `datapipe.ratelimiter.rejections` | Counter | Capacity pressure — signal to scale |

## Extending with custom metrics

### What is `evt.Component`?

DataPipe sets `Component` automatically on every telemetry event:

- **Pipeline-scoped events** — `Component` is the pipeline's `Name` property (e.g. `"CreateOrder"`).
- **Filter-scoped events** — `Component` is the filter's class name with the generic suffix stripped (e.g. a `ChargePayment<OrderMessage>` filter emits events where `Component == "ChargePayment"`).

This makes it straightforward to add counters for specific business filters. You add them as additional fields in your `MetricsTelemetryAdapter` alongside the standard counters, and check for them in `Handle()`:

```csharp
public sealed class MetricsTelemetryAdapter : ITelemetryAdapter
{
    // ... standard counters from earlier ...

    // Domain-specific counters — add as many as you need
    private readonly Counter<long> _paymentAttempts;
    private readonly Counter<long> _retryAttempts;

    public MetricsTelemetryAdapter(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create("DataPipe");
        // ... standard counter creation from earlier ...

        _paymentAttempts = meter.CreateCounter<long>("datapipe.filter.payment_attempts");
        _retryAttempts   = meter.CreateCounter<long>("datapipe.filter.retry_attempts");
    }

    public void Handle(TelemetryEvent evt)
    {
        var tags = new TagList
        {
            { "pipeline", evt.PipelineName },
            { "service", evt.Service?.Name }
        };

        // ... standard metrics from earlier ...

        // Count executions of a specific filter by its class name
        if (evt.Component == "ChargePayment" && evt.Phase == TelemetryPhase.End)
            _paymentAttempts.Add(1, tags);

        // Track retry activity via custom attributes
        if (evt.Attributes?.TryGetValue("retry-attempt", out var attempt) == true)
            _retryAttempts.Add(1, tags);
    }
}
```

## Telemetry mode considerations

The `MetricsTelemetryAdapter` needs events to count. Choose a `TelemetryMode` that emits the events you care about:

| Mode | Pipeline metrics | Error metrics | Filter metrics | Resilience metrics |
|------|:---:|:---:|:---:|:---:|
| `PipelineOnly` | ✓ | — | — | — |
| `PipelineAndErrors` | ✓ | ✓ | — | — |
| `PipelineErrorsAndStops` | ✓ | ✓ | — | ✓ (stops only) |
| `PipelineAndFilters` | ✓ | ✓ | ✓ | ✓ |

For most production services, `PipelineAndErrors` gives you the key metrics without per-filter overhead. Use `PipelineAndFilters` when you need filter-level granularity for performance analysis.
