using DataPipe.Core;
using DataPipe.Core.Filters;
using DataPipe.Core.Middleware;
using DataPipe.Core.Telemetry;
using DataPipe.Core.Telemetry.Adapters;
using DataPipe.Tests.Support;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace DataPipe.Tests
{
    [TestClass]
    public class IfTrueElseTests
    {
        ServiceIdentity si = new ServiceIdentity
        {
            Name = "DataPipeTestService",
            Version = "1.0.0",
            Environment = "Dev",
            InstanceId = Guid.NewGuid().ToString()
        };

        [TestMethod]
        public async Task Should_execute_then_filters_when_condition_is_true()
        {
            // given
            var sut = new DataPipe<TestMessage>();
            sut.Use(new ExceptionAspect<TestMessage>());
            sut.Add(new IfTrueElse<TestMessage>(
                msg => true,
                thenFilters: [new IncrementingNumberFilter()],
                elseFilters: [new DecrementingNumberFilter()]
            ));
            var msg = new TestMessage { Number = 0, Service = si };

            // when
            await sut.Invoke(msg);

            // then
            Assert.AreEqual(1, msg.Number);
        }

        [TestMethod]
        public async Task Should_execute_else_filters_when_condition_is_false()
        {
            // given
            var sut = new DataPipe<TestMessage>();
            sut.Use(new ExceptionAspect<TestMessage>());
            sut.Add(new IfTrueElse<TestMessage>(
                msg => false,
                thenFilters: [new IncrementingNumberFilter()],
                elseFilters: [new DecrementingNumberFilter()]
            ));
            var msg = new TestMessage { Number = 0, Service = si };

            // when
            await sut.Invoke(msg);

            // then
            Assert.AreEqual(-1, msg.Number);
        }

        [TestMethod]
        public async Task Should_execute_multiple_then_filters_when_condition_is_true()
        {
            // given
            var sut = new DataPipe<TestMessage>();
            sut.Use(new ExceptionAspect<TestMessage>());
            sut.Add(new IfTrueElse<TestMessage>(
                msg => true,
                thenFilters: [new IncrementingNumberFilter(), new IncrementingNumberFilter(), new IncrementingNumberFilter()],
                elseFilters: [new DecrementingNumberFilter()]
            ));
            var msg = new TestMessage { Number = 0, Service = si };

            // when
            await sut.Invoke(msg);

            // then
            Assert.AreEqual(3, msg.Number);
        }

        [TestMethod]
        public async Task Should_execute_multiple_else_filters_when_condition_is_false()
        {
            // given
            var sut = new DataPipe<TestMessage>();
            sut.Use(new ExceptionAspect<TestMessage>());
            sut.Add(new IfTrueElse<TestMessage>(
                msg => false,
                thenFilters: [new IncrementingNumberFilter()],
                elseFilters: [new DecrementingNumberFilter(), new DecrementingNumberFilter(), new DecrementingNumberFilter()]
            ));
            var msg = new TestMessage { Number = 0, Service = si };

            // when
            await sut.Invoke(msg);

            // then
            Assert.AreEqual(-3, msg.Number);
        }

        [TestMethod]
        public async Task Should_evaluate_predicate_based_on_message_state()
        {
            // given
            var sut = new DataPipe<TestMessage>();
            sut.Use(new ExceptionAspect<TestMessage>());
            sut.Add(new IfTrueElse<TestMessage>(
                msg => msg.Number > 0,
                thenFilters: [new IncrementingNumberFilter()],
                elseFilters: [new DecrementingNumberFilter()]
            ));
            var msg1 = new TestMessage { Number = 5, Service = si };
            var msg2 = new TestMessage { Number = -5, Service = si };

            // when
            await sut.Invoke(msg1);
            await sut.Invoke(msg2);

            // then
            Assert.AreEqual(6, msg1.Number);
            Assert.AreEqual(-6, msg2.Number);
        }

        [TestMethod]
        public async Task Should_continue_pipeline_after_then_branch_executes()
        {
            // given
            var sut = new DataPipe<TestMessage>();
            sut.Use(new ExceptionAspect<TestMessage>());
            sut.Add(new IfTrueElse<TestMessage>(
                msg => true,
                thenFilters: [new IncrementingNumberFilter()],
                elseFilters: [new DecrementingNumberFilter()]
            ));
            sut.Add(new IncrementingNumberFilter()); // runs after branch
            var msg = new TestMessage { Number = 0, Service = si };

            // when
            await sut.Invoke(msg);

            // then
            Assert.AreEqual(2, msg.Number);
        }

        [TestMethod]
        public async Task Should_continue_pipeline_after_else_branch_executes()
        {
            // given
            var sut = new DataPipe<TestMessage>();
            sut.Use(new ExceptionAspect<TestMessage>());
            sut.Add(new IfTrueElse<TestMessage>(
                msg => false,
                thenFilters: [new IncrementingNumberFilter()],
                elseFilters: [new DecrementingNumberFilter()]
            ));
            sut.Add(new IncrementingNumberFilter()); // runs after branch
            var msg = new TestMessage { Number = 0, Service = si };

            // when
            await sut.Invoke(msg);

            // then
            Assert.AreEqual(0, msg.Number); // -1 + 1
        }

        [TestMethod]
        public async Task Should_propagate_exception_from_then_branch()
        {
            // given
            var sut = new DataPipe<TestMessage>();
            sut.Add(new IfTrueElse<TestMessage>(
                msg => true,
                thenFilters: [new ErroringFilter()],
                elseFilters: [new NoOpFilter()]
            ));
            var msg = new TestMessage { Service = si };

            // when / then
            await Assert.ThrowsExceptionAsync<NotImplementedException>(async () => await sut.Invoke(msg));
        }

        [TestMethod]
        public async Task Should_emit_telemetry_with_condition_and_branch_attributes()
        {
            // given
            var telemetryEvents = new List<TelemetryEvent>();
            var sut = new DataPipe<TestMessage> { TelemetryMode = TelemetryMode.PipelineAndFilters };
            sut.Use(new ExceptionAspect<TestMessage>());
            sut.Use(new TelemetryAspect<TestMessage>(new TestTelemetryAdapter()));
            sut.Add(new IfTrueElse<TestMessage>(
                msg => true,
                thenFilters: [new NoOpFilter()],
                elseFilters: [new NoOpFilter()]
            ));
            var msg = new TestMessage { OnTelemetry = (e) => telemetryEvents.Add(e), Service = si };

            // when
            await sut.Invoke(msg);

            // then
            var startEvent = telemetryEvents.FirstOrDefault(e =>
                e.Component == "IfTrueElse" && e.Phase == TelemetryPhase.Start);
            Assert.IsNotNull(startEvent);
            Assert.AreEqual(true, startEvent.Attributes["condition"]);
            Assert.AreEqual("then", startEvent.Attributes["branch"]);
        }
    }

    [TestClass]
    public class TimeoutTests
    {
        ServiceIdentity si = new ServiceIdentity
        {
            Name = "DataPipeTestService",
            Version = "1.0.0",
            Environment = "Dev",
            InstanceId = Guid.NewGuid().ToString()
        };

        [TestMethod]
        public async Task Should_execute_filters_that_complete_within_timeout()
        {
            // given
            var sut = new DataPipe<TestMessage>();
            sut.Use(new ExceptionAspect<TestMessage>());
            sut.Add(new Timeout<TestMessage>(TimeSpan.FromSeconds(5),
                new IncrementingNumberFilter()));
            var msg = new TestMessage { Number = 0, Service = si };

            // when
            await sut.Invoke(msg);

            // then
            Assert.AreEqual(1, msg.Number);
            Assert.IsTrue(msg.IsSuccess);
        }

        [TestMethod]
        public async Task Should_throw_timeout_exception_when_filters_exceed_timeout()
        {
            // given
            var sut = new DataPipe<TestMessage>();
            sut.Add(new Timeout<TestMessage>(TimeSpan.FromMilliseconds(50),
                new SlowFilter(5000)));
            var msg = new TestMessage { Number = 0, Service = si };

            // when / then
            await Assert.ThrowsExceptionAsync<TimeoutException>(async () => await sut.Invoke(msg));
        }

        [TestMethod]
        public async Task Should_set_error_status_when_timeout_exception_caught_by_exception_aspect()
        {
            // given
            var sut = new DataPipe<TestMessage>();
            sut.Use(new ExceptionAspect<TestMessage>());
            sut.Add(new Timeout<TestMessage>(TimeSpan.FromMilliseconds(50),
                new SlowFilter(5000)));
            var msg = new TestMessage { Number = 0, Service = si };

            // when
            await sut.Invoke(msg);

            // then
            Assert.AreEqual(500, msg.StatusCode);
        }

        [TestMethod]
        public async Task Should_not_execute_subsequent_filters_in_timeout_scope_after_timeout()
        {
            // given
            var sut = new DataPipe<TestMessage>();
            sut.Use(new ExceptionAspect<TestMessage>());
            sut.Add(new Timeout<TestMessage>(TimeSpan.FromMilliseconds(50),
                new SlowFilter(5000),
                new IncrementingNumberFilter())); // should not run
            var msg = new TestMessage { Number = 0, Service = si };

            // when
            await sut.Invoke(msg);

            // then
            Assert.AreEqual(0, msg.Number); // neither filter completed
        }

        [TestMethod]
        public async Task Should_restore_original_cancellation_token_after_timeout()
        {
            // given
            using var cts = new CancellationTokenSource();
            var originalToken = cts.Token;
            var sut = new DataPipe<TestMessage>();
            sut.Use(new ExceptionAspect<TestMessage>());
            sut.Add(new Timeout<TestMessage>(TimeSpan.FromMilliseconds(50),
                new SlowFilter(5000)));
            var msg = new TestMessage { Number = 0, Service = si, CancellationToken = originalToken };

            // when
            await sut.Invoke(msg);

            // then — original token should be restored
            Assert.AreEqual(originalToken, msg.CancellationToken);
        }

        [TestMethod]
        public async Task Should_restore_original_cancellation_token_on_success()
        {
            // given
            using var cts = new CancellationTokenSource();
            var originalToken = cts.Token;
            var sut = new DataPipe<TestMessage>();
            sut.Use(new ExceptionAspect<TestMessage>());
            sut.Add(new Timeout<TestMessage>(TimeSpan.FromSeconds(5),
                new IncrementingNumberFilter()));
            var msg = new TestMessage { Number = 0, Service = si, CancellationToken = originalToken };

            // when
            await sut.Invoke(msg);

            // then
            Assert.AreEqual(originalToken, msg.CancellationToken);
            Assert.AreEqual(1, msg.Number);
        }

        [TestMethod]
        public async Task Should_not_convert_external_cancellation_to_timeout_exception()
        {
            // given — cancel the token shortly after execution starts so the SlowFilter
            // sees the original token cancelled via the linked source
            using var cts = new CancellationTokenSource(millisecondsDelay: 20);
            var sut = new DataPipe<TestMessage>();
            sut.Use(new ExceptionAspect<TestMessage>());
            sut.Add(new Timeout<TestMessage>(TimeSpan.FromSeconds(30),
                new SlowFilter(5000)));
            var msg = new TestMessage { Number = 0, Service = si, CancellationToken = cts.Token };

            // when
            await sut.Invoke(msg);

            // then — cancellation is NOT converted to TimeoutException (500 not from timeout)
            // The original token is restored after execution
            Assert.AreEqual(cts.Token, msg.CancellationToken);
            Assert.AreEqual(0, msg.Number); // SlowFilter did not complete
        }

        [TestMethod]
        public async Task Should_execute_multiple_filters_within_timeout()
        {
            // given
            var sut = new DataPipe<TestMessage>();
            sut.Use(new ExceptionAspect<TestMessage>());
            sut.Add(new Timeout<TestMessage>(TimeSpan.FromSeconds(5),
                new IncrementingNumberFilter(),
                new IncrementingNumberFilter(),
                new IncrementingNumberFilter()));
            var msg = new TestMessage { Number = 0, Service = si };

            // when
            await sut.Invoke(msg);

            // then
            Assert.AreEqual(3, msg.Number);
        }

        [TestMethod]
        public async Task Should_continue_pipeline_after_successful_timeout_scope()
        {
            // given
            var sut = new DataPipe<TestMessage>();
            sut.Use(new ExceptionAspect<TestMessage>());
            sut.Add(new Timeout<TestMessage>(TimeSpan.FromSeconds(5),
                new IncrementingNumberFilter()));
            sut.Add(new IncrementingNumberFilter()); // runs after timeout scope
            var msg = new TestMessage { Number = 0, Service = si };

            // when
            await sut.Invoke(msg);

            // then
            Assert.AreEqual(2, msg.Number);
        }

        [TestMethod]
        public async Task Should_emit_telemetry_with_timeout_duration_and_timed_out_attribute()
        {
            // given
            var telemetryEvents = new List<TelemetryEvent>();
            var sut = new DataPipe<TestMessage> { TelemetryMode = TelemetryMode.PipelineAndFilters };
            sut.Use(new ExceptionAspect<TestMessage>());
            sut.Use(new TelemetryAspect<TestMessage>(new TestTelemetryAdapter()));
            sut.Add(new Timeout<TestMessage>(TimeSpan.FromSeconds(5),
                new IncrementingNumberFilter()));
            var msg = new TestMessage { OnTelemetry = (e) => telemetryEvents.Add(e), Service = si };

            // when
            await sut.Invoke(msg);

            // then
            var startEvent = telemetryEvents.FirstOrDefault(e =>
                e.Component == "Timeout" && e.Phase == TelemetryPhase.Start);
            Assert.IsNotNull(startEvent);
            Assert.AreEqual(5000L, startEvent.Attributes["timeout-ms"]);

            var endEvent = telemetryEvents.FirstOrDefault(e =>
                e.Component == "Timeout" && e.Phase == TelemetryPhase.End);
            Assert.IsNotNull(endEvent);
            Assert.AreEqual(false, endEvent.Attributes["timed-out"]);
        }
    }

    [TestClass]
    public class TryCatchTests
    {
        ServiceIdentity si = new ServiceIdentity
        {
            Name = "DataPipeTestService",
            Version = "1.0.0",
            Environment = "Dev",
            InstanceId = Guid.NewGuid().ToString()
        };

        [TestMethod]
        public async Task Should_execute_try_filters_when_no_exception()
        {
            // given
            var sut = new DataPipe<TestMessage>();
            sut.Use(new ExceptionAspect<TestMessage>());
            sut.Add(new TryCatch<TestMessage>(
                tryFilters: [new IncrementingNumberFilter()],
                catchFilters: [new DecrementingNumberFilter()]
            ));
            var msg = new TestMessage { Number = 0, Service = si };

            // when
            await sut.Invoke(msg);

            // then
            Assert.AreEqual(1, msg.Number);
        }

        [TestMethod]
        public async Task Should_not_execute_catch_filters_when_no_exception()
        {
            // given
            var sut = new DataPipe<TestMessage>();
            sut.Use(new ExceptionAspect<TestMessage>());
            sut.Add(new TryCatch<TestMessage>(
                tryFilters: [new IncrementingNumberFilter()],
                catchFilters: [new DecrementingNumberFilter(), new DecrementingNumberFilter()]
            ));
            var msg = new TestMessage { Number = 10, Service = si };

            // when
            await sut.Invoke(msg);

            // then
            Assert.AreEqual(11, msg.Number); // only incremented, not decremented
        }

        [TestMethod]
        public async Task Should_execute_catch_filters_when_try_throws()
        {
            // given
            var sut = new DataPipe<TestMessage>();
            sut.Use(new ExceptionAspect<TestMessage>());
            sut.Add(new TryCatch<TestMessage>(
                tryFilters: [new ErroringFilter()],
                catchFilters: [new IncrementingNumberFilter()]
            ));
            var msg = new TestMessage { Number = 0, Service = si };

            // when
            await sut.Invoke(msg);

            // then
            Assert.AreEqual(1, msg.Number);
            Assert.IsTrue(msg.IsSuccess); // pipeline continues normally
        }

        [TestMethod]
        public async Task Should_continue_pipeline_after_catch_branch()
        {
            // given
            var sut = new DataPipe<TestMessage>();
            sut.Use(new ExceptionAspect<TestMessage>());
            sut.Add(new TryCatch<TestMessage>(
                tryFilters: [new ErroringFilter()],
                catchFilters: [new IncrementingNumberFilter()]
            ));
            sut.Add(new IncrementingNumberFilter()); // should run after TryCatch
            var msg = new TestMessage { Number = 0, Service = si };

            // when
            await sut.Invoke(msg);

            // then
            Assert.AreEqual(2, msg.Number);
        }

        [TestMethod]
        public async Task Should_make_caught_exception_available_via_state()
        {
            // given
            var sut = new DataPipe<TestMessage>();
            sut.Use(new ExceptionAspect<TestMessage>());
            sut.Add(new TryCatch<TestMessage>(
                tryFilters: [new ErroringFilter()],
                catchFilters: [new RecordExceptionFilter()]
            ));
            var msg = new TestMessage { Service = si };

            // when
            await sut.Invoke(msg);

            // then
            Assert.AreEqual("caught:NotImplementedException", msg.StatusMessage);
        }

        [TestMethod]
        public async Task Should_remove_exception_from_state_after_catch_filters_complete()
        {
            // given
            var sut = new DataPipe<TestMessage>();
            sut.Use(new ExceptionAspect<TestMessage>());
            sut.Add(new TryCatch<TestMessage>(
                tryFilters: [new ErroringFilter()],
                catchFilters: [new NoOpFilter()]
            ));
            sut.Add(new LambdaFilter<TestMessage>(msg =>
            {
                msg.Number = msg.State.Has("TryCatch.Exception") ? 999 : 0;
                return Task.CompletedTask;
            }));
            var msg = new TestMessage { Number = -1, Service = si };

            // when
            await sut.Invoke(msg);

            // then
            Assert.AreEqual(0, msg.Number); // exception was removed from state
        }

        [TestMethod]
        public async Task Should_catch_only_matching_exceptions_when_catchWhen_provided()
        {
            // given
            var sut = new DataPipe<TestMessage>();
            sut.Use(new ExceptionAspect<TestMessage>());
            sut.Add(new TryCatch<TestMessage>(
                tryFilters: [new AlwaysFailingFilter()], // throws InvalidOperationException
                catchFilters: [new IncrementingNumberFilter()],
                catchWhen: (ex, msg) => ex is InvalidOperationException
            ));
            var msg = new TestMessage { Number = 0, Service = si };

            // when
            await sut.Invoke(msg);

            // then
            Assert.AreEqual(1, msg.Number); // catch ran
            Assert.IsTrue(msg.IsSuccess);
        }

        [TestMethod]
        public async Task Should_propagate_exception_when_catchWhen_does_not_match()
        {
            // given
            var sut = new DataPipe<TestMessage>();
            sut.Add(new TryCatch<TestMessage>(
                tryFilters: [new ErroringFilter()], // throws NotImplementedException
                catchFilters: [new IncrementingNumberFilter()],
                catchWhen: (ex, msg) => ex is InvalidOperationException // won't match
            ));
            var msg = new TestMessage { Number = 0, Service = si };

            // when / then
            await Assert.ThrowsExceptionAsync<NotImplementedException>(async () => await sut.Invoke(msg));
            Assert.AreEqual(0, msg.Number); // catch filters did not run
        }

        [TestMethod]
        public async Task Should_propagate_exception_from_catch_filters()
        {
            // given
            var sut = new DataPipe<TestMessage>();
            sut.Add(new TryCatch<TestMessage>(
                tryFilters: [new AlwaysFailingFilter()],
                catchFilters: [new ErroringFilter()] // catch itself throws
            ));
            var msg = new TestMessage { Service = si };

            // when / then — the NotImplementedException from ErroringFilter propagates
            await Assert.ThrowsExceptionAsync<NotImplementedException>(async () => await sut.Invoke(msg));
        }

        [TestMethod]
        public async Task Should_execute_multiple_catch_filters_in_order()
        {
            // given
            var sut = new DataPipe<TestMessage>();
            sut.Use(new ExceptionAspect<TestMessage>());
            sut.Add(new TryCatch<TestMessage>(
                tryFilters: [new ErroringFilter()],
                catchFilters: [
                    new IncrementingNumberFilter(),
                    new IncrementingNumberFilter(),
                    new IncrementingNumberFilter()
                ]
            ));
            var msg = new TestMessage { Number = 0, Service = si };

            // when
            await sut.Invoke(msg);

            // then
            Assert.AreEqual(3, msg.Number);
        }

        [TestMethod]
        public async Task Should_emit_telemetry_with_caught_exception_attribute()
        {
            // given
            var telemetryEvents = new List<TelemetryEvent>();
            var sut = new DataPipe<TestMessage> { TelemetryMode = TelemetryMode.PipelineAndFilters };
            sut.Use(new ExceptionAspect<TestMessage>());
            sut.Use(new TelemetryAspect<TestMessage>(new TestTelemetryAdapter()));
            sut.Add(new TryCatch<TestMessage>(
                tryFilters: [new ErroringFilter()],
                catchFilters: [new NoOpFilter()]
            ));
            var msg = new TestMessage { OnTelemetry = (e) => telemetryEvents.Add(e), Service = si };

            // when
            await sut.Invoke(msg);

            // then
            var endEvent = telemetryEvents.FirstOrDefault(e =>
                e.Component == "TryCatch" && e.Phase == TelemetryPhase.End);
            Assert.IsNotNull(endEvent);
            Assert.AreEqual(true, endEvent.Attributes["caught-exception"]);
        }

        [TestMethod]
        public async Task Should_emit_telemetry_with_no_caught_exception_when_try_succeeds()
        {
            // given
            var telemetryEvents = new List<TelemetryEvent>();
            var sut = new DataPipe<TestMessage> { TelemetryMode = TelemetryMode.PipelineAndFilters };
            sut.Use(new ExceptionAspect<TestMessage>());
            sut.Use(new TelemetryAspect<TestMessage>(new TestTelemetryAdapter()));
            sut.Add(new TryCatch<TestMessage>(
                tryFilters: [new NoOpFilter()],
                catchFilters: [new NoOpFilter()]
            ));
            var msg = new TestMessage { OnTelemetry = (e) => telemetryEvents.Add(e), Service = si };

            // when
            await sut.Invoke(msg);

            // then
            var endEvent = telemetryEvents.FirstOrDefault(e =>
                e.Component == "TryCatch" && e.Phase == TelemetryPhase.End);
            Assert.IsNotNull(endEvent);
            Assert.AreEqual(false, endEvent.Attributes["caught-exception"]);
        }

        [TestMethod]
        public async Task Should_work_with_timeout_inside_try_catching_timeout_exception()
        {
            // given
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

            // then
            Assert.AreEqual(1, msg.Number); // timeout caught, fallback ran
            Assert.IsTrue(msg.IsSuccess);
        }
    }

    [TestClass]
    public class DelayExecutionTests
    {
        ServiceIdentity si = new ServiceIdentity
        {
            Name = "DataPipeTestService",
            Version = "1.0.0",
            Environment = "Dev",
            InstanceId = Guid.NewGuid().ToString()
        };

        [TestMethod]
        public async Task Should_execute_child_filters_after_delay()
        {
            // given
            var sut = new DataPipe<TestMessage>();
            sut.Use(new ExceptionAspect<TestMessage>());
            sut.Add(new DelayExecution<TestMessage>(TimeSpan.FromMilliseconds(50),
                new IncrementingNumberFilter()));
            var msg = new TestMessage { Number = 0, Service = si };

            // when
            await sut.Invoke(msg);

            // then
            Assert.AreEqual(1, msg.Number);
        }

        [TestMethod]
        public async Task Should_introduce_measurable_delay()
        {
            // given
            var sut = new DataPipe<TestMessage>();
            sut.Use(new ExceptionAspect<TestMessage>());
            sut.Add(new DelayExecution<TestMessage>(TimeSpan.FromMilliseconds(100),
                new IncrementingNumberFilter()));
            var msg = new TestMessage { Number = 0, Service = si };
            var sw = System.Diagnostics.Stopwatch.StartNew();

            // when
            await sut.Invoke(msg);
            sw.Stop();

            // then
            Assert.IsTrue(sw.ElapsedMilliseconds >= 80, $"Expected >= 80ms but was {sw.ElapsedMilliseconds}ms");
            Assert.AreEqual(1, msg.Number);
        }

        [TestMethod]
        public async Task Should_execute_multiple_child_filters_in_order()
        {
            // given
            var sut = new DataPipe<TestMessage>();
            sut.Use(new ExceptionAspect<TestMessage>());
            sut.Add(new DelayExecution<TestMessage>(TimeSpan.FromMilliseconds(10),
                new IncrementingNumberFilter(),
                new IncrementingNumberFilter(),
                new IncrementingNumberFilter()));
            var msg = new TestMessage { Number = 0, Service = si };

            // when
            await sut.Invoke(msg);

            // then
            Assert.AreEqual(3, msg.Number);
        }

        [TestMethod]
        public async Task Should_work_with_no_child_filters_as_standalone_pause()
        {
            // given
            var sut = new DataPipe<TestMessage>();
            sut.Use(new ExceptionAspect<TestMessage>());
            sut.Add(new DelayExecution<TestMessage>(TimeSpan.FromMilliseconds(50)));
            sut.Add(new IncrementingNumberFilter());
            var msg = new TestMessage { Number = 0, Service = si };

            // when
            await sut.Invoke(msg);

            // then
            Assert.AreEqual(1, msg.Number);
            Assert.IsTrue(msg.IsSuccess);
        }

        [TestMethod]
        public async Task Should_be_cancellable_via_cancellation_token()
        {
            // given
            using var cts = new CancellationTokenSource(millisecondsDelay: 20);
            var sut = new DataPipe<TestMessage>();
            sut.Use(new ExceptionAspect<TestMessage>());
            sut.Add(new DelayExecution<TestMessage>(TimeSpan.FromSeconds(30),
                new IncrementingNumberFilter()));
            var msg = new TestMessage { Number = 0, Service = si, CancellationToken = cts.Token };

            // when
            await sut.Invoke(msg);

            // then — cancellation during delay prevents child filters from running
            Assert.AreEqual(0, msg.Number);
            Assert.AreEqual(500, msg.StatusCode);
        }

        [TestMethod]
        public async Task Should_propagate_exception_from_child_filters()
        {
            // given
            var sut = new DataPipe<TestMessage>();
            sut.Add(new DelayExecution<TestMessage>(TimeSpan.FromMilliseconds(10),
                new ErroringFilter()));
            var msg = new TestMessage { Service = si };

            // when / then
            await Assert.ThrowsExceptionAsync<NotImplementedException>(async () => await sut.Invoke(msg));
        }

        [TestMethod]
        public async Task Should_continue_pipeline_after_delay_and_child_filters()
        {
            // given
            var sut = new DataPipe<TestMessage>();
            sut.Use(new ExceptionAspect<TestMessage>());
            sut.Add(new DelayExecution<TestMessage>(TimeSpan.FromMilliseconds(10),
                new IncrementingNumberFilter()));
            sut.Add(new IncrementingNumberFilter()); // runs after delay scope
            var msg = new TestMessage { Number = 0, Service = si };

            // when
            await sut.Invoke(msg);

            // then
            Assert.AreEqual(2, msg.Number);
        }

        [TestMethod]
        public async Task Should_work_inside_repeat_loop_for_throttling()
        {
            // given
            var sut = new DataPipe<TestMessage>();
            sut.Use(new ExceptionAspect<TestMessage>());
            sut.Add(new RepeatUntil<TestMessage>(
                msg => msg.Number >= 3,
                new DelayExecution<TestMessage>(TimeSpan.FromMilliseconds(10)),
                new IncrementingNumberFilter()));
            var msg = new TestMessage { Number = 0, Service = si };
            var sw = System.Diagnostics.Stopwatch.StartNew();

            // when
            await sut.Invoke(msg);
            sw.Stop();

            // then — 3 iterations × 10ms delay each
            Assert.AreEqual(3, msg.Number);
            Assert.IsTrue(sw.ElapsedMilliseconds >= 20, $"Expected >= 20ms but was {sw.ElapsedMilliseconds}ms");
        }
    }
}
