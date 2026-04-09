using DataPipe.Core;
using DataPipe.Core.Contracts;
using DataPipe.Core.Contracts.Internal;
using DataPipe.Core.Telemetry;
using DataPipe.Sql.Contracts;
using System;
using System.Data;
using System.Diagnostics;

namespace DataPipe.Sql.Filters
{
    /// <summary>
    /// A filter that wraps child filter execution in a local SQL transaction from the current SQL connection.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This filter expects <see cref="IUseSqlCommand.Command"/> to already be set (for example by
    /// <see cref="OpenSqlConnection{T}"/>), then starts a local transaction using
    /// <see cref="Microsoft.Data.SqlClient.SqlConnection.BeginTransactionAsync(IsolationLevel, System.Threading.CancellationToken)"/>.
    /// </para>
    /// <para>
    /// The transaction commits only when <see cref="BaseMessage.Commit"/> is true and execution has not been
    /// stopped. Otherwise, the transaction is rolled back when disposed.
    /// </para>
    /// </remarks>
    /// <typeparam name="T">The message type, constrained to inherit from BaseMessage and implement IUseSqlCommand and IAmCommittable.</typeparam>
    public sealed class StartSqlTransaction<T> : Filter<T>, IAmStructural where T : BaseMessage, IUseSqlCommand, IAmCommittable
    {
        public bool EmitTelemetryEvent => false; // emit own start event rather than parent so we can capture transaction details

        private readonly IsolationLevel _isolationLevel;
        private readonly Filter<T>[] _filters;

        public StartSqlTransaction(params Filter<T>[] filters)
            : this(IsolationLevel.ReadCommitted, filters)
        {
        }

        public StartSqlTransaction(IsolationLevel isolationLevel, params Filter<T>[] filters)
        {
            _filters = filters;
            _isolationLevel = isolationLevel;
        }

        public async Task Execute(T msg)
        {
            var command = msg.Command;
            if (command is null)
            {
                throw new InvalidOperationException($"{nameof(StartSqlTransaction<T>)} requires msg.Command to be set. Ensure it is nested inside {nameof(OpenSqlConnection<T>)}.");
            }

            var connection = command.Connection;
            if (connection is null)
            {
                throw new InvalidOperationException($"{nameof(StartSqlTransaction<T>)} requires msg.Command.Connection to be set.");
            }

            if (connection.State != ConnectionState.Open)
            {
                throw new InvalidOperationException($"{nameof(StartSqlTransaction<T>)} requires msg.Command.Connection to be open.");
            }

            var previousTransaction = command.Transaction;

            // Track timing and outcome for this structural filter
            var telemetryEnabled = msg.TelemetryMode != TelemetryMode.Off;
            var timingEnabled = telemetryEnabled || msg.EnableTimings;
            Stopwatch? structuralSw = timingEnabled ? Stopwatch.StartNew() : null;
            var structuralOutcome = TelemetryOutcome.Success;
            var structuralReason = string.Empty;
            var committed = false;

            // Build start attributes including any annotations from parent (e.g., retry info)
            var startAttributes = new Dictionary<string, object>(msg.Execution.TelemetryAnnotations)
            {
                ["isolation-level"] = _isolationLevel.ToString(),
                ["database"] = connection.Database
            };

            // Clear annotations after consuming them for Start event
            msg.Execution.ClearTelemetryAnnotations();

            var @txnStart = new TelemetryEvent
            {
                Actor = msg.Actor,
                Component = nameof(StartSqlTransaction<T>),
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
                await using (var transaction = await connection.BeginTransactionAsync(_isolationLevel, msg.CancellationToken).ConfigureAwait(false))
                {
                    var sqlTransaction = transaction as Microsoft.Data.SqlClient.SqlTransaction
                        ?? throw new InvalidOperationException("BeginTransactionAsync returned an unexpected transaction type.");

                    command.Transaction = sqlTransaction;

                    await FilterRunner.ExecuteFiltersAsync(_filters, msg, msg.PipelineName).ConfigureAwait(false);

                    if (msg.Commit)
                    {
                        if (msg.CancellationToken.IsCancellationRequested)
                        {
                            throw new OperationCanceledException("Operation was cancelled before committing transaction.");
                        }

                        if (msg.ShouldStop)
                        {
                            structuralOutcome = TelemetryOutcome.Stopped;
                            structuralReason = $"Transaction rolled back ({msg.Execution.Reason})";
                            msg.OnLog?.Invoke($"INFO: TRANSACTION ROLLED BACK ({msg.Execution.Reason})");
                            return;
                        }

                        await sqlTransaction.CommitAsync(msg.CancellationToken).ConfigureAwait(false);
                        committed = true;
                        msg.OnLog?.Invoke("INFO: TRANSACTION COMMITTED");
                    }
                    else
                    {
                        structuralReason = "Transaction rolled back (msg.Commit = false)";
                        msg.OnLog?.Invoke("INFO: TRANSACTION ROLLED BACK (msg.Commit = false)");
                    }
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
                command.Transaction = previousTransaction;

                structuralSw?.Stop();

                var endAttributes = new Dictionary<string, object>
                {
                    ["isolation-level"] = _isolationLevel.ToString(),
                    ["database"] = connection.Database,
                    ["committed"] = committed
                };

                var @txnEnd = new TelemetryEvent
                {
                    Actor = msg.Actor,
                    Component = nameof(StartSqlTransaction<T>),
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
                    Attributes = endAttributes
                };
                if (msg.ShouldEmitTelemetry(@txnEnd)) msg.OnTelemetry?.Invoke(@txnEnd);

                // Clear any remaining annotations to prevent leaking
                msg.Execution.ClearTelemetryAnnotations();
            }
        }
    }
}
