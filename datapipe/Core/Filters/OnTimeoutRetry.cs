using System;
using System.Diagnostics;
using System.Runtime.Serialization;
using System.Threading.Tasks;
using DataPipe.Core.Contracts;
using DataPipe.Core.Contracts.Internal;
using DataPipe.Core.Telemetry;

namespace DataPipe.Core.Filters
{
    /// <summary>
    /// OnTimeoutRetry provides a fully asynchronous, stateless retry mechanism
    /// for any set of filters within a DataPipe pipeline. 
    /// It allows you to specify:
    ///   - Maximum retry attempts
    ///   - Which exceptions should trigger a retry
    ///   - Custom delay strategies between retries (linear, exponential, etc.)
    /// 
    /// Example Usages:
    /// 
    /// Default retry:
    ///    pipe.Add(new OnTimeoutRetry<TestMessage>(3, new SomeFilter()));
    ///    - Retries up to 3 times
    ///    - Linear 2s, 4s, 6s delay
    ///    - Retries on TimeoutException or transient DB/network errors
    /// 
    /// Retry only on HttpRequestException:
    ///    pipe.Add(new OnTimeoutRetry<TestMessage>(
    ///        5, new SomeFilter(),
    ///        retryWhen: (ex, msg) => ex is HttpRequestException));
    /// 
    /// Exponential backoff delay strategy:
    ///    pipe.Add(new OnTimeoutRetry<TestMessage>(
    ///        5, new SomeFilter(),
    ///        customDelay: (attempt, msg) => TimeSpan.FromSeconds(Math.Pow(2, attempt))));
    /// 
    /// Fully custom example:
    ///    pipe.Add(new OnTimeoutRetry<TestMessage>(
    ///        3, new SomeFilter(),
    ///        retryWhen: (ex, msg) => ex is TimeoutException || ex.Message.Contains("busy"),
    ///        customDelay: (attempt, msg) => TimeSpan.FromSeconds(attempt * 1.5)));
    /// </summary>
    public class OnTimeoutRetry<T> : Filter<T>, IAmStructural where T : BaseMessage, IAmRetryable
    {
        public bool EmitTelemetryEvent => false; // emit own start event rather than parent so we can capture max attempts

        private readonly Filter<T>[] _filters;
        private readonly int _maxRetries;

        // Optional function to determine if an exception should trigger a retry
        private readonly Func<Exception, T, bool> _retryWhen;

        // Optional function to determine the delay before retrying (sliding, exponential, etc.)
        private readonly Func<int, T, TimeSpan> _defaultDelay;

        // Track retry information for telemetry
        private string? _lastRetryReason;

        public OnTimeoutRetry(
            int maxRetries,
            params Filter<T>[] filters
        ) : this(maxRetries, null, null, filters) { } // preserves old behavior

        public OnTimeoutRetry(
            int maxRetries,
            Func<Exception, T, bool>? retryWhen = null,
            params Filter<T>[] filters
        ) : this(maxRetries, retryWhen, null, filters) { } // preserves old behavior

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="maxRetries">Maximum number of retry attempts</param>
        /// <param name="filters">Filters to execute under retry logic</param>
        /// <param name="retryWhen">Optional function to define which exceptions are retryable</param>
        /// <param name="customDelay">Optional function to define delay between retries</param>
        public OnTimeoutRetry(
            int maxRetries,
            Func<Exception, T, bool>? retryWhen = null,
            Func<int, T, TimeSpan>? customDelay = null,
            params Filter<T>[] filters)
        {
            _filters = filters;
            _maxRetries = maxRetries;

            // Default retry logic for transient errors if none provided
            _retryWhen = retryWhen ?? DefaultRetryWhen;

            // Default linear sliding delay (2s * attempt number)
            _defaultDelay = customDelay ?? DefaultDelay;
        }

