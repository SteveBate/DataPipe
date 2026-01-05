using System;
using System.Diagnostics;
using System.Threading.Tasks;
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
            var structuralSw = Stopwatch.StartNew();
            var structuralOutcome = TelemetryOutcome.Success;
            var structuralReason = string.Empty;
            
            var result = _callback(msg);

            // Build start attributes including decision and any annotations from parent
            var startAttributes = new Dictionary<string, object>(msg.Execution.TelemetryAnnotations)
            {
                ["condition"] = result
            };
            msg.Execution.TelemetryAnnotations.Clear();

            var @start = new TelemetryEvent
            {
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
                    foreach (var f in _filters)
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
                            return;
                        }

                        try
                        {
                            await f.Execute(msg);
                        }
                        catch (Exception ex)
                        {
                            outcome = TelemetryOutcome.Exception;
                            reason = ex.Message;
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
                                    Duration = fsw.ElapsedMilliseconds,
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

                            msg.OnLog?.Invoke($"COMPLETED: {f.GetType().Name.Split('`')[0]}");
                        }
                    }
                }
            }
            finally
            {
                structuralSw.Stop();
                
                var @end = new TelemetryEvent
                {
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
                    Duration = structuralSw.ElapsedMilliseconds,
                };
                if (msg.ShouldEmitTelemetry(@end)) msg.OnTelemetry?.Invoke(@end);
                
                msg.Execution.TelemetryAnnotations.Clear();
            }
        }
    }
}