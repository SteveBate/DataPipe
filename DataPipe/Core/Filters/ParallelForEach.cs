using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using DataPipe.Core.Contracts.Internal;
using DataPipe.Core.Telemetry;

namespace DataPipe.Core.Filters
{
    /// <summary>
    /// ParallelForEach fans out over a collection of child messages and executes child filters
    /// concurrently for each item using <see cref="Parallel.ForEachAsync"/>.
    /// 
    /// Each child message is an independent <see cref="BaseMessage"/> instance with its own
    /// lifecycle, state, and execution context. Infrastructure properties (lifecycle callbacks,
    /// cancellation token, telemetry mode, service identity, pipeline name) are automatically
    /// copied from the parent message to each child before execution.
    /// 
    /// The user-supplied mapper delegate sets domain-specific properties (connection strings,
    /// flags, etc.) on each child.
    /// 
    /// All child filters must be stateless and thread-safe — this is already a core DataPipe
    /// requirement but is critical here since multiple branches execute concurrently.
    /// 
    /// Use <see cref="TryCatch{T}"/> inside the branch filters for per-branch error isolation.
    /// Without it, a single branch failure cancels all remaining branches (standard
    /// <see cref="Parallel.ForEachAsync"/> behavior).
    /// </summary>
    /// <typeparam name="TParent">The parent message type (the pipeline's message).</typeparam>
    /// <typeparam name="TChild">The child message type. Must derive from <see cref="BaseMessage"/>.</typeparam>
    public class ParallelForEach<TParent, TChild> : Filter<TParent>, IAmStructural
        where TParent : BaseMessage
        where TChild : BaseMessage
    {
        public bool EmitTelemetryEvent => false; // emit own start/end with branch count and parallelism

        private readonly Func<TParent, IEnumerable<TChild>> _selector;
        private readonly Action<TParent, TChild>? _mapper;
        private readonly int _maxDegreeOfParallelism;
        private readonly Filter<TChild>[] _filters;

        /// <summary>
        /// Creates a ParallelForEach filter with default parallelism (unlimited).
        /// </summary>
        /// <param name="selector">Extracts the collection of child messages from the parent.</param>
        /// <param name="filters">Filters to execute concurrently for each child message.</param>
        public ParallelForEach(
            Func<TParent, IEnumerable<TChild>> selector,
            params Filter<TChild>[] filters)
            : this(selector, null, -1, filters) { }

        /// <summary>
        /// Creates a ParallelForEach filter with a mapper and default parallelism.
        /// </summary>
        /// <param name="selector">Extracts the collection of child messages from the parent.</param>
        /// <param name="mapper">Sets domain-specific properties on each child from the parent.</param>
        /// <param name="filters">Filters to execute concurrently for each child message.</param>
        public ParallelForEach(
            Func<TParent, IEnumerable<TChild>> selector,
            Action<TParent, TChild> mapper,
            params Filter<TChild>[] filters)
            : this(selector, mapper, -1, filters) { }

        /// <summary>
        /// Creates a ParallelForEach filter with a mapper and explicit parallelism control.
        /// </summary>
        /// <param name="selector">Extracts the collection of child messages from the parent.</param>
        /// <param name="mapper">Sets domain-specific properties on each child from the parent. Can be null.</param>
        /// <param name="maxDegreeOfParallelism">Maximum concurrent branches. Use -1 for unlimited.</param>
        /// <param name="filters">Filters to execute concurrently for each child message.</param>
        public ParallelForEach(
            Func<TParent, IEnumerable<TChild>> selector,
            Action<TParent, TChild>? mapper,
            int maxDegreeOfParallelism,
            params Filter<TChild>[] filters)
        {
            ArgumentNullException.ThrowIfNull(selector);
            ArgumentNullException.ThrowIfNull(filters);
            _selector = selector;
            _mapper = mapper;
            _maxDegreeOfParallelism = maxDegreeOfParallelism;
            _filters = filters;
        }

        public async Task Execute(TParent msg)
        {
            var telemetryEnabled = msg.TelemetryMode != TelemetryMode.Off;
            var timingEnabled = telemetryEnabled || msg.EnableTimings;
            Stopwatch? structuralSw = timingEnabled ? Stopwatch.StartNew() : null;
            var structuralOutcome = TelemetryOutcome.Success;
            var structuralReason = string.Empty;
            var branchCount = 0;
            var telemetryStarted = false;

            try
            {
                var children = _selector(msg);
                if (children == null)
                {
                    msg.OnLog?.Invoke($"{nameof(ParallelForEach<TParent, TChild>)}: selector returned null. Skipping.");
                    return;
                }

                // Materialise to count branches for telemetry
                var childList = children as ICollection<TChild> ?? new List<TChild>(children);
                branchCount = childList.Count;

                // Build start attributes
                var startAttributes = new Dictionary<string, object>(msg.Execution.TelemetryAnnotations)
                {
                    ["branches"] = branchCount,
                    ["max-parallelism"] = _maxDegreeOfParallelism
                };
                msg.Execution.ClearTelemetryAnnotations();

                var @start = new TelemetryEvent
                {
                    Actor = msg.Actor,
                    Component = nameof(ParallelForEach<TParent, TChild>),
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
                telemetryStarted = true;

                var options = new ParallelOptions
                {
                    MaxDegreeOfParallelism = _maxDegreeOfParallelism,
                    CancellationToken = msg.CancellationToken
                };

                await Parallel.ForEachAsync(childList, options, async (child, ct) =>
                {
                    // Wire infrastructure from parent to child
                    child.CancellationToken = ct;
                    child.PipelineName = msg.PipelineName;
                    child.TelemetryMode = msg.TelemetryMode;
                    child.EnableTimings = msg.EnableTimings;
                    child.Service = msg.Service;
                    child.Actor = msg.Actor;
                    child.OnError = msg.OnError;
                    var parentOnLog = msg.OnLog;
                    child.OnLog = line =>
                    {
                        if (parentOnLog == null)
                        {
                            return;
                        }

                        using (DataPipe.Core.LogContext.PushTag(child.Tag))
                        {
                            parentOnLog.Invoke(line);
                        }
                    };
                    child.OnStart = msg.OnStart;
                    child.OnSuccess = msg.OnSuccess;
                    child.OnComplete = msg.OnComplete;
                    child.OnTelemetry = msg.OnTelemetry;

                    // Let the user set domain-specific properties
                    _mapper?.Invoke(msg, child);

                    // Execute the filter chain for this branch
                    await FilterRunner.ExecuteFiltersAsync(_filters, child, msg.PipelineName).ConfigureAwait(false);
                }).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                structuralOutcome = TelemetryOutcome.Exception;
                structuralReason = ex.Message;
                throw;
            }
            finally
            {
                structuralSw?.Stop();

                if (telemetryStarted)
                {
                    var @end = new TelemetryEvent
                    {
                        Actor = msg.Actor,
                        Component = nameof(ParallelForEach<TParent, TChild>),
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
                        Attributes = new Dictionary<string, object>
                        {
                            ["branches"] = branchCount
                        }
                    };
                    if (msg.ShouldEmitTelemetry(@end)) msg.OnTelemetry?.Invoke(@end);

                    msg.Execution.ClearTelemetryAnnotations();
                }
            }
        }
    }
}
