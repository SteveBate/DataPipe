using System;
using System.Threading.Tasks;

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
    /// <para>
    /// The filter accepts a <see cref="Func{T, Task}"/> delegate that will be invoked during
    /// execution with the input message. The delegate is responsible for all filter processing,
    /// including validation, transformation, or side effects.
    /// </para>
    /// <para>
    /// Thread Safety: The thread safety of instances depends on the implementation of the provided delegate.
    /// </para>
    /// </remarks>
    /// <typeparam name="T">The message type to filter. Must inherit from <see cref="BaseMessage"/>.</typeparam>
    public class LambdaFilter<T> : Filter<T> where T : BaseMessage
    {
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
