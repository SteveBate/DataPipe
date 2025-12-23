using System;
using System.Threading.Tasks;

namespace DataPipe.Core.Filters
{
    /// <summary>
    /// A filter that repeatedly executes a sequence of child filters until a specified condition is met.
    /// </summary>
    /// <typeparam name="T">The type of message being processed. Must derive from <see cref="BaseMessage"/>.</typeparam>
    /// <remarks>
    /// <para>
    /// The <see cref="RepeatUntil{T}"/> filter provides a loop-based execution model where child filters
    /// are executed repeatedly until either:
    /// <list type="bullet">
    /// <item><description>The callback condition returns true (indicating the repeat should stop)</description></item>
    /// <item><description>The message execution is stopped via <see cref="BaseMessage.Execution.IsStopped"/></description></item>
    /// </list>
    /// </para>
    /// <para>
    /// Execution flow:
    /// <list type="number">
    /// <item><description>Evaluates the callback condition with the current message</description></item>
    /// <item><description>If the condition is false, enters the repeat loop</description></item>
    /// <item><description>Iterates through all child filters in order, executing each one</description></item>
    /// <item><description>If execution is stopped during iteration, breaks out of the filter loop</description></item>
    /// <item><description>Continues looping through all filters until execution is stopped</description></item>
    /// <item><description>Resets the execution state before returning</description></item>
    /// </list>
    /// </para>
    /// <para>
    /// If the callback condition returns true on the initial check, no filters are executed.
    /// </para>
    /// </remarks>
    public class RepeatUntil<T> : Filter<T> where T : BaseMessage
    {
        private readonly Filter<T>[] _filters;
        private Func<T, bool> _callback;

        public RepeatUntil(Func<T, bool> callback, params Filter<T>[] filters)
        {
            _filters = filters;
            _callback = callback;
        }

        public async Task Execute(T msg)
        {
            if (!_callback(msg))
            {
                do
                {
                    foreach (Filter<T> filter in _filters)
                    {
                        if (msg.Execution.IsStopped)
                        {
                            break;
                        }

                        await filter.Execute(msg);
                    }
                }
                while (!msg.Execution.IsStopped);

                msg.Execution.Reset();
            }
        }
    }
}