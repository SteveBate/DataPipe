using System;
using System.Diagnostics;
using System.Threading.Tasks;
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
    public class RepeatUntil<T> : Filter<T>, IAmStructural where T : BaseMessage
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
            var structuralSw = Stopwatch.StartNew();
            var structuralOutcome = TelemetryOutcome.Success;
            var structuralReason = string.Empty;
            var conditionMet = false;

            // Build start attributes including any annotations from parent
            var startAttributes = new Dictionary<string, object>(msg.Execution.TelemetryAnnotations);
            msg.Execution.TelemetryAnnotations.Clear();

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
                        foreach (Filter<T> f in _filters)
                        {
                            var reason = string.Empty;
                            var fsw = Stopwatch.StartNew();
                            
                            // Check if this structural filter manages its own telemetry
                            var selfEmitting = f is IAmStructural structural && !structural.EmitTelemetryEvent;
                            var emitStart = f is not IAmStructural || (f is IAmStructural s && s.EmitTelemetryEvent);

                            if (!msg.ShouldStop && emitStart)
                            {
                                var @childStart = new TelemetryEvent
                                {
                                    Actor = msg.Actor,
                                    Component = f.GetType().Name.Split('`')[0],
                                    PipelineName = msg.PipelineName,
                                    Service = msg.Service,
                                    Scope = TelemetryScope.Filter,
                                    Role = f is IAmStructural ? FilterRole.Structural : FilterRole.Business,
                                    Phase = TelemetryPhase.Start,
                                    MessageId = msg.CorrelationId,
                                    Timestamp = DateTimeOffset.UtcNow,
                                    Attributes = f is IAmStructural ? new Dictionary<string, object>(msg.Execution.TelemetryAnnotations) : []
                                };
                                if (msg.ShouldEmitTelemetry(@childStart)) msg.OnTelemetry?.Invoke(@childStart);
                            }

                            if (!msg.ShouldStop)
                            {
                                msg.OnLog?.Invoke($"INVOKING: {f.GetType().Name.Split('`')[0]}");
                            }

                            var outcome = TelemetryOutcome.Success;
                            if (msg.ShouldStop)
                            {
                                outcome = TelemetryOutcome.Stopped;
                                reason = msg.Execution.Reason;
                                break;
                            }

                            try
                            {
                                await f.Execute(msg);
                            }
                            catch (Exception ex)
                            {
                                outcome = TelemetryOutcome.Exception;
                                reason = ex.Message;
                                structuralOutcome = TelemetryOutcome.Exception;
                                structuralReason = ex.Message;
                                throw;
                            }
                            finally
                            {
                                fsw.Stop();
                                
                                // Skip End event for self-emitting structural filters (they emit their own)
                                if (!selfEmitting)
                                {
                                    var @complete = new TelemetryEvent
                                    {
                                        Actor = msg.Actor,
                                        Component = f.GetType().Name.Split('`')[0],
                                        PipelineName = msg.PipelineName,
                                        Service = msg.Service,
                                        Scope = TelemetryScope.Filter,
                                        Role = f is IAmStructural ? FilterRole.Structural : FilterRole.Business,
                                        Phase = TelemetryPhase.End,
                                        MessageId = msg.CorrelationId,
                                        Outcome = msg.ShouldStop ? TelemetryOutcome.Stopped : outcome,
                                        Reason = msg.ShouldStop ? msg.Execution.Reason : reason,
                                        Timestamp = DateTimeOffset.UtcNow,
                                        DurationMs = fsw.ElapsedMilliseconds,
                                        Attributes = msg.Execution.TelemetryAnnotations.Count != 0 ? new Dictionary<string, object>(msg.Execution.TelemetryAnnotations) : []
                                    };
                                    msg.Execution.TelemetryAnnotations.Clear();
                                    if (msg.ShouldEmitTelemetry(@complete)) msg.OnTelemetry?.Invoke(@complete);
                                }

                                if (msg.ShouldStop && f is not IAmStructural)
                                {
                                    outcome = TelemetryOutcome.Stopped;
                                    reason = msg.Execution.Reason;
                                    msg.OnLog?.Invoke($"STOPPED: {msg.Execution.Reason}");
                                }

                                msg.OnLog?.Invoke($"COMPLETED: {f.GetType().Name.Split('`')[0]} ({fsw.ElapsedMilliseconds}ms)");
                            }
                        }
                        
                        // Check condition after each iteration
                        conditionMet = _callback(msg);
                    }
                    while (!conditionMet);

                    msg.Execution.Reset();
                }
            }
            finally
            {
                structuralSw.Stop();
                
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
                    DurationMs = structuralSw.ElapsedMilliseconds,
                    Attributes = new Dictionary<string, object> { ["condition"] = conditionMet }
                };
                if (msg.ShouldEmitTelemetry(@end)) msg.OnTelemetry?.Invoke(@end);
                
                msg.Execution.TelemetryAnnotations.Clear();
            }
        }
    }
}