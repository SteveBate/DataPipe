using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace DataPipe.Core.Filters
{
    /// <summary>
    /// ForEach<TMessage, TItem> allows you to iterate over an enumerable in the message
    /// and execute one or more filters for each element.
    /// This is fully async and respects pipeline stop conditions.
    /// 
    /// Example Usages:
    /// 
    /// Simple string concatenation:
    /// // msg.Words = new[] { "this", "is", "a", "test" };
    /// pipe.Run(new ForEach<TestMessage, string>(
    ///     msg => msg.Words,
    ///     (msg, word) => msg.CurrentWord = word,
    ///     new ConcatenatingFilter()
    /// ));
    /// 
    /// Execute multiple filters for each item:
    /// pipe.Run(new ForEach<TestMessage, string>(
    ///     msg => msg.Words,
    ///     (msg, word) => msg.CurrentWord = word,
    ///     new ConcatenatingFilter(),
    ///     new LoggingFilter()
    /// ));
    /// 
    /// Stop the pipeline mid-loop:
    /// pipe.Run(new ForEach<TestMessage, string>(
    ///     msg => msg.Words,
    ///     (msg, word) =>
    ///     {
    ///         msg.CurrentWord = word;
    ///         if(word == "stop") msg.Execution.Stop("Hit stop word");
    ///     },
    ///     new ConcatenatingFilter()
    /// ));
    /// </summary>
    public class ForEach<TMessage, TItem> : Filter<TMessage>
        where TMessage : BaseMessage
    {
        private readonly Func<TMessage, IEnumerable<TItem>> _enumerableSelector;
        private readonly Action<TMessage, TItem> _itemSetter;
        private readonly Filter<TMessage>[] _filters;

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="enumerableSelector">Function to select the items to iterate over</param>
        /// <param name="itemSetter">Action to set the current item on the message</param>
        /// <param name="filters">One or more filters to execute per item</param>
        public ForEach(
            Func<TMessage, IEnumerable<TItem>> enumerableSelector,
            Action<TMessage, TItem> itemSetter,
            params Filter<TMessage>[] filters)
        {
            _enumerableSelector = enumerableSelector;
            _itemSetter = itemSetter;
            _filters = filters;
        }

        /// <summary>
        /// Execute iterates asynchronously over the enumerable and executes filters for each item
        /// </summary>
        /// <param name="msg">The pipeline message</param>
        public async Task Execute(TMessage msg)
        {
            var items = _enumerableSelector(msg);
            if (items == null) return;

            foreach (var item in items)
            {
                // Respect stop signals in the pipeline
                if (msg.Execution.IsStopped) break;

                // Set current item into the message
                _itemSetter(msg, item);

                // Execute all filters in sequence for this item
                foreach (var f in _filters)
                {
                    if (msg.Execution.IsStopped) break;
                    await f.Execute(msg);
                }
            }
        }
    }
}
