using System.Text.Json;
using System.Text.Json.Serialization;
using DataPipe.Core.Contracts;
using DataPipe.Core.Telemetry.Policies;
using Microsoft.Extensions.Logging;

namespace DataPipe.Core.Telemetry.Adapters;

/// <summary>
/// A telemetry adapter that batches telemetry events in memory and writes them to disk when Flush() is called.
/// Events are wrapped in a TelemetryBatch parent object for enriched context and serialized as JSON.
/// </summary>
public sealed class StructuredJsonTelemetryAdapter : ITelemetryAdapter
{
    private readonly ILogger _logger;
    private readonly ITelemetryPolicy _policy;
    private readonly JsonSerializerOptions _options;
    private readonly List<TelemetryEvent> _eventBatch;

    /// <summary>
    /// Initializes a new instance of the StructuredJsonTelemetryAdapter.
    /// Writes batched telemetry events to the provided ILogger in JSONL format.
    /// Output is particularly useful for ingestion into log management systems such as Snowflake or Splunk.
    /// </summary>
    /// <param name="logger">The ILogger instance to write batched events to.</param>
    /// <param name="writeIndented">If true, JSON output will be formatted with indentation for readability.</param>
    public StructuredJsonTelemetryAdapter(ILogger logger, ITelemetryPolicy? policy = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
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
        if (_eventBatch.Count == 0)
        {
            return;
        }

        var firstEvent = _eventBatch.First();
        var lastEvent = _eventBatch.Last();

        var batchOutcome = lastEvent.Outcome == TelemetryOutcome.None
            ? null
            : lastEvent.Outcome.ToString();

        var batchReason =
            batchOutcome == TelemetryOutcome.Success.ToString()
                ? null
                : string.IsNullOrWhiteSpace(lastEvent.Reason)
                    ? null
                    : lastEvent.Reason;

        var batch = new TelemetryBatch
        {
            PipelineId = firstEvent.MessageId.ToString(),
            PipelineName = firstEvent.PipelineName,
            StartTime = firstEvent.Timestamp,
            EndTime = lastEvent.Timestamp,
            DurationMs = lastEvent.DurationMs ?? 0,
            Outcome = batchOutcome,
            Reason = batchReason,
            Service = firstEvent.Service,
            Actor = firstEvent.Actor,
            Events = _eventBatch.ToList()
        };

        using (_logger.BeginScope(new Dictionary<string, object> { ["IsTelemetry"] = true }))
        {
            var json = JsonSerializer.Serialize(batch, _options);
            _logger.LogInformation("{TelemetryBatch}", json + Environment.NewLine);
            _eventBatch.Clear();
        }
    }
}