        /// <summary>
        /// Executes the filters, retrying when necessary according to the defined conditions
        /// </summary>
        /// <param name="msg">Message flowing through the pipeline</param>
        public async Task Execute(T msg)
        {
            // Track max retries and current attempt in the message for debugging
            msg.MaxRetries = _maxRetries;
            msg.Attempt = 1;
            _lastRetryReason = null;

            // Track timing and outcome for this structural filter
            var structuralSw = Stopwatch.StartNew();
            var structuralOutcome = TelemetryOutcome.Success;
            var structuralReason = string.Empty;

            // Build start attributes including any annotations from parent
            var startAttributes = new Dictionary<string, object>(msg.Execution.TelemetryAnnotations)
            {
                { "max-attempts", _maxRetries+1 }
            };
            
            // Clear annotations after consuming them for Start event
            msg.Execution.TelemetryAnnotations.Clear();

            // Emit start event with max-attempts attribute
            var @retryStart = new TelemetryEvent
            {
                Component = nameof(OnTimeoutRetry<T>),
                PipelineName = msg.PipelineName,
                Service = msg.Service,
                Scope = TelemetryScope.Filter,
                Role = FilterRole.Structural,
                Phase = TelemetryPhase.Start,
                MessageId = msg.CorrelationId,
                Timestamp = DateTimeOffset.UtcNow,
                Attributes = startAttributes
            };
            if (msg.ShouldEmitTelemetry(@retryStart)) msg.OnTelemetry?.Invoke(@retryStart);

            try
            {
                // Loop until the message is completed or marked as stopped
                while (!msg.Execution.IsStopped)
                {
                    try
                    {
                        // Run each filter sequentially
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
                                    Component = f.GetType().Name.Split('`')[0],
                                    PipelineName = msg.PipelineName,
                                    Service = msg.Service,
                                    Scope = TelemetryScope.Filter,
                                    Role = f is IAmStructural ? FilterRole.Structural : FilterRole.Business,
                                    Phase = TelemetryPhase.Start,
                                    MessageId = msg.CorrelationId,
                                    Timestamp = DateTimeOffset.UtcNow,
                                    Attributes = f is IAmStructural ? new Dictionary<string, object>(msg.Execution.TelemetryAnnotations) : []
                                };
                                if (msg.ShouldEmitTelemetry(@start)) msg.OnTelemetry?.Invoke(@start);
                            }

                            if (!msg.ShouldStop)
                            {
                                msg.OnLog?.Invoke($"INVOKING: {f.GetType().Name.Split('`')[0]}");
                            }

                            var outcome = TelemetryOutcome.Success;
                            if (msg.ShouldStop)
                            {
                                outcome = TelemetryOutcome.Stopped;
                                reason = msg.Execution.Reason;
                                return;
                            }

                            try
                            {
                                await f.Execute(msg);
                            }
                            catch (Exception ex)
                            {
                                outcome = TelemetryOutcome.Exception;
                                reason = ex.Message;
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
                                        Component = f.GetType().Name.Split('`')[0],
                                        PipelineName = msg.PipelineName,
                                        Service = msg.Service,
                                        Scope = TelemetryScope.Filter,
                                        Role = f is IAmStructural ? FilterRole.Structural : FilterRole.Business,
                                        Phase = TelemetryPhase.End,
                                        MessageId = msg.CorrelationId,
                                        Outcome = msg.ShouldStop ? TelemetryOutcome.Stopped : outcome,
                                        Reason = msg.ShouldStop ? msg.Execution.Reason : reason,
                                        Timestamp = DateTimeOffset.UtcNow,
                                        Duration = fsw.ElapsedMilliseconds,
                                        Attributes = msg.Execution.TelemetryAnnotations.Count != 0 ? new Dictionary<string, object>(msg.Execution.TelemetryAnnotations) : []
                                    };
                                    msg.Execution.TelemetryAnnotations.Clear();
                                    if (msg.ShouldEmitTelemetry(@complete)) msg.OnTelemetry?.Invoke(@complete);
                                }

                                if (msg.ShouldStop && f is not IAmStructural)
                                {
                                    outcome = TelemetryOutcome.Stopped;
                                    reason = msg.Execution.Reason;
                                    msg.OnLog?.Invoke($"STOPPED: {msg.Execution.Reason}");
                                }

                                msg.OnLog?.Invoke($"COMPLETED: {f.GetType().Name.Split('`')[0]}");
                            }
                        }

