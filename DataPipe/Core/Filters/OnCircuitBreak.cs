using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using DataPipe.Core.Contracts;
using DataPipe.Core.Contracts.Internal;
using DataPipe.Core.Telemetry;

namespace DataPipe.Core.Filters
{
    /// <summary>
    /// OnCircuitBreak provides the circuit breaker pattern as a first-class
    /// structural filter in a DataPipe pipeline. It wraps one or more filters
    /// and monitors their success/failure rate. After a configurable number
    /// of consecutive failures, the circuit trips to Open, causing all
    /// subsequent invocations to fail fast without executing the wrapped filters.
    /// After a configurable break duration, the circuit moves to Half-Open and
    /// allows a probe attempt to test if the resource has recovered.
    ///
    /// CircuitBreakerState is designed to be shared across pipeline invocations.
    /// In a Web API, register it as a singleton in DI so all pipelines hitting
    /// the same resource share the same circuit state.
    ///
    /// Example Usages:
    ///
    /// Basic circuit breaker around an external API call:
    ///    var circuitState = new CircuitBreakerState();
    ///    pipe.Add(new OnCircuitBreak<MyMessage>(circuitState,
    ///        new CallExternalApi()));
    ///    - Trips after 5 consecutive failures (default)
    ///    - Stays open for 30 seconds (default)
    ///
    /// Custom thresholds:
    ///    pipe.Add(new OnCircuitBreak<MyMessage>(circuitState,
    ///        failureThreshold: 3,
    ///        breakDuration: TimeSpan.FromMinutes(1),
    ///        new CallExternalApi()));
    ///
    /// Shared state across pipelines (DI singleton):
    ///    services.AddSingleton(new CircuitBreakerState());
    ///
    ///    pipe.Add(new OnCircuitBreak<OrderMessage>(sharedState,
    ///        failureThreshold: 10,
    ///        breakDuration: TimeSpan.FromSeconds(60),
    ///        new FetchFromPaymentGateway()));
    ///
    /// Combined with retry (retry inside circuit breaker):
    ///    pipe.Add(new OnCircuitBreak<OrderMessage>(circuitState,
    ///        new OnTimeoutRetry<OrderMessage>(maxRetries: 2,
    ///            new CallExternalApi())));
    /// </summary>
    public class OnCircuitBreak<T> : Filter<T>, IAmStructural where T : BaseMessage
    {
        public bool EmitTelemetryEvent => false; // emit own start/end to include circuit-state attributes

        private readonly Filter<T>[] _filters;
        private readonly CircuitBreakerState _state;
        private readonly int _failureThreshold;
        private readonly TimeSpan _breakDuration;

        public OnCircuitBreak(
            CircuitBreakerState state,
            int failureThreshold = 5,
            TimeSpan? breakDuration = null,
            params Filter<T>[] filters)
        {
            _state = state;
            _failureThreshold = failureThreshold;
            _breakDuration = breakDuration ?? TimeSpan.FromSeconds(30);
            _filters = filters;
        }

