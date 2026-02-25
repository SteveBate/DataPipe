# OpenTelemetry Aspect

DataPipe emits telemetry events when the TelemetryMode is set to any value other than Off for the pipeline. The events are structured to include information such as the message ID, pipeline name, component, scope, role, and service identity. TelemetryAspect provides powerful out-of-the-box integration with any monitoring system of your choice by forwarding these telemetry events to an ITelemetryAdapter implementation or to put it another way, an adapter. Included adapters include BasicConsoleTelemetryAdapter for simple logging and JsonConsoleTelemetryAdapter that can be used immediately to produce structured output to console. A more powerful adapter would be the OpenTelemetryAdapter provided below that allows the events to be captured as spans in an OpenTelemetry-compatible tracing system. In fact, you can create your own adapter to consume the events any way you see fit. e.g., an Azure Service Bus or Application Insights adapter. Simply implement the ITelemetryAdapter interface and handle the TelemetryEvent objects as required.

## Usage Example

```csharp
var tracerProvider = Sdk.CreateTracerProviderBuilder()
    .AddSource("DataPipe")
    .AddConsoleExporter()
    .Build();

var tracer = tracerProvider.GetTracer("DataPipe");

var pipe = new DataPipe<DataMessage>();
pipe.Use(new ExceptionAspect<TestMessage>());
pipe.Use(new TelemetryAspect<DataMessage>(new OpenTelemetryAdapter(tracer)));
pipe.Add(
    new SomeProcessingFilter(),
    new AnotherFilter()
    // Additional filters...
);

var msg = new DataMessage
{
    PipelineName = "OrderProcessingPipeline",
    Service = new ServiceIdentity
    {
        Name = "Orders.Api",
        Environment = "Production",
        Version = "1.0.0",
        InstanceId = "instance-123"
    }
};

await pipe.Execute(msg);
```

## OpenTelemetryAdapter Implementation

```csharp
using OpenTelemetry.Trace;
using System.Collections.Concurrent;

public sealed class OpenTelemetryAdapter : ITelemetryAdapter
{
    private readonly Tracer _tracer;

    // Tracks active spans per message + component
    private readonly ConcurrentDictionary<string, TelemetrySpan> _spans = new();

    public OpenTelemetryAdapter(Tracer tracer)
    {
        _tracer = tracer ?? throw new ArgumentNullException(nameof(tracer));
    }

    public void Handle(TelemetryEvent evt)
    {
        if (evt.Phase == TelemetryPhase.Start)
        {
            StartSpan(evt);
        }
        else if (evt.Phase == TelemetryPhase.End)
        {
            EndSpan(evt);
        }
    }

    private void StartSpan(TelemetryEvent evt)
    {
        var key = Key(evt);

        var span = _tracer.StartSpan(
            name: evt.Component,
            kind: SpanKind.Internal);

        span.SetAttribute("pipeline.name", evt.PipelineName);
        span.SetAttribute("component.role", evt.Role.ToString());
        span.SetAttribute("telemetry.scope", evt.Scope.ToString());

        if (evt.Service is not null)
        {
            span.SetAttribute("service.name", evt.Service.Name);
            span.SetAttribute("service.environment", evt.Service.Environment);
            span.SetAttribute("service.version", evt.Service.Version);
            span.SetAttribute("service.instance.id", evt.Service.InstanceId);
        }

        _spans[key] = new TelemetrySpan(span);
    }

    private void EndSpan(TelemetryEvent evt)
    {
        var key = Key(evt);

        if (!_spans.TryRemove(key, out var span))
            return;

        if (evt.Outcome == TelemetryOutcome.Failure)
        {
            span.Span.SetStatus(Status.Error, evt.Reason);
        }

        if (evt.Duration is not null)
        {
            span.Span.SetAttribute("duration.ms", evt.Duration.Value);
        }

        span.Span.End();
    }

    private static string Key(TelemetryEvent evt)
        => $"{evt.MessageId}:{evt.Component}";

    private sealed class TelemetrySpan
    {
        public TelemetrySpan(TelemetrySpanHandle span)
        {
            Span = span;
        }

        public TelemetrySpanHandle Span { get; }
    }
}

```