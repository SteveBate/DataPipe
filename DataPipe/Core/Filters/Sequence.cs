using DataPipe.Core;
using DataPipe.Core.Contracts.Internal;
using DataPipe.Core.Telemetry;
using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace DataPipe.Core.Filters
{
    /// <summary>
    /// Sequence<T> allows you to execute multiple filters sequentially in a block.
    /// Provides a way to group filters that belong together.
    /// </summary>
    /// <typeparam name="T">The type of message being processed.</typeparam>
    /// <param name="filters">The filters to execute.</param>
    public class Sequence<T>(params Filter<T>[] filters) : Filter<T>, IAmStructural where T : BaseMessage
    {
        public bool EmitTelemetryEvent => true;

        public async Task Execute(T msg)
        {
            foreach (var f in filters)
            {
                var reason = string.Empty;
                var fsw = Stopwatch.StartNew();
                
                // Check if this structural filter manages its own telemetry
                var selfEmitting = f is IAmStructural structural && !structural.EmitTelemetryEvent;
                var emitStart = f is not IAmStructural || (f is IAmStructural s && s.EmitTelemetryEvent);

                if (!msg.ShouldStop && emitStart)
                {
                    var @start = new TelemetryEvent
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
                    if (msg.ShouldEmitTelemetry(@start)) msg.OnTelemetry?.Invoke(@start);
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

        }
    }
}
