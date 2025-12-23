using System.Threading.Tasks;

namespace DataPipe.Core.Filters
{

    /// <summary>
    /// A filter that repeatedly executes a sequence of child filters until an execution stop signal is received.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The <see cref="Repeat{T}"/> filter implements a loop-based execution pattern that continuously
    /// processes a message through a chain of child filters. The repetition continues until the message's
    /// <see cref="BaseMessage.Execution"/> state is marked as stopped via <see cref="IsStopped"/>.
    /// </para>
    /// <para>
    /// During each iteration, all child filters are executed sequentially in the order they were provided.
    /// If any child filter sets <see cref="BaseMessage.Execution.IsStopped"/> to true, the current iteration
    /// is interrupted and the loop condition is checked.
    /// </para>
    /// <para>
    /// After the loop terminates, the execution state is reset via <see cref="BaseMessage.Execution.Reset()"/>
    /// to ensure that filters executed after this <see cref="Repeat{T}"/> instance are not affected by the
    /// stopped state.
    /// </para>
    /// <para>
    /// This filter is useful for  any workflow that requires repeated processing of a message 
    /// until a specific condition is met.
    /// </para>
    /// </remarks>
    /// <typeparam name="T">The message type, must derive from <see cref="BaseMessage"/>.</typeparam>
    public class Repeat<T> : Filter<T> where T : BaseMessage
    {
        private readonly Filter<T>[] _filters;

        public Repeat(params Filter<T>[] filters)
        {
            _filters = filters;
        }

        public async Task Execute(T msg)
        {
            do
            {
                foreach (var f in _filters)
                {
                    if (msg.Execution.IsStopped) break;

                    await f.Execute(msg);
                }

            } while (!msg.Execution.IsStopped);

            msg.Execution.Reset(); // for any filters that come after
        }
    }
}