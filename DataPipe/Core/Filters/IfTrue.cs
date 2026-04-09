using System;
using System.Diagnostics;
using DataPipe.Core.Contracts.Internal;
using DataPipe.Core.Telemetry;

namespace DataPipe.Core.Filters
{
    /// <summary>
    /// IfTrue allows you to conditionally execute one or more filters i.e. branching logic within a pipeline.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public class IfTrue<T> : Filter<T>, IAmStructural where T : BaseMessage
    {
        public bool EmitTelemetryEvent => false; // emit own start/end events to include decision attribute

        private readonly Func<T, bool> _callback;
        private readonly Filter<T>[] _filters;

        public IfTrue(Func<T, bool> callback, params Filter<T>[] filters)
        {
            _callback = callback;
            _filters = filters;
        }

        public async Task Execute(T msg)
        {
            // Track timing and outcome for this structural filter
            var telemetryEnabled = msg.TelemetryMode != TelemetryMode.Off;
            var timingEnabled = telemetryEnabled || msg.EnableTimings;
            Stopwatch? structuralSw = timingEnabled ? Stopwatch.StartNew() : null;
            var structuralOutcome = TelemetryOutcome.Success;
            var structuralReason = string.Empty;
            
            var result = _callback(msg);

            // Build start attributes including decision and any annotations from parent
            var startAttributes = new Dictionary<string, object>(msg.Execution.TelemetryAnnotations)
            {
                ["condition"] = result
            };
            msg.Execution.ClearTelemetryAnnotations();

            var @start = new TelemetryEvent
            {
                Actor = msg.Actor,
                Component = nameof(IfTrue<T>),
                PipelineName = msg.PipelineName,
                Service = msg.Service,
                Scope = TelemetryScope.Filter,
                Role = FilterRole.Structural,
                Phase = TelemetryPhase.Start,
                MessageId = msg.CorrelationId,
                Timestamp = DateTimeOffset.UtcNow,
                Attributes = startAttributes
            };
            if (msg.ShouldEmitTelemetry(@start)) msg.OnTelemetry?.Invoke(@start);

            try
            {
                if (result)
                {
                    await FilterRunner.ExecuteFiltersAsync(_filters, msg, msg.PipelineName).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                structuralOutcome = TelemetryOutcome.Exception;
                structuralReason = ex.Message;
                throw;
            }
            finally
            {
                structuralSw?.Stop();
                
                var @end = new TelemetryEvent
                {
                    Actor = msg.Actor,
                    Component = nameof(IfTrue<T>),
                    PipelineName = msg.PipelineName,
                    Service = msg.Service,
                    Scope = TelemetryScope.Filter,
                    Role = FilterRole.Structural,
                    Phase = TelemetryPhase.End,
                    MessageId = msg.CorrelationId,
                    Outcome = structuralOutcome,
                    Reason = structuralReason,
                    Timestamp = DateTimeOffset.UtcNow,
                    DurationMs = structuralSw?.ElapsedMilliseconds ?? 0,
                };
                if (msg.ShouldEmitTelemetry(@end)) msg.OnTelemetry?.Invoke(@end);
                
                msg.Execution.ClearTelemetryAnnotations();
            }
        }
    }
}