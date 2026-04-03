using DataPipe.Core.Contracts.Internal;

namespace DataPipe.Core.Filters
{
    /// <summary>
    /// DelayExecution introduces an intentional delay before executing child filters.
    /// </summary>
    /// <typeparam name="T">The message type. Must derive from <see cref="BaseMessage"/>.</typeparam>
    public sealed class DelayExecution<T> : Filter<T>, IAmStructural where T : BaseMessage
    {
        public bool EmitTelemetryEvent => true;
        private readonly TimeSpan _delay;
        private readonly Filter<T>[] _filters;

        public DelayExecution(TimeSpan delay, params Filter<T>[] filters)
        {
            _delay = delay;
            _filters = filters;
        }

        public async Task Execute(T msg)
        {
            await Task.Delay(_delay, msg.CancellationToken);
            await FilterRunner.ExecuteFiltersAsync(_filters, msg, msg.PipelineName).ConfigureAwait(false);
        }
    }
}