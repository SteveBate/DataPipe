using System;
using System.Collections.Generic;
using System.Diagnostics;
using DataPipe.Core.Contracts;
using DataPipe.Core.Contracts.Internal;
using DataPipe.Core.Telemetry;

namespace DataPipe.Core.Filters
{
    /// <summary>
    /// OnRateLimit provides the leaky bucket rate limiting pattern as a first-class
    /// structural filter in a DataPipe pipeline. It wraps one or more filters and
    /// enforces a maximum throughput rate by tracking request timestamps in a shared
    /// bucket. When the bucket is full, behaviour depends on the configured mode:
    ///
    /// - Delay: waits until capacity is available (backpressure)
    /// - Reject: immediately fails with 429 status without executing wrapped filters
    ///
    /// RateLimiterState is designed to be shared across pipeline invocations.
    /// In a Web API, register it as a singleton in DI so all pipelines hitting
    /// the same resource share the same bucket.
    ///
    /// Example Usages:
    ///
    /// Throttle database inserts to 200/second with backpressure:
    ///    var dbThrottle = new RateLimiterState();
    ///    pipe.Add(new OnRateLimit<ImportMessage>(dbThrottle,
    ///        capacity: 200,
    ///        leakInterval: TimeSpan.FromMilliseconds(5),
    ///        new InsertRecord()));
    ///
    /// Hard reject at 50 requests/second:
    ///    pipe.Add(new OnRateLimit<ApiMessage>(apiThrottle,
    ///        capacity: 50,
    ///        leakInterval: TimeSpan.FromMilliseconds(20),
    ///        behavior: RateLimitExceededBehavior.Reject,
    ///        new CallExternalApi()));
    ///
    /// Combined with circuit breaker and retry:
    ///    pipe.Add(new OnRateLimit<OrderMessage>(shopifyThrottle,
    ///        capacity: 40,
    ///        leakInterval: TimeSpan.FromMilliseconds(500),
    ///        new OnCircuitBreak<OrderMessage>(circuitState,
    ///            new OnTimeoutRetry<OrderMessage>(maxRetries: 2,
    ///                new CallShopifyApi()))));
    /// </summary>
    public class OnRateLimit<T> : Filter<T>, IAmStructural where T : BaseMessage
    {
        public bool EmitTelemetryEvent => false; // emit own start/end to include rate-limit attributes

        private readonly Filter<T>[] _filters;
        private readonly RateLimiterState _state;
        private readonly int _capacity;
        private readonly TimeSpan _leakInterval;
        private readonly RateLimitExceededBehavior _behavior;

        public OnRateLimit(
            RateLimiterState state,
            int capacity,
            TimeSpan leakInterval,
            RateLimitExceededBehavior behavior = RateLimitExceededBehavior.Delay,
            params Filter<T>[] filters)
        {
            ArgumentNullException.ThrowIfNull(state);
            if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity), capacity, "Capacity must be at least 1.");
            if (leakInterval <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(leakInterval), leakInterval, "Leak interval must be greater than zero.");

