using DataPipe.Core;
using DataPipe.Core.Contracts;
using DataPipe.Core.Contracts.Internal;
using DataPipe.Core.Filters;
using DataPipe.Core.Telemetry;
using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Transactions;

namespace DataPipe.Sql.Filters
{
    /// <summary>
    /// A filter that wraps the execution of child filters within a database transaction scope.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This filter creates a new transaction scope with configurable isolation level and executes
    /// a chain of child filters within that transaction context. The transaction is only committed
    /// if the message's <see cref="BaseMessage.Commit"/> property is true at the end of filter execution.
    /// </para>
    /// <para>
    /// Key features:
    /// <list type="bullet">
    /// <item><description>Supports custom isolation levels (defaults to ReadCommitted)</description></item>
    /// <item><description>Enables async-friendly transaction flow</description></item>
    /// <item><description>Respects execution stop signals via <see cref="BaseMessage.Execution.IsStopped"/></description></item>
    /// <item><description>Provides logging callbacks for transaction lifecycle events</description></item>
    /// <item><description>Automatically rolls back if commit conditions are not met</description></item>
    /// </list>
    /// </para>
    /// </remarks>
    /// <typeparam name="T">The message type, constrained to inherit from BaseMessage and implement IAmCommittable</typeparam>
    public class StartTransaction<T> : Filter<T>, IAmStructural where T : BaseMessage, IAmCommittable
    {
        public bool EmitTelemetryEvent => false; // emit own start event rather than parent so we can capture isolation level and timeout

        private readonly IsolationLevel _isolationLevel;
        private readonly Filter<T>[] _filters;
        
        public StartTransaction(params Filter<T>[] filters) 
            : this(IsolationLevel.ReadCommitted, filters)
        {
        }

        public StartTransaction(IsolationLevel isolationLevel, params Filter<T>[] filters)
        {
            _filters = filters;
            _isolationLevel = isolationLevel;
        }

        public async Task Execute(T msg)
        {
            var options = new TransactionOptions { IsolationLevel = _isolationLevel, Timeout = TransactionManager.MaximumTimeout };

            // Track timing and outcome for this structural filter
            var structuralSw = Stopwatch.StartNew();
            var structuralOutcome = TelemetryOutcome.Success;
            var structuralReason = string.Empty;

            using (var scope = new TransactionScope(TransactionScopeOption.RequiresNew, options, TransactionScopeAsyncFlowOption.Enabled))
            {
                // Build start attributes including any annotations from parent (e.g., retry info)
                var startAttributes = new Dictionary<string, object>(msg.Execution.TelemetryAnnotations)
                {
                    ["isolation-level"] = options.IsolationLevel.ToString(),
                    ["timeout"] = options.Timeout.ToString()
                };
                
                // Clear annotations after consuming them for Start event
                msg.Execution.TelemetryAnnotations.Clear();

                var @txnStart = new TelemetryEvent
                {
                    Component = nameof(StartTransaction<T>),
                    PipelineName = msg.PipelineName,
                    Service = msg.Service,
                    Scope = TelemetryScope.Filter,
                    Role = FilterRole.Structural,
                    Phase = TelemetryPhase.Start,
                    MessageId = msg.CorrelationId,
                    Timestamp = DateTimeOffset.UtcNow,
                    Attributes = startAttributes
                };
                if (msg.ShouldEmitTelemetry(@txnStart)) msg.OnTelemetry?.Invoke(@txnStart);

                try
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
                            msg.OnLog?.Invoke($"COMPLETED: {f.GetType().Name.Split('`')[0]}");
                        }
                    }

                    if (msg.Commit)
                    {
                        if (msg.CancellationToken.IsCancellationRequested)
                        {
                            throw new OperationCanceledException("Operation was cancelled before committing transaction.");
                        }

                        if (msg.ShouldStop)
                        {
                            msg.OnLog?.Invoke($"INFO: TRANSACTION ROLLED BACK ({msg.Execution.Reason})");
                            return;
                        }

                        scope.Complete();
                        msg.OnLog?.Invoke($"INFO: TRANSACTION COMMITTED");
                    }
                    else
                    {
                        structuralReason = "Transaction rolled back (msg.Commit = false)";
                        msg.OnLog?.Invoke($"INFO: TRANSACTION ROLLED BACK (msg.Commit = false)");
                    }
                }
                catch (Exception ex)
                {
                    structuralOutcome = TelemetryOutcome.Exception;
                    structuralReason = ex.Message;
                    msg.OnLog?.Invoke($"INFO: TRANSACTION ROLLED BACK (EXCEPTION: {ex.Message})");
                    throw;
                }
                finally
                {
                    structuralSw.Stop();

                    // Build end attributes
                    var endAttributes = new Dictionary<string, object>
                    {
                        ["isolation-level"] = options.IsolationLevel.ToString(),
                        ["committed"] = msg.Commit && structuralOutcome == TelemetryOutcome.Success
                    };

                    var @txnEnd = new TelemetryEvent
                    {
                        Component = nameof(StartTransaction<T>),
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
                        Attributes = endAttributes
                    };
                    if (msg.ShouldEmitTelemetry(@txnEnd)) msg.OnTelemetry?.Invoke(@txnEnd);
                    
                    // Clear any remaining annotations to prevent leaking
                    msg.Execution.TelemetryAnnotations.Clear();
                }
            }
        }
    }
}