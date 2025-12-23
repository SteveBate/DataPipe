using DataPipe.Core;
using System;
using System.Threading.Tasks;

namespace DataPipe.Core.Filters
{
    /// <summary>
    /// A filter that implements a policy pattern by dynamically selecting and executing
    /// one or more filters based on the message content. This allows for conditional filter execution
    /// where the specific filter to run is determined at runtime.
    /// </summary>
    /// <typeparam name="T">The type of message this policy operates on, must derive from BaseMessage.</typeparam>
    /// <remarks>
    /// The Policy filter acts as a selector that uses a provided delegate function to determine
    /// which filter should be executed for a given message. If no filter is selected (null is returned),
    /// or if the message execution has been stopped, the policy will short-circuit and return without
    /// executing any filter.
    /// 
    /// This pattern is useful for implementing:
    /// - Conditional routing based on message properties
    /// - Dynamic filter selection logic
    /// - Chainable policy decisions in a message processing pipeline
    /// 
    /// Example usage:
    /// <code>
    /// var policy = new Policy&lt;MyMessage&gt;(msg => 
    ///     msg.Priority == High ? new HighPriorityFilter() : new NormalPriorityFilter());
    /// </code>
    /// </remarks>
    public class Policy<T> : Filter<T> where T : BaseMessage
    {
        private readonly Func<T, Filter<T>> _selector;

        public Policy(Func<T, Filter<T>> selector)
        {
            _selector = selector;
        }

        public async Task Execute(T msg)
        {
            var filter = _selector(msg);

            if (filter == null || msg.Execution.IsStopped)
                return;

            await filter.Execute(msg);
        }
    }
}
