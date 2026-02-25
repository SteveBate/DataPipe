using DataPipe.Core;
using DataPipe.Core.Contracts.Internal;
using DataPipe.Core.Telemetry;
using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace DataPipe.Core.Filters
{
    /// <summary>
    /// A filter that implements a policy pattern by dynamically selecting and executing
    /// one or more filters based on the message content. This allows for conditional filter execution
    /// where the specific filter to run is determined at runtime.
    /// </summary>
    /// <typeparam name="T">The type of message this policy operates on, must derive from BaseMessage.</typeparam>
    /// <remarks>
    /// The Policy filter acts as a selector that uses a provided delegate function to determine
    /// which filter should be executed for a given message. If no filter is selected (null is returned),
    /// or if the message execution has been stopped, the policy will short-circuit and return without
    /// executing any filter.
    /// 
    /// This pattern is useful for implementing:
    /// - Conditional routing based on message properties
    /// - Dynamic filter selection logic
    /// - Chainable policy decisions in a message processing pipeline
    /// 
    /// Example usage:
    /// <code>
    /// var policy = new Policy&lt;MyMessage&gt;(msg => 
    ///     msg.Priority == High ? new HighPriorityFilter() : new NormalPriorityFilter());
    /// </code>
    /// </remarks>
    public class Policy<T> : Filter<T>, IAmStructural where T : BaseMessage
    {
        public bool EmitTelemetryEvent => false; // emit own start/end events to include decision attribute

        private readonly Func<T, Filter<T>> _selector;

        public Policy(Func<T, Filter<T>> selector)
        {
            _selector = selector;
        }

        public async Task Execute(T msg)
        {
            // Track timing and outcome for this structural filter
            var structuralSw = Stopwatch.StartNew();
            var structuralOutcome = TelemetryOutcome.Success;
            var structuralReason = string.Empty;
            Filter<T>? filter = null;
            string decision = "null";

            // Build start attributes including any annotations from parent
            var startAttributes = new Dictionary<string, object>(msg.Execution.TelemetryAnnotations);
            msg.Execution.TelemetryAnnotations.Clear();

            try
            {
                // Try to get the filter from the selector - may throw
                filter = _selector(msg);
                decision = filter?.GetType().Name.Split('`')[0] ?? "null";
            }
            catch (Exception ex)
            {
                // Selector threw an exception - emit start/end events with exception info
                decision = "Exception";
                startAttributes["decision"] = decision;
                startAttributes["exception"] = ex.GetType().Name;
                
                var @exStart = new TelemetryEvent
                {
                    Actor = msg.Actor,
                    Component = nameof(Policy<T>),
                    PipelineName = msg.PipelineName,
                    Service = msg.Service,
                    Scope = TelemetryScope.Filter,
                    Role = FilterRole.Structural,
                    Phase = TelemetryPhase.Start,
                    MessageId = msg.CorrelationId,
                    Timestamp = DateTimeOffset.UtcNow,
                    Attributes = startAttributes
                };
                if (msg.ShouldEmitTelemetry(@exStart)) msg.OnTelemetry?.Invoke(@exStart);
                
                structuralSw.Stop();
                var @exEnd = new TelemetryEvent
                {
                    Actor = msg.Actor,
                    Component = nameof(Policy<T>),
                    PipelineName = msg.PipelineName,
                    Service = msg.Service,
                    Scope = TelemetryScope.Filter,
                    Role = FilterRole.Structural,
                    Phase = TelemetryPhase.End,
                    MessageId = msg.CorrelationId,
                    Outcome = TelemetryOutcome.Exception,
                    Reason = ex.Message,
                    Timestamp = DateTimeOffset.UtcNow,
                    DurationMs = structuralSw.ElapsedMilliseconds,
                    Attributes = new Dictionary<string, object> { ["decision"] = decision, ["exception"] = ex.GetType().Name }
                };
                if (msg.ShouldEmitTelemetry(@exEnd)) msg.OnTelemetry?.Invoke(@exEnd);
                
                throw;
            }

            if (filter == null || msg.Execution.IsStopped)
            {
                // Emit start/end events even when no filter selected
                startAttributes["decision"] = decision;
                
                var @policyStart = new TelemetryEvent
                {
                    Actor = msg.Actor,
                    Component = nameof(Policy<T>),
                    PipelineName = msg.PipelineName,
                    Service = msg.Service,
                    Scope = TelemetryScope.Filter,
                    Role = FilterRole.Structural,
                    Phase = TelemetryPhase.Start,
                    MessageId = msg.CorrelationId,
                    Timestamp = DateTimeOffset.UtcNow,
                    Attributes = startAttributes
                };
                if (msg.ShouldEmitTelemetry(@policyStart)) msg.OnTelemetry?.Invoke(@policyStart);
                
                structuralSw.Stop();
                var @policyEnd = new TelemetryEvent
                {
                    Actor = msg.Actor,
                    Component = nameof(Policy<T>),
                    PipelineName = msg.PipelineName,
                    Service = msg.Service,
                    Scope = TelemetryScope.Filter,
                    Role = FilterRole.Structural,
                    Phase = TelemetryPhase.End,
                    MessageId = msg.CorrelationId,
                    Outcome = msg.Execution.IsStopped ? TelemetryOutcome.Stopped : TelemetryOutcome.Success,
                    Reason = msg.Execution.Reason ?? "",
                    Timestamp = DateTimeOffset.UtcNow,
                    DurationMs = structuralSw.ElapsedMilliseconds,
                    Attributes = new Dictionary<string, object> { ["decision"] = decision }
                };
                if (msg.ShouldEmitTelemetry(@policyEnd)) msg.OnTelemetry?.Invoke(@policyEnd);
                return;
            }

            // Normal flow - filter was selected
            startAttributes["decision"] = decision;

            var @start = new TelemetryEvent
            {
                Actor = msg.Actor,
                Component = nameof(Policy<T>),
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
                var reason = string.Empty;
                var fsw = Stopwatch.StartNew();
                
                // Check if this structural filter manages its own telemetry
                var selfEmitting = filter is IAmStructural structural && !structural.EmitTelemetryEvent;
                var emitStart = filter is not IAmStructural || (filter is IAmStructural s && s.EmitTelemetryEvent);

                if (!msg.ShouldStop && emitStart)
                {
                    var @childStart = new TelemetryEvent
                    {
                        Actor = msg.Actor,
                        Component = filter.GetType().Name.Split('`')[0],
                        PipelineName = msg.PipelineName,
                        Service = msg.Service,
                        Scope = TelemetryScope.Filter,
                        Role = filter is IAmStructural ? FilterRole.Structural : FilterRole.Business,
                        Phase = TelemetryPhase.Start,
                        MessageId = msg.CorrelationId,
                        Timestamp = DateTimeOffset.UtcNow,
                        Attributes = filter is IAmStructural ? new Dictionary<string, object>(msg.Execution.TelemetryAnnotations) : []
                    };
                    if (msg.ShouldEmitTelemetry(@childStart)) msg.OnTelemetry?.Invoke(@childStart);
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
                    return;
                }

                try
                {
                    await filter.Execute(msg);
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
                            Component = filter.GetType().Name.Split('`')[0],
                            PipelineName = msg.PipelineName,
                            Service = msg.Service,
                            Scope = TelemetryScope.Filter,
                            Role = filter is IAmStructural ? FilterRole.Structural : FilterRole.Business,
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

                    msg.OnLog?.Invoke($"COMPLETED:  {filter.GetType().Name.Split('`')[0]} ({fsw.ElapsedMilliseconds}ms)");

                    if (msg.ShouldStop && filter is not IAmStructural)
                    {
                        outcome = TelemetryOutcome.Stopped;
                        reason = msg.Execution.Reason;
                        msg.OnLog?.Invoke($"STOPPED: {msg.Execution.Reason}");
                    }
                }
            }
            finally
            {
                structuralSw.Stop();
                
                var @end = new TelemetryEvent
                {
                    Actor = msg.Actor,
                    Component = nameof(Policy<T>),
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
                };
                if (msg.ShouldEmitTelemetry(@end)) msg.OnTelemetry?.Invoke(@end);
                
                msg.Execution.TelemetryAnnotations.Clear();
            }
        }
    }
}
