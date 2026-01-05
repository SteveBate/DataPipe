using System;
using System.Threading.Tasks;
using DataPipe.Core.Contracts.Internal;

namespace DataPipe.Core.Filters
{
    /// <summary>
    /// A filter implementation that executes a custom asynchronous delegate function.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>LambdaFilter&lt;T&gt;</c> provides a lightweight, flexible way to define filter logic
    /// using lambda expressions or delegate functions without requiring explicit class inheritance.
    /// This is useful for simple, one-off filtering operations where creating a dedicated filter class
    /// would be unnecessary overhead.
    /// </para>
    public class LambdaFilter<T> : Filter<T>, IAmStructural where T : BaseMessage
    {
        public bool EmitTelemetryEvent => true;
        /// <summary>
        /// The asynchronous delegate function to execute during filtering.
        /// </summary>
        private readonly Func<T, Task> _func;

        /// <summary>
        /// Initializes a new instance of the <see cref="LambdaFilter{T}"/> class.
        /// </summary>
        /// <param name="func">
        /// The asynchronous delegate function to execute when the filter is invoked.
        /// Cannot be null.
        /// </param>
        public LambdaFilter(Func<T, Task> func) => _func = func;

        /// <summary>
        /// Executes the filter's delegate function asynchronously with the provided message.
        /// </summary>
        /// <param name="msg">The message to filter.</param>
        /// <returns>A task representing the asynchronous filter operation.</returns>
        public Task Execute(T msg) => _func(msg);
    }

}
