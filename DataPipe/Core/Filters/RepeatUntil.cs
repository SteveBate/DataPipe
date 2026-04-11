using System;
using System.Diagnostics;
using DataPipe.Core.Contracts.Internal;
using DataPipe.Core.Telemetry;

namespace DataPipe.Core.Filters
{
    /// <summary>
    /// A filter that repeatedly executes a sequence of child filters until a specified condition is met.
    /// </summary>
    /// <typeparam name="T">The type of message being processed. Must derive from <see cref="BaseMessage"/>.</typeparam>
    /// <remarks>
    /// <para>
    /// The <see cref="RepeatUntil{T}"/> filter provides a loop-based execution model where child filters
    /// are executed repeatedly until either:
    /// <list type="bullet">
    /// <item><description>The callback condition returns true (indicating the repeat should stop)</description></item>
    /// <item><description>The message execution is stopped via <see cref="BaseMessage.Execution.IsStopped"/></description></item>
    /// </list>
    /// </para>
    /// <para>
    /// Execution flow:
    /// <list type="number">
    /// <item><description>Evaluates the callback condition with the current message</description></item>
    /// <item><description>If the condition is false, enters the repeat loop</description></item>
    /// <item><description>Iterates through all child filters in order, executing each one</description></item>
    /// <item><description>If execution is stopped during iteration, breaks out of the filter loop</description></item>
    /// <item><description>Continues looping through all filters until execution is stopped</description></item>
    /// <item><description>Resets the execution state before returning</description></item>
    /// </list>
    /// </para>
    /// <para>
    /// If the callback condition returns true on the initial check, no filters are executed.
    /// </para>
    /// </remarks>
    public sealed class RepeatUntil<T> : Filter<T>, IAmStructural where T : BaseMessage
    {
        public bool EmitTelemetryEvent => false; // emit own start/end events to include condition attribute

        private readonly Filter<T>[] _filters;
        private Func<T, bool> _callback;

        public RepeatUntil(Func<T, bool> callback, params Filter<T>[] filters)
        {
            _filters = filters;
            _callback = callback;
        }

        public async Task Execute(T msg)
        {
            // Track timing and outcome for this structural filter
            var telemetryEnabled = msg.TelemetryMode != TelemetryMode.Off;
            var timingEnabled = telemetryEnabled || msg.EnableTimings;
            Stopwatch? structuralSw = timingEnabled ? Stopwatch.StartNew() : null;
            var structuralOutcome = TelemetryOutcome.Success;
            var structuralReason = string.Empty;
            var conditionMet = false;

            // Build start attributes including any annotations from parent
            var startAttributes = new Dictionary<string, object>(msg.Execution.TelemetryAnnotations);
            msg.Execution.ClearTelemetryAnnotations();

            var @start = new TelemetryEvent
            {
                Actor = msg.Actor,
                Component = nameof(RepeatUntil<T>),
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
                // Check initial condition
                conditionMet = _callback(msg);
                
                if (!conditionMet)
                {
                    do
                    {
                        await FilterRunner.ExecuteFiltersAsync(_filters, msg, msg.PipelineName).ConfigureAwait(false);
                        
                        // Check condition after each iteration
                        conditionMet = _callback(msg);
                    }
                    while (!conditionMet && !msg.Execution.IsStopped);

                    msg.Execution.Reset();
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
                    Component = nameof(RepeatUntil<T>),
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
                    Attributes = new Dictionary<string, object> { ["condition"] = conditionMet }
                };
                if (msg.ShouldEmitTelemetry(@end)) msg.OnTelemetry?.Invoke(@end);
                
                msg.Execution.ClearTelemetryAnnotations();
            }
        }
    }
}