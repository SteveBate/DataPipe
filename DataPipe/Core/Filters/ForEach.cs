using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using DataPipe.Core.Contracts.Internal;
using DataPipe.Core.Telemetry;

namespace DataPipe.Core.Filters
{
    /// <summary>
    /// ForEach<TMessage, TItem> allows you to iterate over an enumerable in the message
    /// and execute one or more filters for each element.
    /// This is fully async and respects pipeline stop conditions.
    /// 
    /// Example Usages:
    /// 
    /// Simple string concatenation:
    /// // msg.Words = new[] { "this", "is", "a", "test" };
    /// pipe.Add(new ForEach<TestMessage, string>(
    ///     msg => msg.Words,
    ///     (msg, word) => msg.CurrentWord = word,
    ///     new ConcatenatingFilter()
    /// ));
    /// 
    /// Execute multiple filters for each item:
    /// pipe.Add(new ForEach<TestMessage, string>(
    ///     msg => msg.Words,
    ///     (msg, word) => msg.CurrentWord = word,
    ///     new ConcatenatingFilter(),
    ///     new LoggingFilter()
    /// ));
    /// 
    /// Stop the pipeline mid-loop:
    /// pipe.Add(new ForEach<TestMessage, string>(
    ///     msg => msg.Words,
    ///     (msg, word) =>
    ///     {
    ///         msg.CurrentWord = word;
    ///         if(word == "stop") msg.Execution.Stop("Hit stop word");
    ///     },
    ///     new ConcatenatingFilter()
    /// ));
    /// </summary>
    public class ForEach<TMessage, TItem> : Filter<TMessage>, IAmStructural where TMessage : BaseMessage
    {
        public bool EmitTelemetryEvent => true;

        private readonly Func<TMessage, IEnumerable<TItem>> _enumerableSelector;
        private readonly Action<TMessage, TItem> _itemSetter;
        private readonly Filter<TMessage>[] _filters;

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="enumerableSelector">Function to select the items to iterate over</param>
        /// <param name="itemSetter">Action to set the current item on the message</param>
        /// <param name="filters">One or more filters to execute per item</param>
        public ForEach(
            Func<TMessage, IEnumerable<TItem>> enumerableSelector,
            Action<TMessage, TItem> itemSetter,
            params Filter<TMessage>[] filters)
        {
            _enumerableSelector = enumerableSelector;
            _itemSetter = itemSetter;
            _filters = filters;
        }

        /// <summary>
        /// Execute iterates asynchronously over the enumerable and executes filters for each item
        /// </summary>
        /// <param name="msg">The pipeline message</param>
        public async Task Execute(TMessage msg)
        {
            var items = _enumerableSelector(msg);
            if (items == null) return;

            foreach (var item in items)
            {
                // Respect stop signals in the pipeline
                if (msg.Execution.IsStopped) break;

                // Set current item into the message
                _itemSetter(msg, item);

                // Execute all filters in sequence for this item
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
}
