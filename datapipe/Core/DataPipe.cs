using DataPipe.Core.Contracts.Internal;
using DataPipe.Core.Filters;
using DataPipe.Core.Telemetry;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

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

        // A UseIf for conditional aspect registration
        public void UseIf(bool condition, Aspect<T> ifTrue, Aspect<T>? ifFalse = null)
        {
            if (condition) Use(ifTrue);
            if (!condition && ifFalse != null) Use(ifFalse);
        }

        public string Name { get; set; } = "DataPipe";
        public TelemetryMode TelemetryMode { get; set; } = TelemetryMode.Off;
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
        public void AddIf(bool condition, Filter<T> filter) => AddIf(condition, filter, null);
        public void AddIf(bool condition, Filter<T> ifTrue, Filter<T>? ifFalse) {
            if (condition) _filters.Add(ifTrue);
            if (!condition && ifFalse != null) _filters.Add(ifFalse);
        }
        
        /// Finally registers filters that must run even when an error occurs
        public void Finally(Filter<T> filter) => _finallyFilters.Add(filter);

        /// <summary>
        /// Invoke kicks off the unit of work including all registered aspects
        /// </summary>
        public async Task Invoke(T msg, CancellationToken cancellationToken = default)
        {
            // Set the pipeline name on the message
            msg.PipelineName = Name;

            // Attach the external cancellation token to the message
            msg.CancellationToken = cancellationToken;
            
            // Set the telemetry mode on the message
            msg.TelemetryMode = TelemetryMode;

            // assert the ServiceIdentity is set when telemetry is enabled and at least the Name and Environment are populated
            switch (msg.TelemetryMode)
            {
                case var x when x != TelemetryMode.Off && msg.Service == null:
                    throw new InvalidOperationException("ServiceIdentity must be set on the message when telemetry is enabled.");
                    
                case var x when x != TelemetryMode.Off && string.IsNullOrWhiteSpace(msg.Service?.Name):
                    throw new InvalidOperationException("ServiceIdentity.Name must be set when telemetry is enabled.");
                
                case var x when x != TelemetryMode.Off && string.IsNullOrWhiteSpace(msg.Service?.Environment):
                    throw new InvalidOperationException("ServiceIdentity.Environment must be set when telemetry is enabled.");
            }

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
            var pipeOutcome = TelemetryOutcome.None;
            var pipeStopReason = string.Empty;
            var psw = Stopwatch.StartNew();
            var @begin = new TelemetryEvent
            {
                PipelineName = Name,
                Component = Name,
                Service = msg.Service,
                Scope = TelemetryScope.Pipeline,
                Role = FilterRole.None,
                Phase = TelemetryPhase.Start,
                MessageId = msg.CorrelationId,
                Timestamp = DateTimeOffset.UtcNow };
            if (msg.ShouldEmitTelemetry(@begin)) msg.OnTelemetry?.Invoke(@begin);
            
            pipeOutcome = TelemetryOutcome.Success;
            msg.OnStart?.Invoke(msg);
            msg.OnLog?.Invoke($"STARTING: {Name}");
            try
            {
                if (DebugOn) msg.OnLog?.Invoke($"MESSAGE STATE: {msg.Dump()}");

                try
                {
                    foreach (var f in _filters)
                    {
                        var reason = string.Empty;
                        var fsw = Stopwatch.StartNew();
                        
                        // Check if this structural filter manages its own telemetry
                        var selfEmitting = f is IAmStructural structural && !structural.EmitTelemetryEvent;
                        var emitStart = f is not IAmStructural || (f is IAmStructural s && s.EmitTelemetryEvent);
                        
                        if (!msg.ShouldStop && emitStart)
                        {
                            var @start = new TelemetryEvent
                            {
                                PipelineName = Name,
                                Component = f.GetType().Name.Split('`')[0],
                                Service = msg.Service,
                                Scope = TelemetryScope.Filter,
                                Role = f is IAmStructural ? FilterRole.Structural : FilterRole.Business,
                                Phase = TelemetryPhase.Start,
                                MessageId = msg.CorrelationId,
                                Timestamp = DateTimeOffset.UtcNow
                            };
                            if (msg.ShouldEmitTelemetry(@start)) msg.OnTelemetry?.Invoke(@start);
                        }
                        msg.OnLog?.Invoke($"INVOKING: {f.GetType().Name.Split('`')[0]}");

                        var outcome = TelemetryOutcome.Success;
                        if (msg.ShouldStop)
                        {
                            outcome = TelemetryOutcome.Stopped;
                            reason = msg.Execution.Reason;
                            msg.OnLog?.Invoke($"STOPPED: {msg.Execution.Reason}");
                            return;
                        }

                        try
                        {
                            await f.Execute(msg);
                        }
                        catch (Exception ex)
                        {
                            pipeOutcome = outcome = TelemetryOutcome.Exception;
                            pipeStopReason = reason = ex.Message;
                            //msg.OnLog?.Invoke($"EXCEPTION-XXXXXXXXXXXXXX: {f.GetType().Name.Split('`')[0]}: {ex}");
                            throw;
                        }
                        finally
                        {
                            fsw.Stop();
                            
                            // Skip End event for self-emitting structural filters (they emit their own)
                            if (!selfEmitting)
                            {
                                var @complete = new TelemetryEvent
                                {
                                    PipelineName = Name,
                                    Component = f.GetType().Name.Split('`')[0],
                                    Service = msg.Service,
                                    Scope = TelemetryScope.Filter,
                                    Role = f is IAmStructural ? FilterRole.Structural : FilterRole.Business,
                                    Phase = TelemetryPhase.End,
                                    MessageId = msg.CorrelationId,
                                    Outcome = outcome,
                                    Reason = reason,
                                    Timestamp = DateTimeOffset.UtcNow,
                                    Duration = fsw.ElapsedMilliseconds,
                                    Attributes = msg.Execution.TelemetryAnnotations.Count != 0 ? new Dictionary<string, object>(msg.Execution.TelemetryAnnotations) : []
                                };
                                msg.Execution.TelemetryAnnotations.Clear();
                                if (msg.ShouldEmitTelemetry(@complete)) msg.OnTelemetry?.Invoke(@complete);
                            }
                            //msg.OnLog?.Invoke($"INFO: {msg.Execution.Reason}");
                            msg.OnLog?.Invoke($"COMPLETED: {f.GetType().Name.Split('`')[0]}");
                            //msg.Execution.Reset();
                        }
                    }
                }
                finally
                {
                    foreach (var f in _finallyFilters)
                    {
                        msg.OnLog?.Invoke($"FINALLY: {f.GetType().Name.Split('`')[0]}");
                        await f.Execute(msg);
                    }
                }
                msg.OnSuccess?.Invoke(msg);
            }
            finally
            {
                // If filters stopped the pipeline without throwing, mark it as stopped.
                if (pipeOutcome == TelemetryOutcome.Success && msg.Execution.IsStopped)
                {
                    pipeOutcome = TelemetryOutcome.Stopped;
                    pipeStopReason = msg.Execution.Reason;
                }

                psw.Stop();
                var @end = new TelemetryEvent
                {
                    PipelineName = Name,
                    Component = Name,
                    Service = msg.Service,
                    Scope = TelemetryScope.Pipeline,
                    Role = FilterRole.None,
                    Phase = TelemetryPhase.End,
                    Outcome = pipeOutcome,
                    Reason = pipeStopReason,
                    MessageId = msg.CorrelationId,
                    Timestamp = DateTimeOffset.UtcNow,
                    Duration = psw.ElapsedMilliseconds
                };
                if (msg.ShouldEmitTelemetry(@end)) msg.OnTelemetry?.Invoke(@end);
                msg.OnComplete?.Invoke(msg);
                msg.OnLog?.Invoke($"FINISHED: {Name} - Outcome: {pipeOutcome}");
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