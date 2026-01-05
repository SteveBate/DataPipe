using DataPipe.Core;
using DataPipe.Core.Contracts.Internal;
using DataPipe.Core.Telemetry;
using DataPipe.Sql.Contracts;
using Microsoft.Data.SqlClient;
using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace DataPipe.Sql.Filters
{

    /// <summary>
    /// Manages the lifecycle of a SQL database connection and orchestrates the execution of chained filters.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This class is responsible for:
    /// <list type="bullet">
    /// <item><description>Opening a SQL connection using the provided connection string</description></item>
    /// <item><description>Creating a SQL command object associated with the connection</description></item>
    /// <item><description>Sequentially executing a chain of filters that process the command</description></item>
    /// <item><description>Properly disposing of resources (connection and command) when execution completes</description></item>
    /// </list>
    /// </para>
    /// <para>
    /// The filter chain execution respects the <see cref="BaseMessage.Execution"/> state, allowing
    /// any filter in the chain to signal early termination by setting <see cref="Execution.IsStopped"/> to true.
    /// </para>
    /// </remarks>
    /// <typeparam name="T">The message type that must inherit from <see cref="BaseMessage"/> and implement <see cref="ISqlCommand"/></typeparam>
    /// <param name="connectionString">The SQL connection string used to establish a database connection</param>
    /// <param name="filters">A variable-length array of filters to execute sequentially in the pipeline</param>
    public sealed class OpenSqlConnection<T>(string connectionString, params Filter<T>[] filters) : Filter<T>, IAmStructural where T : BaseMessage, IUseSqlCommand
    {
        public bool EmitTelemetryEvent => false;

        /// <summary>
        /// Asynchronously executes the filter chain with an open SQL connection and command.
        /// </summary>
        /// <param name="msg">The message object containing the SQL command context to be populated and processed</param>
        /// <returns>A task representing the asynchronous operation</returns>
        public async Task Execute(T msg)
        {
            // Track timing and outcome for this structural filter
            var structuralSw = Stopwatch.StartNew();
            var structuralOutcome = TelemetryOutcome.Success;
            var structuralReason = string.Empty;
            string databaseName = string.Empty;

            using var cnn = new SqlConnection(connectionString);
            await cnn.OpenAsync(msg.CancellationToken);
            databaseName = cnn.Database;

            if (msg.Command != null)
            {
                msg.OnLog?.Invoke($"WARNING: msg.Command was already set. Overwriting existing command.");
            }

            // Build start attributes including any annotations from parent (e.g., retry info)
            var startAttributes = new Dictionary<string, object>(msg.Execution.TelemetryAnnotations)
            {
                ["database"] = databaseName
            };
            
            // Clear annotations after consuming them for Start event
            msg.Execution.TelemetryAnnotations.Clear();

            var @cnnStart = new TelemetryEvent
            {
                Component = nameof(OpenSqlConnection<T>),
                PipelineName = msg.PipelineName,
                Service = msg.Service,
                Scope = TelemetryScope.Filter,
                Role = FilterRole.Structural,
                Phase = TelemetryPhase.Start,
                MessageId = msg.CorrelationId,
                Timestamp = DateTimeOffset.UtcNow,
                Attributes = startAttributes
            };
            if (msg.ShouldEmitTelemetry(@cnnStart)) msg.OnTelemetry?.Invoke(@cnnStart);

            try
            {
                using (msg.Command = cnn.CreateCommand())
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

                            if (msg.ShouldStop)
                            {
                                msg.OnLog?.Invoke($"STOPPED: {msg.Execution.Reason}");
                            }
                            
                            msg.OnLog?.Invoke($"COMPLETED: {f.GetType().Name.Split('`')[0]}");
                        }
                    }
                }
                
                msg.Command = null!;
            }
            catch (Exception ex)
            {
                structuralOutcome = TelemetryOutcome.Exception;
                structuralReason = ex.Message;
                throw;
            }
            finally
            {
                structuralSw.Stop();
                
                var @cnnEnd = new TelemetryEvent
                {
                    Component = nameof(OpenSqlConnection<T>),
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
                if (msg.ShouldEmitTelemetry(@cnnEnd)) msg.OnTelemetry?.Invoke(@cnnEnd);
                
                // Clear any remaining annotations to prevent leaking
                msg.Execution.TelemetryAnnotations.Clear();
            }
        }
    }
}
