using System.Diagnostics;
using DataPipe.Core.Contracts.Internal;
using DataPipe.Core.Telemetry;

namespace DataPipe.Core;

/// <summary>
/// Internal helper that executes filters with telemetry instrumentation.
/// Used by structural filters to avoid duplicating the per-filter telemetry loop.
/// </summary>
internal static class FilterRunner
{
    /// <summary>
    /// Executes a single filter with full telemetry instrumentation.
    /// Returns true if the filter was executed, false if ShouldStop was detected
    /// before execution (caller decides break vs return).
    /// Exceptions propagate after the End telemetry event is emitted.
    /// </summary>
    internal static async Task<bool> ExecuteWithTelemetryAsync<T>(
        Filter<T> filter, T msg, string pipelineName) where T : BaseMessage
    {
        var reason = string.Empty;
        var telemetryEnabled = msg.TelemetryMode != TelemetryMode.Off;
        var timingEnabled = telemetryEnabled || msg.EnableTimings;
        Stopwatch? fsw = timingEnabled ? Stopwatch.StartNew() : null;

        var selfEmitting = filter is IAmStructural structural && !structural.EmitTelemetryEvent;
        var emitStart = filter is not IAmStructural || (filter is IAmStructural s && s.EmitTelemetryEvent);

        if (telemetryEnabled && !msg.ShouldStop && emitStart)
        {
            var @start = new TelemetryEvent
            {
                Actor = msg.Actor,
                Component = filter.GetType().Name.Split('`')[0],
                PipelineName = pipelineName,
                Service = msg.Service,
                Scope = TelemetryScope.Filter,
                Role = filter is IAmStructural ? FilterRole.Structural : FilterRole.Business,
                Phase = TelemetryPhase.Start,
                MessageId = msg.CorrelationId,
                Timestamp = DateTimeOffset.UtcNow,
                Attributes = filter is IAmStructural
                    ? new Dictionary<string, object>(msg.Execution.TelemetryAnnotations)
                    : []
            };
            if (msg.ShouldEmitTelemetry(@start)) msg.OnTelemetry?.Invoke(@start);
        }

        if (!msg.ShouldStop)
        {
            msg.OnLog?.Invoke($"INVOKING: {filter.GetType().Name.Split('`')[0]}");
        }

        var outcome = TelemetryOutcome.Success;
        if (msg.ShouldStop)
        {
            outcome = TelemetryOutcome.Stopped;
            reason = msg.Execution.Reason;
            return false;
        }

        try
        {
            await filter.Execute(msg).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            outcome = TelemetryOutcome.Exception;
            reason = ex.Message;
            throw;
        }
        finally
        {
            fsw?.Stop();

            if (telemetryEnabled && !selfEmitting)
            {
                var @complete = new TelemetryEvent
                {
                    Actor = msg.Actor,
                    Component = filter.GetType().Name.Split('`')[0],
                    PipelineName = pipelineName,
                    Service = msg.Service,
                    Scope = TelemetryScope.Filter,
                    Role = filter is IAmStructural ? FilterRole.Structural : FilterRole.Business,
                    Phase = TelemetryPhase.End,
                    MessageId = msg.CorrelationId,
                    Outcome = msg.ShouldStop ? TelemetryOutcome.Stopped : outcome,
                    Reason = msg.ShouldStop ? msg.Execution.Reason : reason,
                    Timestamp = DateTimeOffset.UtcNow,
                    DurationMs = fsw?.ElapsedMilliseconds ?? 0,
                    Attributes = msg.Execution.HasTelemetryAnnotations
                        ? new Dictionary<string, object>(msg.Execution.TelemetryAnnotations)
                        : []
                };
                msg.Execution.ClearTelemetryAnnotations();
                if (msg.ShouldEmitTelemetry(@complete)) msg.OnTelemetry?.Invoke(@complete);
            }

            if (msg.ShouldStop && filter is not IAmStructural)
            {
                msg.OnLog?.Invoke($"STOPPED: {msg.Execution.Reason}");
            }

            var name = filter.GetType().Name.Split('`')[0];
            msg.OnLog?.Invoke(fsw != null
                ? $"COMPLETED: {name} ({fsw.ElapsedMilliseconds}ms)"
                : $"COMPLETED: {name}");
        }

        return true;
    }

    /// <summary>
    /// Executes an array of filters sequentially with telemetry.
    /// Returns true if all filters completed, false if stopped.
    /// Exceptions propagate.
    /// </summary>
    internal static async Task<bool> ExecuteFiltersAsync<T>(
        Filter<T>[] filters, T msg, string pipelineName) where T : BaseMessage
    {
        foreach (var f in filters)
        {
            if (!await ExecuteWithTelemetryAsync(f, msg, pipelineName).ConfigureAwait(false))
                return false;
        }
        return true;
    }
}
