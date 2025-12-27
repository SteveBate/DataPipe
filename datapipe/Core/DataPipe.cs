using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using DataPipe.Core.Filters;

[assembly: InternalsVisibleTo("datapipe.tests")]
namespace DataPipe.Core
{
    /// <summary>
    /// DataPipe represents a fully asynchronous, stateless, and thread-safe pipeline for processing messages of type T.
    /// 
    /// This class provides a flexible, composable architecture for building message processing pipelines with support for:
    /// - Pre-processing filters that execute before the main pipeline
    /// - Multiple sequential filters that form the core pipeline logic
    /// - Post-processing filters that execute after the main pipeline completes
    /// - Finally filters that execute even when errors occur, similar to finally blocks
    /// - Aspects that provide cross-cutting concerns (logging, metrics, error handling, etc.)
    /// - Conditional filter registration based on configuration-time state
    /// - Message-level control flow with stop conditions
    /// - Debug logging capabilities for troubleshooting pipeline execution
    /// 
    /// The pipeline maintains execution order as: Pre -> Aspects -> Filters -> Finally -> Post
    /// Each filter receives the message and can modify it before passing to the next stage.
    /// Aspects wrap the core filter execution and can intercept, log, or add handle exceptions.
    /// 
    /// The message being processed (of type T) carries state throughout the pipeline, including:
    /// - Execution metadata and control signals (stop conditions, cancellation tokens)
    /// - Callbacks for lifecycle events (start, success, error, completion)
    /// - Custom application data relevant to the specific message type
    /// 
    /// This implementation is thread-safe for concurrent invocations with different message instances.
    /// </summary>
    /// <typeparam name="T">The type of message the pipeline operates on. Must derive from BaseMessage.</typeparam>
    public class DataPipe<T> where T : BaseMessage
    {
        /// <summary>
        /// Constructor initializes the DataPipe creating a default aspect as the context in which registered filters are executed
        /// </summary>
        public DataPipe() => _aspects.Add(new DefaultAspect(this.Execute));

        /// Supply cross cutting concerns around the actual unit of work.
        public void Use(Aspect<T> aspect)
        {
            _aspects.Insert(_aspects.Count - 1, aspect);
            _aspects.Aggregate((a, b) => a.Next = b);
        }

        public bool DebugOn { get; set; }

        /// Register a filter to run before everything else - useful for set up  such as copying files in to a working directory or reading config file values to set on the message before the main pipe runs
        public void Pre(Filter<T> filter) => _preFilter = filter;

        /// Register a filter to run after everything else has completed - useful for clean up work like removing files from a directory, etc
        public void Post(Filter<T> filter) => _postFilter = filter;

        /// Register the individual steps that make up the DataPipe
        public void Add(Filter<T> filter) => _filters.Add(filter);

        // Register multiple filters at once
        public void Add(params Filter<T>[] filters) => _filters.AddRange(filters);

        // Syntacic sugar to register a filter from a lambda
        public void Add(Func<T, Task> action)
        {
            _filters.Add(new LambdaFilter<T>(action));
        }


        /// Conditionally add filters - based on state known at configuration time e.g. environment variables
        public void AddIf(bool condition, Filter<T> filter) => AddIf(condition, filter, new NullFilter<T>());
        public void AddIf(bool condition, Filter<T> ifTrue, Filter<T> ifFalse) => _filters.Add(condition ? ifTrue : ifFalse);
        
        /// Finally registers filters that must run even when an error occurs
        public void Finally(Filter<T> filter) => _finallyFilters.Add(filter);

        /// <summary>
        /// Invoke kicks off the unit of work including all registered aspects
        /// </summary>
        public async Task Invoke(T msg, CancellationToken cancellationToken = default)
        {
            // Attach the external cancellation token to the message
            msg.CancellationToken = cancellationToken;

            await _preFilter.Execute(msg);
            await _aspects.First().Execute(msg);
            await _postFilter.Execute(msg);
        }

        // Print out the order of execution for aspects followed by filters
        public override string ToString() => 
            $"{_aspects.Select(a => a.GetType().Name).Aggregate((a, b) => a + " -> " + b)} -> " +
            $"{_filters.Select(f => f.GetType().Name).Aggregate((a,b) => a + " -> " + b)}";

        // Execute iterates the pipeline pushing the message through each registered filter
        private async Task Execute(T msg)
        {
            msg.OnStart?.Invoke(msg);
            try
            {
                if (DebugOn) msg.OnLog?.Invoke($"MESSAGE STATE: {msg.Dump()}");

                try
                {
                    foreach (var f in _filters)
                    {
                        if (msg.ShouldStop)
                        {
                            msg.OnLog?.Invoke($"Pipeline stopped: {msg.Execution.Reason}");
                            return;
                        }

                        if (DebugOn) msg.OnLog?.Invoke($"CURRENT FILTER: {f.GetType().Name}");
                        await f.Execute(msg);
                    }
                }
                finally
                {
                    foreach (var f in _finallyFilters)
                    {
                        await f.Execute(msg);
                    }
                }
                msg.OnSuccess?.Invoke(msg);
            }
            finally
            {
                msg.OnComplete?.Invoke(msg);
            }
        }

        /// DefaultAspect is the context that the DataPipe's unit of work runs under
        private class DefaultAspect : Aspect<T>, Filter<T>
        {
            private readonly Func<T, Task> _inner;
            public DefaultAspect(Func<T, Task> action) => _inner = action ?? throw new ArgumentNullException(nameof(action));
            public Aspect<T> Next { get; set; } = default!;
            public async Task Execute(T msg) => await _inner(msg);
        }

        private Filter<T> _preFilter = new NullFilter<T>();
        private Filter<T> _postFilter = new NullFilter<T>();
        private readonly List<Filter<T>> _filters = new List<Filter<T>>();
        private readonly List<Filter<T>> _finallyFilters = new List<Filter<T>>();
        private readonly List<Aspect<T>> _aspects = new List<Aspect<T>>();        
    }
}