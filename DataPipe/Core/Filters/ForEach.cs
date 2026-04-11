using System;
using System.Collections.Generic;
using DataPipe.Core.Contracts.Internal;

namespace DataPipe.Core.Filters
{
    /// <summary>
    /// ForEach executes child filters sequentially for each child message selected from a parent message.
    /// This shares the same selector/mapper/filter shape as ParallelForEach so a demo can switch between
    /// sequential and parallel fan-out by changing only the filter type.
    /// </summary>
    /// <typeparam name="TParent">The parent message type (the pipeline's message).</typeparam>
    /// <typeparam name="TChild">The child message type. Must derive from <see cref="BaseMessage"/>.</typeparam>
    public class ForEach<TParent, TChild> : Filter<TParent>, IAmStructural
        where TParent : BaseMessage
        where TChild : BaseMessage
    {
        public bool EmitTelemetryEvent => true;

        private readonly Func<TParent, IEnumerable<TChild>> _selector;
        private readonly Action<TParent, TChild>? _mapper;
        private readonly Filter<TChild>[] _filters;

        /// <summary>
        /// Creates a sequential ForEach with no mapper.
        /// </summary>
        /// <param name="selector">Extracts the collection of child messages from the parent.</param>
        /// <param name="filters">Filters to execute sequentially for each child message.</param>
        public ForEach(
            Func<TParent, IEnumerable<TChild>> selector,
            params Filter<TChild>[] filters)
            : this(selector, null, filters)
        {
        }

        /// <summary>
        /// Creates a sequential ForEach with an optional mapper.
        /// </summary>
        /// <param name="selector">Extracts the collection of child messages from the parent.</param>
        /// <param name="mapper">Sets domain-specific properties on each child from the parent. Can be null.</param>
        /// <param name="filters">Filters to execute sequentially for each child message.</param>
        public ForEach(
            Func<TParent, IEnumerable<TChild>> selector,
            Action<TParent, TChild>? mapper,
            params Filter<TChild>[] filters)
        {
            ArgumentNullException.ThrowIfNull(selector);
            ArgumentNullException.ThrowIfNull(filters);

            _selector = selector;
            _mapper = mapper;
            _filters = filters;
        }

        /// <summary>
        /// Executes child filters sequentially for each selected child message.
        /// </summary>
        /// <param name="msg">The parent pipeline message.</param>
        public async Task Execute(TParent msg)
        {
            var children = _selector(msg);
            if (children == null)
            {
                msg.OnLog?.Invoke($"{nameof(ForEach<TParent, TChild>)}: selector returned null. Skipping.");
                return;
            }

            foreach (var child in children)
            {
                // Respect stop signals in the pipeline (including CancellationToken)
                if (msg.ShouldStop) break;

                // Wire infrastructure from parent to child.
                child.CancellationToken = msg.CancellationToken;
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

                // Let the user set domain-specific properties.
                _mapper?.Invoke(msg, child);

                // Execute all filters in sequence for this child.
                await FilterRunner.ExecuteFiltersAsync(_filters, child, msg.PipelineName).ConfigureAwait(false);

                // Preserve historical ForEach behavior: stop parent pipeline if the current item stops.
                if (child.ShouldStop)
                {
                    msg.Execution.Stop(child.Execution.Reason);
                    break;
                }
            }
        }
    }
}
