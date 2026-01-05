using System.Text.Json;
using System.Text.Json.Serialization;
using DataPipe.Core.Contracts;
using DataPipe.Core.Telemetry.Policies;

namespace DataPipe.Core.Telemetry.Adapters;

/// <summary>
/// A telemetry adapter that outputs telemetry events to the console as JSON.
/// </summary>
public sealed class JsonConsoleTelemetryAdapter : ITelemetryAdapter
{
    private readonly ITelemetryPolicy _policy;
    private readonly JsonSerializerOptions _options;

    public JsonConsoleTelemetryAdapter(bool writeIndented = false) : this(new DefaultCaptureEverythingPolicy(), writeIndented)
    {
    }

    public JsonConsoleTelemetryAdapter(ITelemetryPolicy policy, bool writeIndented = false)
    {
        _policy = policy ?? new DefaultCaptureEverythingPolicy();
        _options = new JsonSerializerOptions
        {
            WriteIndented = writeIndented,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
        _options.Converters.Add(new JsonStringEnumConverter());
    }

    public void Flush()
    {
        Console.WriteLine("--- Telemetry Flush ---");
    }

    public void Handle(TelemetryEvent evt)
    {
        if (_policy.ShouldInclude(evt))
        {
            Console.WriteLine(JsonSerializer.Serialize(evt, _options));
        }
    }
}
