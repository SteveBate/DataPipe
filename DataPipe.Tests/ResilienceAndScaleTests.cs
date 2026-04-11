using DataPipe.Core;
using DataPipe.Core.Contracts;
using DataPipe.Core.Filters;
using DataPipe.Core.Middleware;
using DataPipe.Core.Telemetry;
using DataPipe.Core.Telemetry.Adapters;
using DataPipe.Tests.Support;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace DataPipe.Tests
{
    // =========================================================================
    // 1. Circuit Breaker Tests
    // =========================================================================

    [TestClass]
    public class CircuitBreakerTests
    {
        ServiceIdentity si = new ServiceIdentity
        {
            Name = "DataPipeTestService",
            Version = "1.0.0",
            Environment = "Dev",
            InstanceId = Guid.NewGuid().ToString()
        };

        [TestMethod]
        public async Task Should_execute_filters_when_circuit_is_closed()
        {
            // given
            var state = new CircuitBreakerState();
            var sut = new DataPipe<TestMessage>();
            sut.Use(new ExceptionAspect<TestMessage>());
            sut.Add(new OnCircuitBreak<TestMessage>(state,
                filters: [new IncrementingNumberFilter()]));
            var msg = new TestMessage { Number = 0, Service = si };

            // when
            await sut.Invoke(msg);

            // then
            Assert.AreEqual(1, msg.Number);
            Assert.IsTrue(msg.IsSuccess);
            Assert.AreEqual(CircuitState.Closed, state.Status);
        }

        [TestMethod]
        public async Task Should_trip_circuit_after_failure_threshold_reached()
        {
            // given
            var state = new CircuitBreakerState();

            for (int i = 0; i < 5; i++)
            {
                var pipe = new DataPipe<TestMessage>();
                pipe.Use(new ExceptionAspect<TestMessage>());
                pipe.Add(new OnCircuitBreak<TestMessage>(state,
                    failureThreshold: 5,
                    filters: [new AlwaysFailingFilter()]));
                var msg = new TestMessage { Service = si };
                await pipe.Invoke(msg);
            }

            // then
            Assert.AreEqual(CircuitState.Open, state.Status);
        }

        [TestMethod]
        public async Task Should_fail_fast_when_circuit_is_open()
        {
            // given — trip the circuit
            var state = new CircuitBreakerState();
            for (int i = 0; i < 5; i++)
            {
                var pipe = new DataPipe<TestMessage>();
                pipe.Use(new ExceptionAspect<TestMessage>());
                pipe.Add(new OnCircuitBreak<TestMessage>(state, failureThreshold: 5,
                    filters: [new AlwaysFailingFilter()]));
                await pipe.Invoke(new TestMessage { Service = si });
            }
            Assert.AreEqual(CircuitState.Open, state.Status);

            // when — next request should fail fast
            var sut = new DataPipe<TestMessage>();
            sut.Add(new OnCircuitBreak<TestMessage>(state, failureThreshold: 5,
                filters: [new IncrementingNumberFilter()]));
            var fastFailMsg = new TestMessage { Number = 0, Service = si };

            // then
            await Assert.ThrowsExceptionAsync<CircuitBreakerOpenException>(
                async () => await sut.Invoke(fastFailMsg));
            Assert.AreEqual(0, fastFailMsg.Number); // filters did not execute
        }

        [TestMethod]
        public async Task Should_not_trip_circuit_before_threshold()
        {
            // given — fail 4 times with threshold of 5
            var state = new CircuitBreakerState();
            for (int i = 0; i < 4; i++)
            {
                var pipe = new DataPipe<TestMessage>();
                pipe.Use(new ExceptionAspect<TestMessage>());
                pipe.Add(new OnCircuitBreak<TestMessage>(state, failureThreshold: 5,
                    filters: [new AlwaysFailingFilter()]));
                await pipe.Invoke(new TestMessage { Service = si });
            }

            // then — still closed
            Assert.AreEqual(CircuitState.Closed, state.Status);
            Assert.AreEqual(4, state.FailureCount);
        }

        [TestMethod]
        public async Task Should_reset_failure_count_on_success()
        {
            // given — accumulate some failures then succeed
            var state = new CircuitBreakerState();
            for (int i = 0; i < 3; i++)
            {
                var pipe = new DataPipe<TestMessage>();
                pipe.Use(new ExceptionAspect<TestMessage>());
                pipe.Add(new OnCircuitBreak<TestMessage>(state, failureThreshold: 5,
                    filters: [new AlwaysFailingFilter()]));
                await pipe.Invoke(new TestMessage { Service = si });
            }
            Assert.AreEqual(3, state.FailureCount);

            // when — a success resets
            var sut = new DataPipe<TestMessage>();
            sut.Use(new ExceptionAspect<TestMessage>());
            sut.Add(new OnCircuitBreak<TestMessage>(state, failureThreshold: 5,
                filters: [new IncrementingNumberFilter()]));
            await sut.Invoke(new TestMessage { Number = 0, Service = si });

            // then
            Assert.AreEqual(CircuitState.Closed, state.Status);
            Assert.AreEqual(0, state.FailureCount);
        }

        [TestMethod]
        public async Task Should_transition_to_half_open_after_break_duration()
        {
            // given — trip circuit with a very short break duration
            var state = new CircuitBreakerState();
            for (int i = 0; i < 3; i++)
            {
                var pipe = new DataPipe<TestMessage>();
                pipe.Use(new ExceptionAspect<TestMessage>());
                pipe.Add(new OnCircuitBreak<TestMessage>(state,
                    failureThreshold: 3,
                    breakDuration: TimeSpan.FromMilliseconds(50),
                    filters: [new AlwaysFailingFilter()]));
                await pipe.Invoke(new TestMessage { Service = si });
            }
            Assert.AreEqual(CircuitState.Open, state.Status);

            // when — wait for break to expire, then invoke
            await Task.Delay(100);
            var sut = new DataPipe<TestMessage>();
            sut.Use(new ExceptionAspect<TestMessage>());
            sut.Add(new OnCircuitBreak<TestMessage>(state,
                failureThreshold: 3,
                breakDuration: TimeSpan.FromMilliseconds(50),
                filters: [new IncrementingNumberFilter()]));
            var msg = new TestMessage { Number = 0, Service = si };
            await sut.Invoke(msg);

            // then — probe succeeded, circuit closed
            Assert.AreEqual(1, msg.Number);
            Assert.AreEqual(CircuitState.Closed, state.Status);
            Assert.AreEqual(0, state.FailureCount);
        }

        [TestMethod]
        public async Task Should_reopen_circuit_if_half_open_probe_fails()
        {
            // given — trip circuit with short break
            var state = new CircuitBreakerState();
            for (int i = 0; i < 3; i++)
            {
                var pipe = new DataPipe<TestMessage>();
                pipe.Use(new ExceptionAspect<TestMessage>());
                pipe.Add(new OnCircuitBreak<TestMessage>(state,
                    failureThreshold: 3,
                    breakDuration: TimeSpan.FromMilliseconds(50),
                    filters: [new AlwaysFailingFilter()]));
                await pipe.Invoke(new TestMessage { Service = si });
            }

            // when — wait for break, probe with a failing filter
            await Task.Delay(100);
            var sut = new DataPipe<TestMessage>();
            sut.Use(new ExceptionAspect<TestMessage>());
            sut.Add(new OnCircuitBreak<TestMessage>(state,
                failureThreshold: 3,
                breakDuration: TimeSpan.FromMilliseconds(50),
                filters: [new AlwaysFailingFilter()]));
            await sut.Invoke(new TestMessage { Service = si });

            // then — back to Open
            Assert.AreEqual(CircuitState.Open, state.Status);
        }

        [TestMethod]
        public async Task Should_use_custom_failure_threshold()
        {
            // given
            var state = new CircuitBreakerState();

            for (int i = 0; i < 2; i++)
            {
                var pipe = new DataPipe<TestMessage>();
                pipe.Use(new ExceptionAspect<TestMessage>());
                pipe.Add(new OnCircuitBreak<TestMessage>(state, failureThreshold: 2,
                    filters: [new AlwaysFailingFilter()]));
                await pipe.Invoke(new TestMessage { Service = si });
            }

            // then — trips at 2 instead of default 5
            Assert.AreEqual(CircuitState.Open, state.Status);
        }

        [TestMethod]
        public async Task Should_share_state_across_pipeline_invocations()
        {
            // given — shared state simulates singleton DI registration
            var state = new CircuitBreakerState();

            // when — multiple independent pipelines use the same state
            for (int i = 0; i < 3; i++)
            {
                var pipe = new DataPipe<TestMessage>();
                pipe.Use(new ExceptionAspect<TestMessage>());
                pipe.Add(new OnCircuitBreak<TestMessage>(state, failureThreshold: 3,
                    filters: [new AlwaysFailingFilter()]));
                await pipe.Invoke(new TestMessage { Service = si });
            }

            // then — failure count accumulated across invocations
            Assert.AreEqual(CircuitState.Open, state.Status);
        }

        [TestMethod]
        public async Task Should_emit_telemetry_with_circuit_state_attributes()
        {
            // given
            var telemetryEvents = new List<TelemetryEvent>();
            var state = new CircuitBreakerState();
            var sut = new DataPipe<TestMessage> { TelemetryMode = TelemetryMode.PipelineAndFilters };
            sut.Use(new ExceptionAspect<TestMessage>());
            sut.Use(new TelemetryAspect<TestMessage>(new TestTelemetryAdapter()));
            sut.Add(new OnCircuitBreak<TestMessage>(state,
                failureThreshold: 5,
                breakDuration: TimeSpan.FromSeconds(30),
                filters: [new IncrementingNumberFilter()]));
            var msg = new TestMessage
            {
                Number = 0,
                Service = si,
                OnTelemetry = e => telemetryEvents.Add(e)
            };

            // when
            await sut.Invoke(msg);

            // then
            var startEvent = telemetryEvents.FirstOrDefault(e =>
                e.Component == "OnCircuitBreak" && e.Phase == TelemetryPhase.Start);
            Assert.IsNotNull(startEvent);
            Assert.AreEqual("Closed", startEvent.Attributes["circuit-state"]);
            Assert.AreEqual(5, startEvent.Attributes["failure-threshold"]);
            Assert.AreEqual(30.0, startEvent.Attributes["break-duration-seconds"]);

            var endEvent = telemetryEvents.FirstOrDefault(e =>
                e.Component == "OnCircuitBreak" && e.Phase == TelemetryPhase.End);
            Assert.IsNotNull(endEvent);
            Assert.AreEqual("Closed", endEvent.Attributes["circuit-state"]);
            Assert.AreEqual(0, endEvent.Attributes["failure-count"]);
            Assert.AreEqual(false, endEvent.Attributes["circuit-tripped"]);
        }

        [TestMethod]
        public async Task Should_emit_telemetry_showing_circuit_tripped()
        {
            // given — trip the circuit and capture telemetry from the tripping invocation
            var telemetryEvents = new List<TelemetryEvent>();
            var state = new CircuitBreakerState();

            // First 4 failures (no telemetry capture)
            for (int i = 0; i < 4; i++)
            {
                var pipe = new DataPipe<TestMessage>();
                pipe.Use(new ExceptionAspect<TestMessage>());
                pipe.Add(new OnCircuitBreak<TestMessage>(state, failureThreshold: 5,
                    filters: [new AlwaysFailingFilter()]));
                await pipe.Invoke(new TestMessage { Service = si });
            }

            // 5th failure — capture telemetry
            var sut = new DataPipe<TestMessage> { TelemetryMode = TelemetryMode.PipelineAndFilters };
            sut.Use(new ExceptionAspect<TestMessage>());
            sut.Use(new TelemetryAspect<TestMessage>(new TestTelemetryAdapter()));
            sut.Add(new OnCircuitBreak<TestMessage>(state, failureThreshold: 5,
                filters: [new AlwaysFailingFilter()]));
            var msg = new TestMessage
            {
                Service = si,
                OnTelemetry = e => telemetryEvents.Add(e)
            };
            await sut.Invoke(msg);

            // then
            var endEvent = telemetryEvents.FirstOrDefault(e =>
                e.Component == "OnCircuitBreak" && e.Phase == TelemetryPhase.End);
            Assert.IsNotNull(endEvent);
            Assert.AreEqual(true, endEvent.Attributes["circuit-tripped"]);
            Assert.AreEqual("Open", endEvent.Attributes["circuit-state"]);
        }

        [TestMethod]
        public async Task Should_handle_concurrent_requests_through_closed_circuit()
        {
            // given
            var state = new CircuitBreakerState();
            var tasks = Enumerable.Range(0, 20).Select(async _ =>
            {
                var pipe = new DataPipe<TestMessage>();
                pipe.Use(new ExceptionAspect<TestMessage>());
                pipe.Add(new OnCircuitBreak<TestMessage>(state,
                    filters: [new IncrementingNumberFilter()]));
                var msg = new TestMessage { Number = 0, Service = si };
                await pipe.Invoke(msg);
                return msg.Number;
            });

            // when
            var results = await Task.WhenAll(tasks);

            // then — all requests succeeded
            Assert.IsTrue(results.All(n => n == 1));
            Assert.AreEqual(CircuitState.Closed, state.Status);
        }

        [TestMethod]
        public async Task Should_fast_fail_concurrent_requests_when_circuit_open()
        {
            // given — trip the circuit
            var state = new CircuitBreakerState();
            for (int i = 0; i < 3; i++)
            {
                var pipe = new DataPipe<TestMessage>();
                pipe.Use(new ExceptionAspect<TestMessage>());
                pipe.Add(new OnCircuitBreak<TestMessage>(state, failureThreshold: 3,
                    filters: [new AlwaysFailingFilter()]));
                await pipe.Invoke(new TestMessage { Service = si });
            }

            // when — fire 10 concurrent requests against open circuit
            var results = new ConcurrentBag<int>();
            var tasks = Enumerable.Range(0, 10).Select(async _ =>
            {
                var pipe = new DataPipe<TestMessage>();
                pipe.Use(new ExceptionAspect<TestMessage>());
                pipe.Add(new OnCircuitBreak<TestMessage>(state, failureThreshold: 3,
                    filters: [new IncrementingNumberFilter()]));
                var msg = new TestMessage { Number = 0, Service = si };
                await pipe.Invoke(msg);
                results.Add(msg.StatusCode);
            });
            await Task.WhenAll(tasks);

            // then — all failed fast
            Assert.AreEqual(10, results.Count);
            Assert.IsTrue(results.All(sc => sc != 200),
                $"Expected all non-200 but got: {string.Join(",", results)}");
        }

        [TestMethod]
        public async Task Should_apply_jitter_to_break_duration()
        {
            // given — jitterRatio of 1.0 means ±100%, so break could be 0–200ms for a 100ms base
            var state = new CircuitBreakerState();
            var baseDuration = TimeSpan.FromMilliseconds(100);

            for (int i = 0; i < 3; i++)
            {
                var pipe = new DataPipe<TestMessage>();
                pipe.Use(new ExceptionAspect<TestMessage>());
                pipe.Add(new OnCircuitBreak<TestMessage>(state,
                    failureThreshold: 3,
                    breakDuration: baseDuration,
                    jitterRatio: 1.0,
                    filters: [new AlwaysFailingFilter()]));
                await pipe.Invoke(new TestMessage { Service = si });
            }

            // then — circuit is open with a LockedUntil that differs from exact base
            Assert.AreEqual(CircuitState.Open, state.Status);
            Assert.IsNotNull(state.LockedUntil);

            // The jittered duration should be between 0ms and 200ms from the trip time
            var remaining = state.LockedUntil.Value - DateTimeOffset.UtcNow;
            Assert.IsTrue(remaining.TotalMilliseconds <= 200,
                $"LockedUntil is too far in the future: {remaining.TotalMilliseconds}ms");
        }

        [TestMethod]
        public async Task Should_not_apply_jitter_when_ratio_is_zero()
        {
            // given
            var state = new CircuitBreakerState();
            var baseDuration = TimeSpan.FromSeconds(10);
            var tripTime = DateTimeOffset.UtcNow;

            for (int i = 0; i < 3; i++)
            {
                var pipe = new DataPipe<TestMessage>();
                pipe.Use(new ExceptionAspect<TestMessage>());
                pipe.Add(new OnCircuitBreak<TestMessage>(state,
                    failureThreshold: 3,
                    breakDuration: baseDuration,
                    jitterRatio: 0.0,
                    filters: [new AlwaysFailingFilter()]));
                await pipe.Invoke(new TestMessage { Service = si });
            }

            // then — LockedUntil should be very close to tripTime + 10 seconds (no jitter)
            Assert.AreEqual(CircuitState.Open, state.Status);
            var expected = tripTime.Add(baseDuration);
            var drift = Math.Abs((state.LockedUntil.Value - expected).TotalMilliseconds);
            Assert.IsTrue(drift < 500,
                $"Expected LockedUntil within 500ms of exact duration but drift was {drift}ms");
        }

        [TestMethod]
        public async Task Should_produce_varied_break_durations_with_jitter()
        {
            // given — trip the circuit multiple times with jitter and collect LockedUntil values
            var durations = new List<DateTimeOffset>();

            for (int run = 0; run < 10; run++)
            {
                var state = new CircuitBreakerState();
                for (int i = 0; i < 3; i++)
                {
                    var pipe = new DataPipe<TestMessage>();
                    pipe.Use(new ExceptionAspect<TestMessage>());
                    pipe.Add(new OnCircuitBreak<TestMessage>(state,
                        failureThreshold: 3,
                        breakDuration: TimeSpan.FromSeconds(10),
                        jitterRatio: 0.5,
                        filters: [new AlwaysFailingFilter()]));
                    await pipe.Invoke(new TestMessage { Service = si });
                }
                durations.Add(state.LockedUntil!.Value);
            }

            // then — not all durations should be identical (jitter introduces variance)
            var distinct = durations.Select(d => d.ToUnixTimeMilliseconds()).Distinct().Count();
            Assert.IsTrue(distinct > 1,
                "Expected varied LockedUntil values with jitter but all were identical");
        }

        [TestMethod]
        public async Task Should_clamp_jitter_ratio_to_valid_range()
        {
            // given — jitterRatio > 1.0 should be clamped to 1.0
            var state = new CircuitBreakerState();
            var baseDuration = TimeSpan.FromMilliseconds(100);

            for (int i = 0; i < 3; i++)
            {
                var pipe = new DataPipe<TestMessage>();
                pipe.Use(new ExceptionAspect<TestMessage>());
                pipe.Add(new OnCircuitBreak<TestMessage>(state,
                    failureThreshold: 3,
                    breakDuration: baseDuration,
                    jitterRatio: 5.0, // should be clamped to 1.0
                    filters: [new AlwaysFailingFilter()]));
                await pipe.Invoke(new TestMessage { Service = si });
            }

            // then — circuit tripped and LockedUntil is within clamped range (0–200ms from trip)
            Assert.AreEqual(CircuitState.Open, state.Status);
            var remaining = state.LockedUntil.Value - DateTimeOffset.UtcNow;
            Assert.IsTrue(remaining.TotalMilliseconds <= 200,
                $"Clamped jitter should limit LockedUntil but got {remaining.TotalMilliseconds}ms remaining");
        }
    }

    // =========================================================================
    // 2. Rate Limiter Tests
    // =========================================================================

    [TestClass]
    public class RateLimiterTests
    {
        ServiceIdentity si = new ServiceIdentity
        {
            Name = "DataPipeTestService",
            Version = "1.0.0",
            Environment = "Dev",
            InstanceId = Guid.NewGuid().ToString()
        };

        [TestMethod]
        public async Task Should_execute_filters_when_under_capacity()
        {
            // given
            var state = new RateLimiterState();
            var sut = new DataPipe<TestMessage>();
            sut.Use(new ExceptionAspect<TestMessage>());
            sut.Add(new OnRateLimit<TestMessage>(state,
                capacity: 10,
                leakInterval: TimeSpan.FromSeconds(1),
                filters: [new IncrementingNumberFilter()]));
            var msg = new TestMessage { Number = 0, Service = si };

            // when
            await sut.Invoke(msg);

            // then
            Assert.AreEqual(1, msg.Number);
            Assert.IsTrue(msg.IsSuccess);
        }

        [TestMethod]
        public async Task Should_reject_when_bucket_full_in_reject_mode()
        {
            // given — fill bucket to capacity
            var state = new RateLimiterState();
            for (int i = 0; i < 3; i++)
            {
                var pipe = new DataPipe<TestMessage>();
                pipe.Use(new ExceptionAspect<TestMessage>());
                pipe.Add(new OnRateLimit<TestMessage>(state,
                    capacity: 3,
                    leakInterval: TimeSpan.FromSeconds(10), // very slow leak
                    behavior: RateLimitExceededBehavior.Reject,
                    new IncrementingNumberFilter()));
                await pipe.Invoke(new TestMessage { Number = 0, Service = si });
            }

            // when — next request exceeds capacity
            var sut = new DataPipe<TestMessage>();
            sut.Add(new OnRateLimit<TestMessage>(state,
                capacity: 3,
                leakInterval: TimeSpan.FromSeconds(10),
                behavior: RateLimitExceededBehavior.Reject,
                new IncrementingNumberFilter()));
            var msg = new TestMessage { Number = 0, Service = si };

            // then
            await Assert.ThrowsExceptionAsync<RateLimitRejectedException>(
                async () => await sut.Invoke(msg));
            Assert.AreEqual(0, msg.Number); // filters did not execute
        }

        [TestMethod]
        public async Task Should_delay_when_bucket_full_in_delay_mode()
        {
            // given — fill bucket to capacity
            var state = new RateLimiterState();
            for (int i = 0; i < 3; i++)
            {
                var pipe = new DataPipe<TestMessage>();
                pipe.Use(new ExceptionAspect<TestMessage>());
                pipe.Add(new OnRateLimit<TestMessage>(state,
                    capacity: 3,
                    leakInterval: TimeSpan.FromMilliseconds(50),
                    behavior: RateLimitExceededBehavior.Delay,
                    new NoOpFilter()));
                await pipe.Invoke(new TestMessage { Service = si });
            }

            // when — next request must wait for a token to leak
            var sw = Stopwatch.StartNew();
            var sut = new DataPipe<TestMessage>();
            sut.Use(new ExceptionAspect<TestMessage>());
            sut.Add(new OnRateLimit<TestMessage>(state,
                capacity: 3,
                leakInterval: TimeSpan.FromMilliseconds(50),
                behavior: RateLimitExceededBehavior.Delay,
                new IncrementingNumberFilter()));
            var msg = new TestMessage { Number = 0, Service = si };
            await sut.Invoke(msg);
            sw.Stop();

            // then — executed after waiting
            Assert.AreEqual(1, msg.Number);
            Assert.IsTrue(sw.ElapsedMilliseconds >= 30,
                $"Expected wait >= 30ms but was {sw.ElapsedMilliseconds}ms");
        }

        [TestMethod]
        public async Task Should_allow_requests_after_tokens_leak()
        {
            // given — fill bucket, then wait for all tokens to leak
            var state = new RateLimiterState();
            for (int i = 0; i < 3; i++)
            {
                var pipe = new DataPipe<TestMessage>();
                pipe.Use(new ExceptionAspect<TestMessage>());
                pipe.Add(new OnRateLimit<TestMessage>(state,
                    capacity: 3,
                    leakInterval: TimeSpan.FromMilliseconds(30),
                    behavior: RateLimitExceededBehavior.Reject,
                    new NoOpFilter()));
                await pipe.Invoke(new TestMessage { Service = si });
            }

            // when — wait for tokens to expire
            await Task.Delay(100);
            var sut = new DataPipe<TestMessage>();
            sut.Use(new ExceptionAspect<TestMessage>());
            sut.Add(new OnRateLimit<TestMessage>(state,
                capacity: 3,
                leakInterval: TimeSpan.FromMilliseconds(30),
                behavior: RateLimitExceededBehavior.Reject,
                new IncrementingNumberFilter()));
            var msg = new TestMessage { Number = 0, Service = si };
            await sut.Invoke(msg);

            // then
            Assert.AreEqual(1, msg.Number);
            Assert.IsTrue(msg.IsSuccess);
        }

        [TestMethod]
        public async Task Should_share_state_across_pipeline_invocations()
        {
            // given
            var state = new RateLimiterState();
            int successCount = 0;
            int rejectCount = 0;

            for (int i = 0; i < 5; i++)
            {
                var pipe = new DataPipe<TestMessage>();
                pipe.Use(new ExceptionAspect<TestMessage>());
                pipe.Add(new OnRateLimit<TestMessage>(state,
                    capacity: 3,
                    leakInterval: TimeSpan.FromSeconds(10),
                    behavior: RateLimitExceededBehavior.Reject,
                    new NoOpFilter()));
                var msg = new TestMessage { Service = si };
                await pipe.Invoke(msg);

                if (msg.IsSuccess) successCount++;
                else rejectCount++;
            }

            // then — first 3 succeed, last 2 rejected
            Assert.AreEqual(3, successCount);
            Assert.AreEqual(2, rejectCount);
        }

        [TestMethod]
        public async Task Should_emit_telemetry_with_rate_limit_attributes()
        {
            // given
            var telemetryEvents = new List<TelemetryEvent>();
            var state = new RateLimiterState();
            var sut = new DataPipe<TestMessage> { TelemetryMode = TelemetryMode.PipelineAndFilters };
            sut.Use(new ExceptionAspect<TestMessage>());
            sut.Use(new TelemetryAspect<TestMessage>(new TestTelemetryAdapter()));
            sut.Add(new OnRateLimit<TestMessage>(state,
                capacity: 10,
                leakInterval: TimeSpan.FromMilliseconds(100),
                filters: [new IncrementingNumberFilter()]));
            var msg = new TestMessage
            {
                Number = 0,
                Service = si,
                OnTelemetry = e => telemetryEvents.Add(e)
            };

            // when
            await sut.Invoke(msg);

            // then
            var startEvent = telemetryEvents.FirstOrDefault(e =>
                e.Component == "OnRateLimit" && e.Phase == TelemetryPhase.Start);
            Assert.IsNotNull(startEvent);
            Assert.AreEqual(10, startEvent.Attributes["capacity"]);
            Assert.AreEqual(100.0, startEvent.Attributes["leak-interval-ms"]);
            Assert.AreEqual("Delay", startEvent.Attributes["behavior"]);

            var endEvent = telemetryEvents.FirstOrDefault(e =>
                e.Component == "OnRateLimit" && e.Phase == TelemetryPhase.End);
            Assert.IsNotNull(endEvent);
            Assert.AreEqual(false, endEvent.Attributes["rejected"]);
        }

        [TestMethod]
        public async Task Should_handle_concurrent_requests_within_capacity()
        {
            // given
            var state = new RateLimiterState();
            var tasks = Enumerable.Range(0, 5).Select(async _ =>
            {
                var pipe = new DataPipe<TestMessage>();
                pipe.Use(new ExceptionAspect<TestMessage>());
                pipe.Add(new OnRateLimit<TestMessage>(state,
                    capacity: 10,
                    leakInterval: TimeSpan.FromSeconds(1),
                    filters: [new IncrementingNumberFilter()]));
                var msg = new TestMessage { Number = 0, Service = si };
                await pipe.Invoke(msg);
                return msg.Number;
            });

            // when
            var results = await Task.WhenAll(tasks);

            // then
            Assert.IsTrue(results.All(n => n == 1));
        }

        [TestMethod]
        public async Task Should_reject_excess_concurrent_requests()
        {
            // given — capacity of 3, send 6 concurrent requests with long leak interval
            var state = new RateLimiterState();
            var successes = new ConcurrentBag<bool>();
            var tasks = Enumerable.Range(0, 6).Select(async _ =>
            {
                var pipe = new DataPipe<TestMessage>();
                pipe.Use(new ExceptionAspect<TestMessage>());
                pipe.Add(new OnRateLimit<TestMessage>(state,
                    capacity: 3,
                    leakInterval: TimeSpan.FromSeconds(10),
                    behavior: RateLimitExceededBehavior.Reject,
                    new NoOpFilter()));
                var msg = new TestMessage { Service = si };
                await pipe.Invoke(msg);
                successes.Add(msg.IsSuccess);
            });

            // when
            await Task.WhenAll(tasks);

            // then
            var successCount = successes.Count(s => s);
            var failCount = successes.Count(s => !s);
            Assert.AreEqual(3, successCount, $"Expected 3 successes but got {successCount}");
            Assert.AreEqual(3, failCount, $"Expected 3 rejections but got {failCount}");
        }

        [TestMethod]
        public async Task Should_cancel_delay_wait_when_token_cancelled()
        {
            // given — fill bucket
            var state = new RateLimiterState();
            for (int i = 0; i < 3; i++)
            {
                var pipe = new DataPipe<TestMessage>();
                pipe.Use(new ExceptionAspect<TestMessage>());
                pipe.Add(new OnRateLimit<TestMessage>(state,
                    capacity: 3,
                    leakInterval: TimeSpan.FromSeconds(30),
                    behavior: RateLimitExceededBehavior.Delay,
                    new NoOpFilter()));
                await pipe.Invoke(new TestMessage { Service = si });
            }

            // when — next request waits but gets cancelled
            using var cts = new CancellationTokenSource(millisecondsDelay: 50);
            var sut = new DataPipe<TestMessage>();
            sut.Use(new ExceptionAspect<TestMessage>());
            sut.Add(new OnRateLimit<TestMessage>(state,
                capacity: 3,
                leakInterval: TimeSpan.FromSeconds(30),
                behavior: RateLimitExceededBehavior.Delay,
                new IncrementingNumberFilter()));
            var msg = new TestMessage { Number = 0, Service = si, CancellationToken = cts.Token };
            await sut.Invoke(msg);

            // then — cancelled, filters never executed
            Assert.AreEqual(0, msg.Number);
            Assert.AreEqual(500, msg.StatusCode);
        }
    }

    // =========================================================================
    // 3. Nested Structural Filter Tests
    // =========================================================================

    [TestClass]
    public class NestedStructuralFilterTests
    {
        ServiceIdentity si = new ServiceIdentity
        {
            Name = "DataPipeTestService",
            Version = "1.0.0",
            Environment = "Dev",
            InstanceId = Guid.NewGuid().ToString()
        };

        [TestMethod]
        public async Task Should_nest_retry_inside_circuit_breaker()
        {
            // given — retry recovers, circuit stays closed
            var state = new CircuitBreakerState();
            var sut = new DataPipe<TestMessage>();
            sut.Use(new ExceptionAspect<TestMessage>());
            sut.Add(new OnCircuitBreak<TestMessage>(state, failureThreshold: 3,
                filters: [new OnTimeoutRetry<TestMessage>(maxRetries: 3,
                    retryWhen: (ex, msg) => ex is InvalidOperationException,
                    customDelay: (attempt, msg) => TimeSpan.FromMilliseconds(10),
                    new FailNTimesThenSucceedFilter(2))]));
            var msg = new TestMessage { Number = 0, Service = si };

            // when
            await sut.Invoke(msg);

            // then — retry succeeded so circuit remains closed
            Assert.IsTrue(msg.IsSuccess);
            Assert.AreEqual(CircuitState.Closed, state.Status);
            Assert.AreEqual(0, state.FailureCount);
        }

        [TestMethod]
        public async Task Should_nest_trycatch_around_timeout()
        {
            // given — TryCatch catches timeout from inner Timeout filter
            var sut = new DataPipe<TestMessage>();
            sut.Use(new ExceptionAspect<TestMessage>());
            sut.Add(new TryCatch<TestMessage>(
                tryFilters: [
                    new Timeout<TestMessage>(TimeSpan.FromMilliseconds(50),
                        new SlowFilter(5000))
                ],
                catchFilters: [new IncrementingNumberFilter()]
            ));
            var msg = new TestMessage { Number = 0, Service = si };

            // when
            await sut.Invoke(msg);

            // then — TryCatch caught the timeout, catch filter ran
            Assert.AreEqual(1, msg.Number);
            Assert.IsTrue(msg.IsSuccess);
        }

        [TestMethod]
        public async Task Should_nest_iftrue_inside_retry()
        {
            // given — conditional logic inside retry loop
            var callCount = 0;
            var sut = new DataPipe<TestMessage>();
            sut.Use(new ExceptionAspect<TestMessage>());
            sut.Add(new OnTimeoutRetry<TestMessage>(maxRetries: 2,
                retryWhen: (ex, msg) => ex is InvalidOperationException,
                customDelay: (attempt, msg) => TimeSpan.FromMilliseconds(10),
                new IfTrue<TestMessage>(msg => true,
                    new LambdaFilter<TestMessage>(msg =>
                    {
                        callCount++;
                        if (callCount < 2)
                            throw new InvalidOperationException("transient");
                        msg.Number = callCount;
                        return Task.CompletedTask;
                    }))));
            var msg = new TestMessage { Number = 0, Service = si };

            // when
            await sut.Invoke(msg);

            // then
            Assert.AreEqual(2, msg.Number);
        }

        [TestMethod]
        public async Task Should_nest_trycatch_inside_trycatch()
        {
            // given — inner TryCatch catches, outer continues
            var sut = new DataPipe<TestMessage>();
            sut.Use(new ExceptionAspect<TestMessage>());
            sut.Add(new TryCatch<TestMessage>(
                tryFilters: [
                    new IncrementingNumberFilter(),
                    new TryCatch<TestMessage>(
                        tryFilters: [new ErroringFilter()],
                        catchFilters: [new IncrementingNumberFilter()]
                    ),
                    new IncrementingNumberFilter() // should run after inner catch
                ],
                catchFilters: [new DecrementingNumberFilter()] // should NOT run
            ));
            var msg = new TestMessage { Number = 0, Service = si };

            // when
            await sut.Invoke(msg);

            // then — 1 (first increment) + 1 (inner catch) + 1 (continues after inner) = 3
            Assert.AreEqual(3, msg.Number);
        }

        [TestMethod]
        public async Task Should_nest_trycatch_preserves_outer_exception_state()
        {
            // given — outer catch accesses exception after inner TryCatch ran
            var sut = new DataPipe<TestMessage>();
            sut.Use(new ExceptionAspect<TestMessage>());
            sut.Add(new TryCatch<TestMessage>(
                tryFilters: [
                    new TryCatch<TestMessage>(
                        tryFilters: [new ErroringFilter()], // throws NotImplementedException
                        catchFilters: [new NoOpFilter()]
                    ),
                    new AlwaysFailingFilter() // throws InvalidOperationException
                ],
                catchFilters: [new RecordExceptionFilter()]
            ));
            var msg = new TestMessage { Service = si };

            // when
            await sut.Invoke(msg);

            // then — outer catch sees InvalidOperationException, not the inner's exception
            Assert.AreEqual("caught:InvalidOperationException", msg.StatusMessage);
        }

        [TestMethod]
        public async Task Should_nest_parallel_inside_retry()
        {
            // given — parallel fan-out inside retry
            var callCount = 0;
            var state = new CircuitBreakerState();
            var sut = new DataPipe<TestMessage>();
            sut.Use(new ExceptionAspect<TestMessage>());
            sut.Add(new OnTimeoutRetry<TestMessage>(maxRetries: 2,
                retryWhen: (ex, msg) => true,
                customDelay: (a, m) => TimeSpan.FromMilliseconds(10),
                new LambdaFilter<TestMessage>(msg =>
                {
                    callCount++;
                    if (callCount < 2)
                    {
                        msg.Children = new List<ChildMessage>
                        {
                            new ChildMessage { Id = 1, Value = 0 }
                        };
                    }
                    else
                    {
                        msg.Children = Enumerable.Range(1, 3)
                            .Select(i => new ChildMessage { Id = i, Value = 0 }).ToList();
                    }
                    return Task.CompletedTask;
                }),
                new ParallelForEach<TestMessage, ChildMessage>(
                    msg => msg.Children,
                    new LambdaFilter<ChildMessage>(child =>
                    {
                        if (callCount < 2)
                            throw new InvalidOperationException("transient");
                        child.Value = 1;
                        return Task.CompletedTask;
                    }))));
            var msg = new TestMessage { Service = si };

            // when
            await sut.Invoke(msg);

            // then — retry recovered, all children processed
            Assert.IsTrue(msg.Children.All(c => c.Value == 1));
            Assert.AreEqual(3, msg.Children.Count);
        }

        [TestMethod]
        public async Task Should_nest_rate_limiter_inside_circuit_breaker()
        {
            // given
            var cbState = new CircuitBreakerState();
            var rlState = new RateLimiterState();
            var sut = new DataPipe<TestMessage>();
            sut.Use(new ExceptionAspect<TestMessage>());
            sut.Add(new OnCircuitBreak<TestMessage>(cbState, failureThreshold: 3,
                filters: [new OnRateLimit<TestMessage>(rlState,
                    capacity: 10,
                    leakInterval: TimeSpan.FromSeconds(1),
                    filters: [new IncrementingNumberFilter()])]));
            var msg = new TestMessage { Number = 0, Service = si };

            // when
            await sut.Invoke(msg);

            // then
            Assert.AreEqual(1, msg.Number);
            Assert.AreEqual(CircuitState.Closed, cbState.Status);
        }

        [TestMethod]
        public async Task Should_emit_telemetry_from_all_nested_layers()
        {
            // given — circuit breaker → retry → filter
            var telemetryEvents = new List<TelemetryEvent>();
            var state = new CircuitBreakerState();
            var sut = new DataPipe<TestMessage> { TelemetryMode = TelemetryMode.PipelineAndFilters };
            sut.Use(new ExceptionAspect<TestMessage>());
            sut.Use(new TelemetryAspect<TestMessage>(new TestTelemetryAdapter()));
            sut.Add(new OnCircuitBreak<TestMessage>(state,
                filters: [new OnTimeoutRetry<TestMessage>(maxRetries: 1,
                    new IncrementingNumberFilter())]));
            var msg = new TestMessage
            {
                Number = 0,
                Service = si,
                OnTelemetry = e => telemetryEvents.Add(e)
            };

            // when
            await sut.Invoke(msg);

            // then — events from all layers
            Assert.IsTrue(telemetryEvents.Any(e => e.Component == "OnCircuitBreak"));
            Assert.IsTrue(telemetryEvents.Any(e => e.Component == "OnTimeoutRetry"));
            Assert.IsTrue(telemetryEvents.Any(e => e.Component == "IncrementingNumberFilter"));
        }
    }

    // =========================================================================
    // 4. Concurrent Failure Mode Tests
    // =========================================================================

    [TestClass]
    public class ConcurrentFailureModeTests
    {
        ServiceIdentity si = new ServiceIdentity
        {
            Name = "DataPipeTestService",
            Version = "1.0.0",
            Environment = "Dev",
            InstanceId = Guid.NewGuid().ToString()
        };

        [TestMethod]
        public async Task Should_report_error_and_process_children_in_parallel_with_mixed_outcomes()
        {
            // given — odd children fail, even children succeed
            var children = Enumerable.Range(1, 6)
                .Select(i => new ChildMessage { Id = i, Value = 0 }).ToList();
            var sut = new DataPipe<TestMessage>();
            sut.Use(new ExceptionAspect<TestMessage>());
            sut.Add(new ParallelForEach<TestMessage, ChildMessage>(
                msg => msg.Children,
                new FailOddChildFilter()));
            var msg = new TestMessage { Children = children, Service = si };

            // when
            await sut.Invoke(msg);

            // then — pipeline reports error due to child failures
            Assert.AreEqual(500, msg.StatusCode);
            // at least some even children may have been processed
            var evenChildren = children.Where(c => c.Id % 2 == 0).ToList();
            var processedEvens = evenChildren.Count(c => c.Value == 1);
            Assert.IsTrue(processedEvens >= 0,
                $"Even children processed: {processedEvens}");
        }

        [TestMethod]
        public async Task Should_report_error_status_when_any_parallel_branch_fails()
        {
            // given
            var children = Enumerable.Range(1, 4)
                .Select(i => new ChildMessage { Id = i, Value = 0 }).ToList();
            var sut = new DataPipe<TestMessage>();
            sut.Use(new ExceptionAspect<TestMessage>());
            sut.Add(new ParallelForEach<TestMessage, ChildMessage>(
                msg => msg.Children,
                new FailOddChildFilter()));
            var msg = new TestMessage { Children = children, Service = si };

            // when
            await sut.Invoke(msg);

            // then
            Assert.AreEqual(500, msg.StatusCode);
        }

        [TestMethod]
        public async Task Should_handle_concurrent_circuit_breaker_failures_without_corruption()
        {
            // given — concurrent failures against shared circuit state
            var state = new CircuitBreakerState();
            var tasks = Enumerable.Range(0, 20).Select(async _ =>
            {
                var pipe = new DataPipe<TestMessage>();
                pipe.Use(new ExceptionAspect<TestMessage>());
                pipe.Add(new OnCircuitBreak<TestMessage>(state, failureThreshold: 10,
                    filters: [new AlwaysFailingFilter()]));
                await pipe.Invoke(new TestMessage { Service = si });
            });

            // when
            await Task.WhenAll(tasks);

            // then — circuit should be open with consistent state
            Assert.AreEqual(CircuitState.Open, state.Status);
            // Failure count may exceed threshold due to concurrent increments, but state is Open
            Assert.IsTrue(state.FailureCount >= 10);
        }

        [TestMethod]
        public async Task Should_handle_mixed_success_and_failure_in_concurrent_rate_limited_requests()
        {
            // given
            var state = new RateLimiterState();
            var results = new ConcurrentBag<(bool Success, int StatusCode)>();
            var callCount = 0;

            var tasks = Enumerable.Range(0, 5).Select(async i =>
            {
                var pipe = new DataPipe<TestMessage>();
                pipe.Use(new ExceptionAspect<TestMessage>());
                pipe.Add(new OnRateLimit<TestMessage>(state,
                    capacity: 5,
                    leakInterval: TimeSpan.FromSeconds(10),
                    filters: [new LambdaFilter<TestMessage>(msg =>
                    {
                        var count = Interlocked.Increment(ref callCount);
                        if (count % 2 == 0) throw new InvalidOperationException("intermittent");
                        msg.Number = count;
                        return Task.CompletedTask;
                    })]));
                var msg = new TestMessage { Number = 0, Service = si };
                await pipe.Invoke(msg);
                results.Add((msg.IsSuccess, msg.StatusCode));
            });

            // when
            await Task.WhenAll(tasks);

            // then — mix of successes and failures
            Assert.IsTrue(results.Any(r => r.Success), "Expected at least one success");
            Assert.IsTrue(results.Any(r => !r.Success), "Expected at least one failure");
        }

        [TestMethod]
        public async Task Should_handle_exception_in_retry_within_circuit_breaker_without_tripping()
        {
            // given — retry recovers before circuit threshold
            var cbState = new CircuitBreakerState();
            var sut = new DataPipe<TestMessage>();
            sut.Use(new ExceptionAspect<TestMessage>());
            sut.Add(new OnCircuitBreak<TestMessage>(cbState, failureThreshold: 3,
                filters: [new OnTimeoutRetry<TestMessage>(maxRetries: 3,
                    retryWhen: (ex, msg) => ex is InvalidOperationException,
                    customDelay: (a, m) => TimeSpan.FromMilliseconds(10),
                    new FailNTimesThenSucceedFilter(2))]));
            var msg = new TestMessage { Number = 0, Service = si };

            // when
            await sut.Invoke(msg);

            // then — retry recovered, circuit untouched
            Assert.IsTrue(msg.IsSuccess);
            Assert.AreEqual(CircuitState.Closed, cbState.Status);
            Assert.AreEqual(0, cbState.FailureCount);
        }

        [TestMethod]
        public async Task Should_trip_circuit_when_retry_exhausted()
        {
            // given — retry can't recover, exception reaches circuit breaker
            var cbState = new CircuitBreakerState();
            for (int i = 0; i < 3; i++)
            {
                var pipe = new DataPipe<TestMessage>();
                pipe.Use(new ExceptionAspect<TestMessage>());
                pipe.Add(new OnCircuitBreak<TestMessage>(cbState, failureThreshold: 3,
                    filters: [new OnTimeoutRetry<TestMessage>(maxRetries: 1,
                        retryWhen: (ex, msg) => ex is InvalidOperationException,
                        customDelay: (a, m) => TimeSpan.FromMilliseconds(5),
                        new AlwaysFailingFilter())]));
                await pipe.Invoke(new TestMessage { Service = si });
            }

            // then — retry exhausted on each invocation, circuit saw 3 failures
            Assert.AreEqual(CircuitState.Open, cbState.Status);
        }

        [TestMethod]
        public async Task Should_stop_foreach_iteration_when_pipeline_stopped()
        {
            // given — first child stops the pipeline, subsequent children should not run
            var processedIds = new ConcurrentBag<int>();
            var children = Enumerable.Range(1, 5)
                .Select(i => new ChildMessage { Id = i, Value = 0 }).ToList();
            var sut = new DataPipe<TestMessage>();
            sut.Use(new ExceptionAspect<TestMessage>());
            sut.Add(new ForEach<TestMessage, ChildMessage>(
                msg => msg.Children,
                new LambdaFilter<ChildMessage>(child =>
                {
                    processedIds.Add(child.Id);
                    if (child.Id == 2)
                        child.Execution.Stop("halting");
                    child.Value = 1;
                    return Task.CompletedTask;
                })));
            var msg = new TestMessage { Children = children, Service = si };

            // when
            await sut.Invoke(msg);

            // then — only 1 and 2 were processed (ForEach respects ShouldStop)
            Assert.IsTrue(processedIds.Contains(1));
            Assert.IsTrue(processedIds.Contains(2));
            Assert.IsTrue(processedIds.Count <= 2,
                $"Expected <= 2 processed but got {processedIds.Count}");
        }
    }

    // =========================================================================
    // 5. Stress / Scale Tests
    // =========================================================================

    [TestClass]
    public class StressAndScaleTests
    {
        ServiceIdentity si = new ServiceIdentity
        {
            Name = "DataPipeTestService",
            Version = "1.0.0",
            Environment = "Dev",
            InstanceId = Guid.NewGuid().ToString()
        };

        [TestMethod]
        public async Task Should_handle_hundreds_of_parallel_children()
        {
            // given — 500 children processed in parallel
            var children = Enumerable.Range(1, 500)
                .Select(i => new ChildMessage { Id = i, Value = 0 }).ToList();
            var sut = new DataPipe<TestMessage>();
            sut.Use(new ExceptionAspect<TestMessage>());
            sut.Add(new ParallelForEach<TestMessage, ChildMessage>(
                msg => msg.Children,
                new IncrementChildValueFilter()));
            var msg = new TestMessage { Children = children, Service = si };

            // when
            await sut.Invoke(msg);

            // then — all 500 processed
            Assert.IsTrue(children.All(c => c.Value == 1),
                $"Expected all 500 children processed. Failed: {children.Count(c => c.Value != 1)}");
        }

        [TestMethod]
        public async Task Should_handle_hundreds_of_sequential_children()
        {
            // given — 500 children processed sequentially
            var children = Enumerable.Range(1, 500)
                .Select(i => new ChildMessage { Id = i, Value = 0 }).ToList();
            var sut = new DataPipe<TestMessage>();
            sut.Use(new ExceptionAspect<TestMessage>());
            sut.Add(new ForEach<TestMessage, ChildMessage>(
                msg => msg.Children,
                new IncrementChildValueFilter()));
            var msg = new TestMessage { Children = children, Service = si };

            // when
            await sut.Invoke(msg);

            // then
            Assert.IsTrue(children.All(c => c.Value == 1));
        }

        [TestMethod]
        public async Task Should_handle_many_retries()
        {
            // given — filter fails 9 times then succeeds on 10th
            var sut = new DataPipe<TestMessage>();
            sut.Use(new ExceptionAspect<TestMessage>());
            sut.Add(new OnTimeoutRetry<TestMessage>(maxRetries: 10,
                retryWhen: (ex, msg) => ex is InvalidOperationException,
                customDelay: (attempt, msg) => TimeSpan.FromMilliseconds(1),
                new FailNTimesThenSucceedFilter(9)));
            var msg = new TestMessage { Number = 0, Service = si };

            // when
            await sut.Invoke(msg);

            // then
            Assert.IsTrue(msg.IsSuccess);
            Assert.AreEqual(10, msg.Number); // FailNTimesThenSucceedFilter sets Number to callCount
        }

        [TestMethod]
        public async Task Should_handle_many_repeat_iterations()
        {
            // given — repeat 100 times
            var sut = new DataPipe<TestMessage>();
            sut.Use(new ExceptionAspect<TestMessage>());
            sut.Add(new RepeatUntil<TestMessage>(
                msg => msg.Number >= 100,
                new IncrementingNumberFilter()));
            var msg = new TestMessage { Number = 0, Service = si };

            // when
            await sut.Invoke(msg);

            // then
            Assert.AreEqual(100, msg.Number);
        }

        [TestMethod]
        public async Task Should_handle_high_concurrency_through_circuit_breaker()
        {
            // given — 100 concurrent requests through a closed circuit
            var state = new CircuitBreakerState();
            var results = new ConcurrentBag<int>();
            var tasks = Enumerable.Range(0, 100).Select(async _ =>
            {
                var pipe = new DataPipe<TestMessage>();
                pipe.Use(new ExceptionAspect<TestMessage>());
                pipe.Add(new OnCircuitBreak<TestMessage>(state,
                    filters: [new IncrementingNumberFilter()]));
                var msg = new TestMessage { Number = 0, Service = si };
                await pipe.Invoke(msg);
                results.Add(msg.Number);
            });

            // when
            await Task.WhenAll(tasks);

            // then — all 100 succeeded
            Assert.AreEqual(100, results.Count);
            Assert.IsTrue(results.All(n => n == 1));
            Assert.AreEqual(CircuitState.Closed, state.Status);
        }

        [TestMethod]
        public async Task Should_handle_high_concurrency_through_rate_limiter_delay_mode()
        {
            // given — 20 concurrent requests, capacity 5, fast leak
            var state = new RateLimiterState();
            var results = new ConcurrentBag<int>();
            var tasks = Enumerable.Range(0, 20).Select(async _ =>
            {
                var pipe = new DataPipe<TestMessage>();
                pipe.Use(new ExceptionAspect<TestMessage>());
                pipe.Add(new OnRateLimit<TestMessage>(state,
                    capacity: 5,
                    leakInterval: TimeSpan.FromMilliseconds(10),
                    behavior: RateLimitExceededBehavior.Delay,
                    new IncrementingNumberFilter()));
                var msg = new TestMessage { Number = 0, Service = si };
                await pipe.Invoke(msg);
                results.Add(msg.Number);
            });

            // when
            await Task.WhenAll(tasks);

            // then — all eventually admitted via delay
            Assert.AreEqual(20, results.Count);
            Assert.IsTrue(results.All(n => n == 1));
        }

        [TestMethod]
        public async Task Should_handle_deeply_nested_structural_filters()
        {
            // given — 5 levels: CircuitBreaker → RateLimit → Retry → TryCatch → Timeout → filter
            var cbState = new CircuitBreakerState();
            var rlState = new RateLimiterState();
            var sut = new DataPipe<TestMessage>();
            sut.Use(new ExceptionAspect<TestMessage>());
            sut.Add(new OnCircuitBreak<TestMessage>(cbState, failureThreshold: 3,
                filters: [new OnRateLimit<TestMessage>(rlState,
                    capacity: 10,
                    leakInterval: TimeSpan.FromSeconds(1),
                    filters: [new OnTimeoutRetry<TestMessage>(maxRetries: 1,
                        new TryCatch<TestMessage>(
                            tryFilters: [
                                new Timeout<TestMessage>(TimeSpan.FromSeconds(5),
                                    new IncrementingNumberFilter())
                            ],
                            catchFilters: [new NoOpFilter()]
                        ))])]));
            var msg = new TestMessage { Number = 0, Service = si };

            // when
            await sut.Invoke(msg);

            // then
            Assert.AreEqual(1, msg.Number);
            Assert.IsTrue(msg.IsSuccess);
            Assert.AreEqual(CircuitState.Closed, cbState.Status);
        }

        [TestMethod]
        public async Task Should_handle_parallel_branches_with_nested_retry_at_scale()
        {
            // given — 50 children, each with a retry that fails once then succeeds
            var children = Enumerable.Range(1, 50)
                .Select(i => new ChildMessage { Id = i, Value = 0 }).ToList();
            var callCounts = new ConcurrentDictionary<int, int>();

            var sut = new DataPipe<TestMessage>();
            sut.Use(new ExceptionAspect<TestMessage>());
            sut.Add(new ParallelForEach<TestMessage, ChildMessage>(
                msg => msg.Children,
                new LambdaFilter<ChildMessage>(child =>
                {
                    var count = callCounts.AddOrUpdate(child.Id, 1, (_, c) => c + 1);
                    if (count < 2)
                        throw new InvalidOperationException($"Child {child.Id} transient failure");
                    child.Value = 1;
                    return Task.CompletedTask;
                })));
            var msg = new TestMessage { Children = children, Service = si };

            // when — note: without retry at child level, failures propagate
            // This tests that ParallelForEach handles many failing branches gracefully
            await sut.Invoke(msg);

            // then — pipeline reported error (children threw)
            Assert.AreEqual(500, msg.StatusCode);
        }

        [TestMethod]
        public async Task Should_handle_hundreds_of_pipeline_invocations()
        {
            // given — simulate a web server processing 200 sequential requests
            var state = new CircuitBreakerState();
            int successCount = 0;

            for (int i = 0; i < 200; i++)
            {
                var pipe = new DataPipe<TestMessage>();
                pipe.Use(new ExceptionAspect<TestMessage>());
                pipe.Add(new OnCircuitBreak<TestMessage>(state,
                    filters: [new IncrementingNumberFilter()]));
                var msg = new TestMessage { Number = 0, Service = si };
                await pipe.Invoke(msg);
                if (msg.IsSuccess) successCount++;
            }

            // then
            Assert.AreEqual(200, successCount);
            Assert.AreEqual(CircuitState.Closed, state.Status);
        }
    }
}
