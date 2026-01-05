using System;
using System.Diagnostics;
using System.Threading.Tasks;
using DataPipe.Core.Contracts.Internal;
using DataPipe.Core.Telemetry;

namespace DataPipe.Core.Filters
{

    /// <summary>
    /// A filter that repeatedly executes a sequence of child filters until an execution stop signal is received.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The <see cref="Repeat{T}"/> filter implements a loop-based execution pattern that continuously
    /// processes a message through a chain of child filters. The repetition continues until the message's
    /// <see cref="BaseMessage.Execution"/> state is marked as stopped via <see cref="IsStopped"/>.
    /// </para>
    /// <para>
    /// During each iteration, all child filters are executed sequentially in the order they were provided.
    /// If any child filter sets <see cref="BaseMessage.Execution.IsStopped"/> to true, the current iteration
    /// is interrupted and the loop condition is checked.
    /// </para>
    /// <para>
    /// After the loop terminates, the execution state is reset via <see cref="BaseMessage.Execution.Reset()"/>
    /// to ensure that filters executed after this <see cref="Repeat{T}"/> instance are not affected by the
    /// stopped state.
    /// </para>
    /// <para>
    /// This filter is useful for  any workflow that requires repeated processing of a message 
    /// until the caller determines that no further processing is needed and signals to stop execution.
    /// </para>
    /// </remarks>
    /// <typeparam name="T">The message type, must derive from <see cref="BaseMessage"/>.</typeparam>
    public class Repeat<T> : Filter<T>, IAmStructural where T : BaseMessage
    {
        public bool EmitTelemetryEvent => true;

        private readonly Filter<T>[] _filters;

        public Repeat(params Filter<T>[] filters)
        {
            _filters = filters;
        }

        public async Task Execute(T msg)
        {
            do
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
                        var @start = new TelemetryEvent
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

            } while (!msg.Execution.IsStopped);

            msg.Execution.Reset(); // for any filters that come after
        }
    }
}