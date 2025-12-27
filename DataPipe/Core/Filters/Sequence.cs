using DataPipe.Core;
using System.Threading.Tasks;

namespace DataPipe.Core.Filters
{
    /// <summary>
    /// Sequence<T> allows you to execute multiple filters sequentially in a block.
    /// Provides a way to group filters that belong together.
    /// </summary>
    /// <typeparam name="T">The type of message being processed.</typeparam>
    /// <param name="filters">The filters to execute.</param>
    public class Sequence<T>(params Filter<T>[] filters) : Filter<T> where T : BaseMessage
    {
        public async Task Execute(T msg)
        {
            foreach (var f in filters)
            {
                if (msg.Execution.IsStopped) break;

                await f.Execute(msg);
            }

        }
    }
}