                        // Exit loop on successful execution
                        break;
                    }
                    catch (Exception ex) when (_retryWhen(ex, msg))
                    {
                        // Handle retries asynchronously
                        if (!await TryAgainAsync(msg, ex))
                        {
                            structuralOutcome = TelemetryOutcome.Exception;
                            structuralReason = ex.Message;
                            throw;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                structuralOutcome = TelemetryOutcome.Exception;
                structuralReason = ex.Message;
                throw;
            }
            finally
            {
                structuralSw.Stop();
                
                // Build end attributes including retry info if retries occurred
                var endAttributes = new Dictionary<string, object>
                {
                    { "max-attempts", _maxRetries+1 },
                    { "final-attempt", msg.Attempt }
                };
                
                // Add retry information if any retries occurred
                if (msg.Attempt > 1 && _lastRetryReason != null)
                {
                    endAttributes["retry"] = msg.Attempt - 1; // number of retries that occurred
                    endAttributes["retry-reason"] = _lastRetryReason;
                }
                
                var @retryEnd = new TelemetryEvent
                {
                    Component = nameof(OnTimeoutRetry<T>),
                    PipelineName = msg.PipelineName,
                    Service = msg.Service,
                    Scope = TelemetryScope.Filter,
                    Role = FilterRole.Structural,
                    Phase = TelemetryPhase.End,
                    MessageId = msg.CorrelationId,
                    Outcome = structuralOutcome,
                    Reason = structuralReason,
                    Timestamp = DateTimeOffset.UtcNow,
                    Duration = structuralSw.ElapsedMilliseconds,
                    Attributes = endAttributes
                };
                if (msg.ShouldEmitTelemetry(@retryEnd)) msg.OnTelemetry?.Invoke(@retryEnd);
                
                // Clear any remaining annotations to prevent leaking
                msg.Execution.TelemetryAnnotations.Clear();
            }
        }

        /// <summary>
        /// Handles incrementing the attempt counter and applying delay between retries
        /// </summary>
        /// <param name="msg">Message being processed</param>
        /// <param name="ex">The exception that triggered retry</param>
        /// <returns>True if retry will occur, false if max attempts reached</returns>
        private async Task<bool> TryAgainAsync(T msg, Exception ex)
        {
            if (msg.Attempt <= msg.MaxRetries)
            {
                // Track retry reason for telemetry
                _lastRetryReason = ex.Message;
                
                // Set retry annotations so they appear on the next iteration's child filter Start events
                msg.Execution.TelemetryAnnotations["retry"] = msg.Attempt;
                msg.Execution.TelemetryAnnotations["retry-reason"] = ex.Message;
                
                // Call optional hook before incrementing attempt
                msg.OnRetrying?.Invoke(msg.Attempt);

                msg.OnLog?.Invoke($"RETRYING {msg.Attempt}/{msg.MaxRetries}...");
                msg.Attempt++;

                // Wait asynchronously according to the delay strategy
                await Task.Delay(_defaultDelay(msg.Attempt, msg));

                return true;
            }

            // No more retries left
            return false;
        }

        /// <summary>
        /// Default retry logic for transient errors:
        /// - TimeoutException
        /// - Transport-level database/network errors
        /// - Deadlocks
        /// </summary>
        private static bool DefaultRetryWhen(Exception ex, T _) =>
            ex is TimeoutException ||
            ex.Message.Contains("transport-level error") ||
            ex.Message.Contains("deadlocked") ||
            ex.Message.Contains("timeout");

        /// <summary>
        /// Default linear sliding delay between retries
        /// </summary>
        private static TimeSpan DefaultDelay(int attempt, T _) => TimeSpan.FromSeconds(attempt * 2);
    }
}