        public async Task Execute(T msg)
        {
            var structuralSw = Stopwatch.StartNew();
            var structuralOutcome = TelemetryOutcome.Success;
            var structuralReason = string.Empty;
            var circuitTripped = false;
            bool fastFail = false;
            CircuitState initialState;

            // 1. Check and transition circuit state (thread-safe)
            lock (_state.Lock)
            {
                initialState = _state.Status;

                if (_state.Status == CircuitState.Open)
                {
                    if (DateTimeOffset.UtcNow < _state.LockedUntil)
                    {
                        fastFail = true;
                    }
                    else
                    {
                        _state.Status = CircuitState.HalfOpen;
                        initialState = CircuitState.HalfOpen;
                        msg.OnLog?.Invoke("Circuit is HALF-OPEN. Probing external resource...");
                    }
                }
                else if (_state.Status == CircuitState.HalfOpen)
                {
                    // Another request is already probing — fast-fail others
                    fastFail = true;
                }
            }

            // 2. Emit start event with circuit state attributes
            var startAttributes = new Dictionary<string, object>(msg.Execution.TelemetryAnnotations)
            {
                { "circuit-state", initialState.ToString() },
                { "failure-threshold", _failureThreshold },
                { "break-duration-seconds", _breakDuration.TotalSeconds }
            };
            msg.Execution.TelemetryAnnotations.Clear();

            var @cbStart = new TelemetryEvent
            {
                Actor = msg.Actor,
                Component = nameof(OnCircuitBreak<T>),
                PipelineName = msg.PipelineName,
                Service = msg.Service,
                Scope = TelemetryScope.Filter,
                Role = FilterRole.Structural,
                Phase = TelemetryPhase.Start,
                MessageId = msg.CorrelationId,
                Timestamp = DateTimeOffset.UtcNow,
                Attributes = startAttributes
            };
            if (msg.ShouldEmitTelemetry(@cbStart)) msg.OnTelemetry?.Invoke(@cbStart);

            try
            {
                // 3. Fast-fail if circuit is open or half-open (probing in progress)
                if (fastFail)
                {
                    msg.OnLog?.Invoke("Circuit is OPEN. Failing fast.");
                    throw new CircuitBreakerOpenException();
                }

                // 4. Execute the wrapped filters
                foreach (var f in _filters)
                {
                    var reason = string.Empty;
                    var fsw = Stopwatch.StartNew();

                    var selfEmitting = f is IAmStructural structural && !structural.EmitTelemetryEvent;
                    var emitStart = f is not IAmStructural || (f is IAmStructural s && s.EmitTelemetryEvent);

                    if (!msg.ShouldStop && emitStart)
                    {
                        var @start = new TelemetryEvent
                        {
                            Actor = msg.Actor,
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
                        break;
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

                        if (!selfEmitting)
                        {
                            var @complete = new TelemetryEvent
                            {
                                Actor = msg.Actor,
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
                                DurationMs = fsw.ElapsedMilliseconds,
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

                        msg.OnLog?.Invoke($"COMPLETED: {f.GetType().Name.Split('`')[0]} ({fsw.ElapsedMilliseconds}ms)");
                    }
                }

                // 5. Success — reset the circuit (only if filters actually executed)
                if (!msg.ShouldStop)
                {
                    lock (_state.Lock)
                    {
                        if (_state.Status == CircuitState.HalfOpen)
                        {
                            msg.OnLog?.Invoke("Probe succeeded! Closing circuit.");
                        }

                        _state.Status = CircuitState.Closed;
                        _state.FailureCount = 0;
                    }
                }
            }
            catch (Exception ex)
            {
                // 6. Failure — increment count or trip the breaker
                if (!fastFail)
                {
                    lock (_state.Lock)
                    {
                        _state.FailureCount++;
                        msg.OnLog?.Invoke($"Filter failed. Failure count: {_state.FailureCount}");

                        if (_state.FailureCount >= _failureThreshold || _state.Status == CircuitState.HalfOpen)
                        {
                            _state.Status = CircuitState.Open;
                            _state.LockedUntil = DateTimeOffset.UtcNow.Add(_breakDuration);
                            circuitTripped = true;
                            msg.OnLog?.Invoke($"Circuit TRIPPED to OPEN for {_breakDuration.TotalSeconds} seconds.");
                        }
                    }
                }

                structuralOutcome = TelemetryOutcome.Exception;
                structuralReason = ex.Message;
                throw;
            }
            finally
            {
                structuralSw.Stop();

                CircuitState finalState;
                int finalFailureCount;
                lock (_state.Lock)
                {
                    finalState = _state.Status;
                    finalFailureCount = _state.FailureCount;
                }

                var endAttributes = new Dictionary<string, object>
                {
                    { "circuit-state", finalState.ToString() },
                    { "failure-count", finalFailureCount },
                    { "circuit-tripped", circuitTripped }
                };

                var @cbEnd = new TelemetryEvent
                {
                    Actor = msg.Actor,
                    Component = nameof(OnCircuitBreak<T>),
                    PipelineName = msg.PipelineName,
                    Service = msg.Service,
                    Scope = TelemetryScope.Filter,
                    Role = FilterRole.Structural,
                    Phase = TelemetryPhase.End,
                    MessageId = msg.CorrelationId,
                    Outcome = structuralOutcome,
                    Reason = structuralReason,
                    Timestamp = DateTimeOffset.UtcNow,
                    DurationMs = structuralSw.ElapsedMilliseconds,
                    Attributes = endAttributes
                };
                if (msg.ShouldEmitTelemetry(@cbEnd)) msg.OnTelemetry?.Invoke(@cbEnd);

                msg.Execution.TelemetryAnnotations.Clear();
            }
        }
    }
}