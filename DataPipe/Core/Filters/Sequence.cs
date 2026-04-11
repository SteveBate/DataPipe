using DataPipe.Core;
using DataPipe.Core.Contracts.Internal;

namespace DataPipe.Core.Filters
{
    /// <summary>
    /// Sequence<T> allows you to execute multiple filters sequentially in a block.
    /// Provides a way to group filters that belong together.
    /// </summary>
    /// <typeparam name="T">The type of message being processed.</typeparam>
    /// <param name="filters">The filters to execute.</param>
    public sealed class Sequence<T>(params Filter<T>[] filters) : Filter<T>, IAmStructural where T : BaseMessage
    {
        public bool EmitTelemetryEvent => true;

        public async Task Execute(T msg)
        {
            await FilterRunner.ExecuteFiltersAsync(filters, msg, msg.PipelineName).ConfigureAwait(false);
        }
    }
}
