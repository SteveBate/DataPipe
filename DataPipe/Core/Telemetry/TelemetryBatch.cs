using System.Text.Json.Serialization;

namespace DataPipe.Core.Telemetry;

/// <summary>
/// Represents a batch of telemetry events grouped by pipeline execution.
/// </summary>
public class TelemetryBatch
{
    [JsonPropertyName("pipelineId")]
    public string? PipelineId { get; set; }

    [JsonPropertyName("pipelineName")]
    public string? PipelineName { get; set; }

    [JsonPropertyName("startTime")]
    public DateTimeOffset StartTime { get; set; }

    [JsonPropertyName("endTime")]
    public DateTimeOffset EndTime { get; set; }

    [JsonPropertyName("durationMs")]
    public long DurationMs { get; set; }

    [JsonPropertyName("outcome")]
    public string? Outcome { get; set; }

    [JsonPropertyName("reason")]
    public string? Reason { get; set; }

    [JsonPropertyName("service")]
    public ServiceIdentity? Service { get; set; }

    [JsonPropertyName("actor")]
    public string? Actor { get; init; }

    [JsonPropertyName("events")]
    public List<TelemetryEvent> Events { get; set; } = new();
}
