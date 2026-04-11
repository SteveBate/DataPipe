using DataPipe.Core.Contracts;

namespace DataPipe.Core.Telemetry.Adapters;

/// <summary>
/// Fans out telemetry events to multiple adapters, allowing you to combine
/// logging, metrics, and custom adapters in a single pipeline.
///
/// Example:
///    var adapter = new CompositeTelemetryAdapter(
///        new StructuredJsonTelemetryAdapter(logger, policy),
///        new MetricsTelemetryAdapter(meterFactory));
///    pipeline.Use(new TelemetryAspect&lt;OrderMessage&gt;(adapter));
/// </summary>
public sealed class CompositeTelemetryAdapter : ITelemetryAdapter
{
    private readonly IReadOnlyList<ITelemetryAdapter> _adapters;

    public CompositeTelemetryAdapter(params ITelemetryAdapter[] adapters)
    {
        ArgumentNullException.ThrowIfNull(adapters);
        _adapters = adapters;
    }

    public void Handle(TelemetryEvent evt)
    {
        for (var i = 0; i < _adapters.Count; i++)
            _adapters[i].Handle(evt);
    }

    public void Flush()
    {
        for (var i = 0; i < _adapters.Count; i++)
            _adapters[i].Flush();
    }
}
