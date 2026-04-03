using System;
using System.Diagnostics;
using DataPipe.Core.Contracts.Internal;
using DataPipe.Core.Telemetry;

namespace DataPipe.Core.Filters
{
    /// <summary>
    /// IfTrueElse evaluates a predicate and executes one of two filter branches.
    /// If the predicate returns true, the <c>thenFilters</c> are executed.
    /// If false, the <c>elseFilters</c> are executed.
    /// </summary>
    /// <typeparam name="T">The message type. Must derive from <see cref="BaseMessage"/>.</typeparam>
    public class IfTrueElse<T> : Filter<T>, IAmStructural where T : BaseMessage
    {
        public bool EmitTelemetryEvent => false; // emit own start/end events to include decision attribute

        private readonly Func<T, bool> _callback;
        private readonly Filter<T>[] _thenFilters;
        private readonly Filter<T>[] _elseFilters;

        public IfTrueElse(Func<T, bool> callback, Filter<T>[] thenFilters, Filter<T>[] elseFilters)
        {
            _callback = callback;
            _thenFilters = thenFilters;
            _elseFilters = elseFilters;
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
                ["condition"] = result,
                ["branch"] = result ? "then" : "else"
            };
            msg.Execution.ClearTelemetryAnnotations();

            var @start = new TelemetryEvent
            {
                Actor = msg.Actor,
                Component = nameof(IfTrueElse<T>),
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
                var filters = result ? _thenFilters : _elseFilters;
                await FilterRunner.ExecuteFiltersAsync(filters, msg, msg.PipelineName).ConfigureAwait(false);
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

                var @end = new TelemetryEvent
                {
                    Actor = msg.Actor,
                    Component = nameof(IfTrueElse<T>),
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

                msg.Execution.ClearTelemetryAnnotations();
            }
        }
    }
}
