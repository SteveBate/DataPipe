using System;
using System.Diagnostics;
using DataPipe.Core.Contracts.Internal;
using DataPipe.Core.Telemetry;

namespace DataPipe.Core.Filters
{
    /// <summary>
    /// Timeout enforces a maximum execution duration on child filters.
    /// If the child filters do not complete within the specified timeout,
    /// execution is cancelled via the message's <see cref="BaseMessage.CancellationToken"/>.
    /// A <see cref="TimeoutException"/> is thrown when the timeout elapses.
    /// </summary>
    /// <typeparam name="T">The message type. Must derive from <see cref="BaseMessage"/>.</typeparam>
    public class Timeout<T> : Filter<T>, IAmStructural where T : BaseMessage
    {
        public bool EmitTelemetryEvent => false; // emit own start/end events to include timeout duration attribute

        private readonly TimeSpan _timeout;
        private readonly Filter<T>[] _filters;

        public Timeout(TimeSpan timeout, params Filter<T>[] filters)
        {
            _timeout = timeout;
            _filters = filters;
        }

        public async Task Execute(T msg)
        {
            // Track timing and outcome for this structural filter
            var telemetryEnabled = msg.TelemetryMode != TelemetryMode.Off;
            var timingEnabled = telemetryEnabled || msg.EnableTimings;
            Stopwatch? structuralSw = timingEnabled ? Stopwatch.StartNew() : null;
            var structuralOutcome = TelemetryOutcome.Success;
            var structuralReason = string.Empty;
            var timedOut = false;

            // Build start attributes including any annotations from parent
            var startAttributes = new Dictionary<string, object>(msg.Execution.TelemetryAnnotations)
            {
                ["timeout-ms"] = (long)_timeout.TotalMilliseconds
            };
            msg.Execution.ClearTelemetryAnnotations();

            var @start = new TelemetryEvent
            {
                Actor = msg.Actor,
                Component = nameof(Timeout<T>),
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

            // Create a linked token that cancels when either the original token
            // is cancelled or the timeout elapses
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(msg.CancellationToken);
            cts.CancelAfter(_timeout);

            // Temporarily replace the message's cancellation token
            var originalToken = msg.CancellationToken;
            msg.CancellationToken = cts.Token;

            try
            {
                await FilterRunner.ExecuteFiltersAsync(_filters, msg, msg.PipelineName).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!originalToken.IsCancellationRequested)
            {
                // The timeout fired, not an external cancellation
                timedOut = true;
                structuralOutcome = TelemetryOutcome.Exception;
                structuralReason = $"Execution exceeded timeout of {_timeout.TotalMilliseconds}ms";
                throw new TimeoutException(structuralReason);
            }
            catch (Exception ex)
            {
                structuralOutcome = TelemetryOutcome.Exception;
                structuralReason = ex.Message;
                throw;
            }
            finally
            {
                // Restore the original cancellation token
                msg.CancellationToken = originalToken;

                structuralSw?.Stop();

                var endAttributes = new Dictionary<string, object>
                {
                    ["timed-out"] = timedOut
                };

                var @end = new TelemetryEvent
                {
                    Actor = msg.Actor,
                    Component = nameof(Timeout<T>),
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
                if (msg.ShouldEmitTelemetry(@end)) msg.OnTelemetry?.Invoke(@end);

                msg.Execution.ClearTelemetryAnnotations();
            }
        }
    }
}
