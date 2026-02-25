using System.Text.Json;
using System.Text.Json.Serialization;
using DataPipe.Core.Contracts;
using DataPipe.Core.Telemetry.Policies;

namespace DataPipe.Core.Telemetry.Adapters;

/// <summary>
/// A telemetry adapter that batches telemetry events in memory and writes them to console when Flush() is called.
/// Events are wrapped in a TelemetryBatch parent object for enriched context and serialized as JSON.
/// Useful during development for quick feedback and debugging without needing a full logging infrastructure.
/// </summary>
public sealed class ConsoleTelemetryAdapter : ITelemetryAdapter
{
    private readonly ITelemetryPolicy _policy;
    private readonly JsonSerializerOptions _options;
    private readonly List<TelemetryEvent> _eventBatch;

    public ConsoleTelemetryAdapter(ITelemetryPolicy? policy = null)
    {
        _policy = policy ?? new DefaultCaptureEverythingPolicy();
        _options = new JsonSerializerOptions
        {
            WriteIndented = false,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
        _options.Converters.Add(new JsonStringEnumConverter());
        _eventBatch = new List<TelemetryEvent>();
    }

    public void Handle(TelemetryEvent evt)
    {
        if (!_policy.ShouldInclude(evt)) return;
        _eventBatch.Add(evt);

    }

    public void Flush()
    {
        if (_eventBatch.Count == 0) return;
        var json = JsonSerializer.Serialize(_eventBatch, _options);
        Console.WriteLine(json);

    }
}