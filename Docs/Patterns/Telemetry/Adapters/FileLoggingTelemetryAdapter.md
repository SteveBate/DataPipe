
## FileLoggingTelemetryAdapter

This adapter batches telemetry events produced by a pipeline execution, enriches them with execution-level metadata and writes the batch as JSON to an ILogger. The included sample uses Serilog for output, but the adapter only depends on `Microsoft.Extensions.Logging.ILogger` and can be used with any logging provider.

When to use
- Gather a sequence of `TelemetryEvent`s for a single pipeline execution and emit them as a single, enriched JSON object.
- Keep telemetry I/O out of the hot path by buffering events and flushing at the end of the pipeline.
- Emit structured telemetry that can be indexed in log stores or forwarded to other sinks.

## Quick example

In AspNet Core applications, configure Serilog in `Program.cs` as follows to ensure telemetry logs are handled correctly:

```csharp
// Configure Serilog to ensure consistent structured logging
builder.Host.UseSerilog((context, services, configuration) =>
{
    configuration
        .ReadFrom.Configuration(context.Configuration)
        .Enrich.FromLogContext();
});
```

```csharp
public async Task Authorize(AuthorizeMessage msg)
{
    var fileAdapter = new FileLoggingTelemetryAdapter(TelemetryMode.PipelineAndErrors, logger);

    var pipe = new DataPipe<AuthorizeMessage> { Name = "Authorize", TelemetryMode = TelemetryMode.PipelineAndErrors };
    pipe.Use(new ExceptionAspect<AuthorizeMessage>());
    pipe.Use(new LoggingAspect<AuthorizeMessage>(logger, "Authorize Request"));
    pipe.Use(new TelemetryAspect<AuthorizeMessage>(fileAdapter));
    pipe.Add(
        new OnTimeoutRetry<AuthorizeMessage>(AppSettings.Instance.MaxRetries,
            new OpenSqlConnection<AuthorizeMessage>(AppSettings.Instance.ProxisDb,
                new GetUserClaims())));

    await pipe.Invoke(msg);
}
```

## Serilog example (recommended)

The project ships a Serilog configuration that excludes telemetry messages from the normal logger pipeline and routes them to a dedicated logger. The adapter marks telemetry logs with an `IsTelemetry` scope so you can filter them easily.

```json
  "Serilog": {
    "WriteTo": [
      {
        "Name": "Logger",
        "Args": {
          "configureLogger": {
            "Filter": [ { "Name": "ByExcluding", "Args": { "expression": "IsTelemetry = true" } } ],
            "WriteTo": [ { "Name": "Console" } ]
          }
        }
      },
      {
        "Name": "Logger",
        "Args": {
          "configureLogger": {
            "Filter": [ { "Name": "ByIncludingOnly", "Args": { "expression": "IsTelemetry = true" } } ],
            "WriteTo": [
              {
                "Name": "File",
                "Args": { "path": "Telemetry/telemetry-.jsonl", "rollingInterval": "Day", "outputTemplate": "{Message:lj}" }
              }
            ]
          }
        }
      }
    ]
  }
```


How it works (implementation notes)

- Batching: `FileLoggingTelemetryAdapter` collects `TelemetryEvent` instances in an in-memory list for the lifetime of a pipeline invocation. The adapter is expected to be flushed (via `Flush()`) once the pipeline completes.
- Enrichment: When flushing, the adapter creates a `TelemetryBatch` object that includes pipeline-level metadata (pipeline id/name, start/end time, duration, outcome and reason) together with the list of events.
- Serialization: The batch is serialized using `System.Text.Json` with camelCase naming, null-value suppression and enum string conversion.
- Logging: The adapter writes the JSON string to the provided `ILogger` inside a scope that sets `IsTelemetry = true`. This makes it easy to route or filter telemetry records, e.g. with Serilog filters.

Key public types

- `TelemetryBatch` - POCO that wraps pipeline-level metadata and the `events` list. Useful as the wire format when sending telemetry to files, log stores or web endpoints.
- `FileLoggingTelemetryAdapter` - implements `ITelemetryAdapter`. Construct it with a `TelemetryMode`, an `ILogger` and an optional `ITelemetryPolicy`.

Constructor

- `FileLoggingTelemetryAdapter(TelemetryMode mode, ILogger logger, ITelemetryPolicy policy = null)`
  - `mode`: controls whether the adapter is enabled. When `TelemetryMode.Off` the adapter ignores events.
  - `logger`: the target `ILogger` used to emit the serialized batch.
  - `policy`: optional filter policy to decide which events should be captured. Defaults to `DefaultCaptureEverythingPolicy`.

Behavior details and recommendations

- Threading: The adapter is not thread-safe. For parallel pipeline executions you should use one adapter instance per execution (the usual pattern when creating the adapter inside the pipeline invocation).
- Memory: Events are kept in memory until `Flush()` is called. For extremely chatty pipelines consider a policy that limits sequence length or a streaming export adapter.
- Failure handling: The adapter writes to `ILogger`; if serialization or logging throws, catch and handle that at a higher level (the adapter does not perform retries or persistence).
- Policy: Use an `ITelemetryPolicy` to limit sensitive data or volume. Example policies include `DefaultCaptureEverythingPolicy` and any custom policies you add.

Adapting to your logging stack

- Serilog: recommended - use the `IsTelemetry` scope to route telemetry to a separate sink or file.
- Any ILogger provider: since the adapter uses `ILogger` it will work with NLog, Microsoft Logging to file, Elastic.CommonSchema sinks, Application Insights providers, etc.
- Sending remotely: swap the final `ILogger.LogInformation` call for code that POSTs the serialized JSON to an HTTP endpoint or writes it to a durable file.

Example internals (reference)

The adapter serializes a `TelemetryBatch` similar to:

```csharp
public class TelemetryBatch
{
    public string PipelineId { get; set; }
    public string PipelineName { get; set; }
    public DateTimeOffset StartTime { get; set; }
    public DateTimeOffset EndTime { get; set; }
    public long Duration { get; set; }
    public string Outcome { get; set; }
    public string Reason { get; set; }
    public ServiceIdentity Service { get; set; }
    public List<TelemetryEvent> Events { get; set; } = new();
}

public sealed class FileLoggingTelemetryAdapter : ITelemetryAdapter
{
    // simplified for docs
    public FileLoggingTelemetryAdapter(TelemetryMode mode, ILogger logger, ITelemetryPolicy policy = null) { }
    public void Handle(TelemetryEvent evt) { }
    public void Flush() { }
}
```

Troubleshooting

- No telemetry in logs: ensure the adapter was registered in the pipeline via `TelemetryAspect` and that `Flush()` is called at the end of the execution.
- Telemetry filtered out: confirm your logging configuration does not exclude messages with `IsTelemetry = true` (the Serilog sample above purposely excludes them from the main logger and routes them separately).
- Large payloads: if batches are too large, introduce a policy to sample or truncate events before they are stored.

See also
- OpenTelemetryAspect.md - integration point for OpenTelemetry-style exporters.
- Telemetry policy files in `DataPipe.Core.Telemetry.Policies` for examples of filtering logic.

License
- Follows the project's license; this is a documentation example only.