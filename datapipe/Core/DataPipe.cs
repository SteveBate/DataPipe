using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

[assembly: InternalsVisibleTo("datapipe.tests")]
namespace DataPipe.Core
{
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
        public void Run(Filter<T> filter) => _filters.Add(filter);
        
        /// Conditionally add filters - based on state known at configuration time e.g. environment variables
        public void RunIf(bool condition, Filter<T> filter) => RunIf(condition, filter, new NullFilter<T>());
        public void RunIf(bool condition, Filter<T> filter, Filter<T> elseFilter) => _filters.Add(condition ? filter : elseFilter);
        
        /// Finally registers filters that must run even when an error occurs
        public void Finally(Filter<T> filter) => _finallyFilters.Add(filter);

        /// Invoke kicks of the unit of work including all registered aspects
        public async Task Invoke(T msg)
        {
            await _preFilter.Execute(msg);
            await _aspects.First().Execute(msg);
            await _postFilter.Execute(msg);
        }

        // Print out the order of execution for aspects followed by filters
        public override string ToString() => $"{_aspects.Select(a => a.GetType().Name).Aggregate((a, b) => a + " -> " + b)} -> {_filters.Select(f => f.GetType().Name).Aggregate((a,b) => a + " -> " + b)}";

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
                        if (msg.CancellationToken.Stopped)
                        {
                            msg.OnLog?.Invoke($"Processing aborted - {msg.CancellationToken.Reason}");
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
            public DefaultAspect(Func<T, Task> action)
            {
                _inner = action;
            }

            public async Task Execute(T msg)
            {
                await _inner(msg);
            }

            public Aspect<T> Next { get; set; }

            private readonly Func<T, Task> _inner;
        }

        private Filter<T> _preFilter = new NullFilter<T>();
        private Filter<T> _postFilter = new NullFilter<T>();
        private readonly List<Filter<T>> _filters = new List<Filter<T>>();
        private readonly List<Filter<T>> _finallyFilters = new List<Filter<T>>();
        private readonly List<Aspect<T>> _aspects = new List<Aspect<T>>();        
    }
}