            _state = state;
            _capacity = capacity;
            _leakInterval = leakInterval;
            _behavior = behavior;
            _filters = filters;
        }

        public async Task Execute(T msg)
        {
            var telemetryEnabled = msg.TelemetryMode != TelemetryMode.Off;
            var timingEnabled = telemetryEnabled || msg.EnableTimings;
            Stopwatch? structuralSw = timingEnabled ? Stopwatch.StartNew() : null;
            var structuralOutcome = TelemetryOutcome.Success;
            var structuralReason = string.Empty;
            var rejected = false;
            long waitTimeMs = 0;
            int queueDepthAtEntry;

            // 1. Acquire a slot in the bucket (drain expired tokens, check capacity)
            Stopwatch? waitSw = timingEnabled ? Stopwatch.StartNew() : null;
            bool admitted = await TryAcquireAsync(msg).ConfigureAwait(false);
            waitSw?.Stop();
            waitTimeMs = waitSw?.ElapsedMilliseconds ?? 0;

            lock (_state.Lock)
            {
                queueDepthAtEntry = _state.Tokens.Count;
            }

            if (!admitted)
            {
                rejected = true;
            }

            // 2. Emit start event with rate-limit attributes
            var startAttributes = new Dictionary<string, object>(msg.Execution.TelemetryAnnotations)
            {
                { "capacity", _capacity },
                { "queue-depth", queueDepthAtEntry },
                { "leak-interval-ms", _leakInterval.TotalMilliseconds },
                { "behavior", _behavior.ToString() }
            };
            msg.Execution.ClearTelemetryAnnotations();

            var @rlStart = new TelemetryEvent
            {
                Actor = msg.Actor,
                Component = nameof(OnRateLimit<T>),
                PipelineName = msg.PipelineName,
                Service = msg.Service,
                Scope = TelemetryScope.Filter,
                Role = FilterRole.Structural,
                Phase = TelemetryPhase.Start,
                MessageId = msg.CorrelationId,
                Timestamp = DateTimeOffset.UtcNow,
                Attributes = startAttributes
            };
            if (msg.ShouldEmitTelemetry(@rlStart)) msg.OnTelemetry?.Invoke(@rlStart);

            try
            {
                // 3. Reject if bucket was full and behaviour is Reject
                if (rejected)
                {
                    msg.OnLog?.Invoke($"Rate limit exceeded. Bucket full ({_capacity}). Rejecting request.");
                    throw new RateLimitRejectedException();
                }

                if (waitTimeMs > 0)
                {
                    msg.OnLog?.Invoke($"Rate limit: waited {waitTimeMs}ms for bucket capacity.");
                }

                // 4. Execute the wrapped filters
                await FilterRunner.ExecuteFiltersAsync(_filters, msg, msg.PipelineName).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                structuralOutcome = TelemetryOutcome.Exception;
                structuralReason = ex.Message;
                throw;
            }
            finally
            {
                structuralSw?.Stop();

                int finalQueueDepth;
                lock (_state.Lock)
                {
                    finalQueueDepth = _state.Tokens.Count;
                }

                var endAttributes = new Dictionary<string, object>
                {
                    { "capacity", _capacity },
                    { "queue-depth", finalQueueDepth },
                    { "wait-time-ms", waitTimeMs },
                    { "rejected", rejected }
                };

                var @rlEnd = new TelemetryEvent
                {
                    Actor = msg.Actor,
                    Component = nameof(OnRateLimit<T>),
                    PipelineName = msg.PipelineName,
                    Service = msg.Service,
                    Scope = TelemetryScope.Filter,
                    Role = FilterRole.Structural,
                    Phase = TelemetryPhase.End,
                    MessageId = msg.CorrelationId,
                    Outcome = structuralOutcome,
                    Reason = structuralReason,
                    Timestamp = DateTimeOffset.UtcNow,
                    DurationMs = structuralSw?.ElapsedMilliseconds ?? 0,
                    Attributes = endAttributes
                };
                if (msg.ShouldEmitTelemetry(@rlEnd)) msg.OnTelemetry?.Invoke(@rlEnd);

                msg.Execution.ClearTelemetryAnnotations();
            }
        }

        /// <summary>
        /// Attempts to acquire a token in the leaky bucket.
        /// Drains expired tokens, then either adds a new token (admitted),
        /// waits for capacity (Delay mode), or returns false (Reject mode).
        /// </summary>
        private async Task<bool> TryAcquireAsync(T msg)
        {
            while (true)
            {
                TimeSpan? waitTime = null;

                lock (_state.Lock)
                {
                    DrainExpiredTokens();

                    if (_state.Tokens.Count < _capacity)
                    {
                        // Capacity available — add our token and proceed
                        _state.Tokens.Enqueue(DateTimeOffset.UtcNow);
                        return true;
                    }

                    // Bucket is full
                    if (_behavior == RateLimitExceededBehavior.Reject)
                    {
                        return false;
                    }

                    // Delay mode — calculate how long until the oldest token expires.
                    // Tokens.Count is guaranteed > 0 here because Count >= capacity >= 1.
                    var oldest = _state.Tokens.Peek();
                    var expiresAt = oldest.Add(_leakInterval);
                    var remaining = expiresAt - DateTimeOffset.UtcNow;
                    waitTime = remaining > TimeSpan.Zero ? remaining : TimeSpan.FromMilliseconds(1);
                }

                // Wait outside the lock, respecting cancellation
                if (waitTime.HasValue)
                {
                    msg.OnLog?.Invoke($"Rate limit: bucket full ({_capacity}). Waiting {waitTime.Value.TotalMilliseconds:F0}ms...");
                    await Task.Delay(waitTime.Value, msg.CancellationToken).ConfigureAwait(false);
                }
            }
        }

        /// <summary>
        /// Removes tokens that have expired (older than the leak interval).
        /// Must be called within the state lock.
        /// </summary>
        private void DrainExpiredTokens()
        {
            var now = DateTimeOffset.UtcNow;
            while (_state.Tokens.Count > 0 && now - _state.Tokens.Peek() >= _leakInterval)
            {
                _state.Tokens.Dequeue();
            }
        }
    }
}
