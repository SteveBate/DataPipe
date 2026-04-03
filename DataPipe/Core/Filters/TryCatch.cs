using System;
using System.Diagnostics;
using DataPipe.Core.Contracts.Internal;
using DataPipe.Core.Telemetry;

namespace DataPipe.Core.Filters
{
    /// <summary>
    /// TryCatch executes the <c>tryFilters</c> and, if an exception is thrown,
    /// executes the <c>catchFilters</c> as a fallback instead of propagating the error.
    /// An optional predicate controls which exceptions are caught; unmatched exceptions propagate normally.
    /// </summary>
    /// <typeparam name="T">The message type. Must derive from <see cref="BaseMessage"/>.</typeparam>
    public class TryCatch<T> : Filter<T>, IAmStructural where T : BaseMessage
    {
        public bool EmitTelemetryEvent => false; // emit own start/end events to include caught/fallback attributes

        private readonly Filter<T>[] _tryFilters;
        private readonly Filter<T>[] _catchFilters;
        private readonly Func<Exception, T, bool>? _catchWhen;

        /// <summary>
        /// Creates a TryCatch filter that catches all exceptions.
        /// </summary>
        public TryCatch(Filter<T>[] tryFilters, Filter<T>[] catchFilters)
            : this(tryFilters, catchFilters, null) { }

        /// <summary>
        /// Creates a TryCatch filter with a predicate controlling which exceptions are caught.
        /// Exceptions that do not match the predicate propagate normally.
        /// </summary>
        public TryCatch(Filter<T>[] tryFilters, Filter<T>[] catchFilters, Func<Exception, T, bool>? catchWhen)
        {
            _tryFilters = tryFilters;
            _catchFilters = catchFilters;
            _catchWhen = catchWhen;
        }

        public async Task Execute(T msg)
        {
            // Track timing and outcome for this structural filter
            var structuralSw = Stopwatch.StartNew();
            var structuralOutcome = TelemetryOutcome.Success;
            var structuralReason = string.Empty;
            var caughtException = false;

            // Build start attributes including any annotations from parent
            var startAttributes = new Dictionary<string, object>(msg.Execution.TelemetryAnnotations);
            msg.Execution.ClearTelemetryAnnotations();

            var @start = new TelemetryEvent
            {
                Actor = msg.Actor,
                Component = nameof(TryCatch<T>),
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
                try
                {
                    await FilterRunner.ExecuteFiltersAsync(_tryFilters, msg, msg.PipelineName).ConfigureAwait(false);
                }
                catch (Exception ex) when (_catchWhen == null || _catchWhen(ex, msg))
                {
                    caughtException = true;

                    // Store the caught exception in transient state so catch filters can inspect it
                    msg.State.Set("TryCatch.Exception", ex);

                    await FilterRunner.ExecuteFiltersAsync(_catchFilters, msg, msg.PipelineName).ConfigureAwait(false);

                    msg.State.Remove("TryCatch.Exception");
                }
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

                var endAttributes = new Dictionary<string, object>
                {
                    ["caught-exception"] = caughtException
                };

                var @end = new TelemetryEvent
                {
                    Actor = msg.Actor,
                    Component = nameof(TryCatch<T>),
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
                    Attributes = endAttributes
                };
                if (msg.ShouldEmitTelemetry(@end)) msg.OnTelemetry?.Invoke(@end);

                msg.Execution.ClearTelemetryAnnotations();
            }
        }
    }
}
