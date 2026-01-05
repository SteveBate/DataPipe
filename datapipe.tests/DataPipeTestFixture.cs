using DataPipe.Core;
using DataPipe.Core.Filters;
using DataPipe.Core.Middleware;
using DataPipe.Core.Telemetry;
using DataPipe.Core.Telemetry.Adapters;
using DataPipe.Core.Telemetry.Policies;
using DataPipe.Sql.Filters;
using DataPipe.Tests.Support;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;

namespace DataPipe.Tests
{
    [TestClass]
    public class DataPipeTestFixture
    {
        ServiceIdentity si = new ServiceIdentity
        {
            Name = "DataPipeTestService",
            Version = "1.0.0",
            Environment = "Dev",
            InstanceId = Guid.NewGuid().ToString()
        };

        [TestMethod]
        public void Should_arrange_aspects_in_correct_execution_order()
        {
            // given
            var sut = new DataPipe<TestMessage>();
            sut.Use(new ExceptionAspect<TestMessage>());
            sut.Use(new BasicConsoleLoggingAspect<TestMessage>(nameof(Should_arrange_aspects_in_correct_execution_order)));
            sut.Add(async m =>
            {
                await Task.Delay(0);
            });

            // when
            var result = sut.ToString();

            // then
            Assert.AreEqual("ExceptionAspect`1 -> BasicConsoleLoggingAspect`1 -> DefaultAspect -> LambdaFilter`1", result);
        }

        [TestMethod]
        public async Task Should_execute_lambda_filters_indistinguishably_from_concrete_filters()
        {
            // given
            var sut = new DataPipe<TestMessage>();
            sut.Use(new ExceptionAspect<TestMessage>());
            sut.Use(new BasicConsoleLoggingAspect<TestMessage>(nameof(Should_execute_lambda_filters_indistinguishably_from_concrete_filters)));
            sut.Add(new LambdaFilter<TestMessage>(async m => { m.Number += 1; }));
            sut.Add(async m => { m.Number += 1; });
            var msg = new TestMessage { Number = 0 };

            // when
            await sut.Invoke(msg);

            // then
            Assert.AreEqual(2, msg.Number);
        }

        [TestMethod]
        public async Task Should_notify_when_started()
        {
            // given
            var sut = new DataPipe<TestMessage>();
            sut.Use(new ExceptionAspect<TestMessage>());
            sut.Use(new BasicConsoleLoggingAspect<TestMessage>(nameof(Should_notify_when_started)));
            var msg = new TestMessage { OnStart = (m) => m.StatusMessage = "started" };

            // when
            await sut.Invoke(msg);

            // then
            Assert.AreEqual("started", msg.StatusMessage);
        }

        [TestMethod]
        public async Task Should_notify_when_successful()
        {
            // given
            var sut = new DataPipe<TestMessage>();
            sut.Use(new ExceptionAspect<TestMessage>());
            sut.Use(new BasicConsoleLoggingAspect<TestMessage>(nameof(Should_notify_when_successful)));
            var msg = new TestMessage { OnSuccess = (m) => m.StatusMessage = "success" };

            // when
            await sut.Invoke(msg);

            // then
            Assert.AreEqual("success", msg.StatusMessage);
        }

        [TestMethod]
        public async Task Should_notify_when_complete()
        {
            // given
            var sut = new DataPipe<TestMessage>();
            sut.Use(new ExceptionAspect<TestMessage>());
            sut.Use(new BasicConsoleLoggingAspect<TestMessage>(nameof(Should_notify_when_complete)));
            var msg = new TestMessage { OnComplete = (m) => m.StatusMessage = "complete" };

            // when
            await sut.Invoke(msg);

            // then
            Assert.AreEqual("complete", msg.StatusMessage);
        }

        [TestMethod]
        public async Task Should_notify_error_when_using_exception_middleware()
        {
            // given
            var sut = new DataPipe<TestMessage>();
            sut.Use(new ExceptionAspect<TestMessage>());
            sut.Use(new BasicConsoleLoggingAspect<TestMessage>(nameof(Should_notify_error_when_using_exception_middleware)));
            sut.Add(new ErroringFilter());
            var msg = new TestMessage { OnError = (m, e) => m.StatusMessage = "error" };

            // when
            await sut.Invoke(msg);

            // then
            Assert.AreEqual("error", msg.StatusMessage);
            Assert.AreEqual(500, msg.StatusCode);
        }

        [TestMethod]
        public async Task Should_allow_pipeline_to_fail_without_exception_aspect()
        {
            // given
            var sut = new DataPipe<TestMessage>();
            sut.Use(new BasicConsoleLoggingAspect<TestMessage>(nameof(Should_allow_pipeline_to_fail_without_exception_aspect)));
            sut.Add(new ErroringFilter());
            var msg = new TestMessage();

            // when
            await Assert.ThrowsExceptionAsync<NotImplementedException>(async () => await sut.Invoke(msg));
        }

        /// Filters can be composed inline to provide more complex functionality
        /// that affects only the filters within the current Add statement
        [TestMethod]
        public async Task Should_retry_when_using_locally_composed_retry_and_transaction_filters()
        {
            // given
            const int MaxRetries = 2;
            var sut = new DataPipe<TestMessage> { TelemetryMode = TelemetryMode.PipelineAndFilters };
            sut.Use(new ExceptionAspect<TestMessage>());
            sut.Use(new TelemetryAspect<TestMessage>(new JsonConsoleTelemetryAdapter()));
            sut.Add(new OnTimeoutRetry<TestMessage>(MaxRetries,
                null,
                customDelay: (attempt, msg) => TimeSpan.FromMilliseconds(10 * attempt),
                        new StartTransaction<TestMessage>(
                            new MockTimeoutErroringFilter())));
            var msg = new TestMessage { Service = si };

            // when
            await sut.Invoke(msg);

            // then
            Assert.AreEqual(3, msg.Attempt); // original attempt + 2 retries
        }

        [TestMethod]
        public async Task Should_retry_with_custom_retry_and_default_delay_when_using_locally_composed_retry_and_transaction_filters()
        {
            // given
            const int MaxRetries = 2;
            var sut = new DataPipe<TestMessage> { TelemetryMode = TelemetryMode.PipelineAndFilters };
            sut.Use(new ExceptionAspect<TestMessage>());
            sut.Use(new TelemetryAspect<TestMessage>(new JsonConsoleTelemetryAdapter()));
            sut.Add(
                new OnTimeoutRetry<TestMessage>(MaxRetries,
                retryWhen: (ex, msg) => ex is HttpRequestException,
                customDelay: (attempt, msg) => TimeSpan.FromMilliseconds(10 * attempt),
                    new StartTransaction<TestMessage>(
                        new MockHttpErroringFilter())));
            var msg = new TestMessage { Service = si };

            // when
            await sut.Invoke(msg);

            // then
            Assert.AreEqual(3, msg.Attempt); // original attempt + 2 retries
        }

        [TestMethod]
        public async Task Should_retry_with_custom_retry_and_custom_delay_when_using_locally_composed_retry_and_transaction_filters()
        {
            // given
            const int MaxRetries = 2;
            var sut = new DataPipe<TestMessage> { TelemetryMode = TelemetryMode.PipelineAndFilters };
            sut.Use(new ExceptionAspect<TestMessage>());
            sut.Use(new TelemetryAspect<TestMessage>(new JsonConsoleTelemetryAdapter()));
            sut.Add(
                new OnTimeoutRetry<TestMessage>(MaxRetries,
                retryWhen: (ex, msg) => ex is HttpRequestException,
                customDelay: (attempt, msg) => TimeSpan.FromMilliseconds(10 * attempt),
                    new StartTransaction<TestMessage>(
                        new MockHttpErroringFilter())));
            var msg = new TestMessage { Service = si };

            // when
            await sut.Invoke(msg);

            // then
            Assert.AreEqual(3, msg.Attempt); // original attempt + 2 retries
        }

        /// Filters can also be composed inside a purpose-specific filter
        /// to achieve the same functionaluity as the test above
        [TestMethod]
        public async Task Should_retry_when_using_externally_composed_retry_and_transaction_filters()
        {
            // given
            var maxAttempts = 2;
            var sut = new DataPipe<TestMessage> { TelemetryMode = TelemetryMode.PipelineAndFilters };
            sut.Use(new ExceptionAspect<TestMessage>());
            sut.Use(new ExceptionAspect<TestMessage>());
            sut.Use(new BasicConsoleLoggingAspect<TestMessage>());
            sut.Use(new TelemetryAspect<TestMessage>(new JsonConsoleTelemetryAdapter()));
            sut.Add(new ComposedRetryWithTransactionFilter<TestMessage>(maxAttempts,
                new MockTimeoutErroringFilter()));
            var msg = new TestMessage { Service = si };

            // when
            await sut.Invoke(msg);

            // then
            Assert.AreEqual(3, msg.Attempt);  // original attempt + 2 retries
        }

        [TestMethod]
        public async Task Should_retry_and_recover_after_one_attempt_when_using_retry_filter()
        {
            // given
            var maxRetries = 2;
            var sut = new DataPipe<TestMessage> { TelemetryMode = TelemetryMode.PipelineAndFilters };
            sut.Use(new ExceptionAspect<TestMessage>());
            sut.Use(new TelemetryAspect<TestMessage>(new JsonConsoleTelemetryAdapter()));
            sut.Add(new OnTimeoutRetry<TestMessage>(maxRetries,
                null,
                customDelay: (attempt, msg) => TimeSpan.FromMilliseconds(10 * attempt),
                        new StartTransaction<TestMessage>(
                            new MockRecoveringTimeoutErroringFilter())));
            var msg = new TestMessage { Service = si };

            // when
            await sut.Invoke(msg);

            // then
            Assert.AreEqual(2, msg.Attempt); // orignal attempt plus first retry
        }

        [TestMethod]
        public async Task Should_not_succeed_when_pipeline_cancelled_manually()
        {
            // given
            var success = false;
            var sut = new DataPipe<TestMessage> { TelemetryMode = TelemetryMode.PipelineAndFilters };
            sut.Use(new ExceptionAspect<TestMessage>());
            sut.Use(new TelemetryAspect<TestMessage>(new JsonConsoleTelemetryAdapter()));
            sut.Add(new CancellingFilter());
            sut.Add(new NoOpFilter());
            var msg = new TestMessage { OnSuccess = (m) => success = true, Service = si };

            // when
            await sut.Invoke(msg);

            // then
            Assert.IsFalse(success);
        }

        [TestMethod]
        public async Task Should_report_reason_when_pipeline_cancelled_manually()
        {
            // given
            var sut = new DataPipe<TestMessage> { TelemetryMode = TelemetryMode.PipelineAndFilters };
            sut.Use(new ExceptionAspect<TestMessage>());
            sut.Use(new TelemetryAspect<TestMessage>(new JsonConsoleTelemetryAdapter()));
            sut.Add(new CancellingFilter());
            sut.Add(new NoOpFilter());
            var msg = new TestMessage { Service = si };

            // when
            await sut.Invoke(msg);

            // then
            Assert.IsTrue(msg.Execution.IsStopped);
            Assert.IsTrue(msg.Execution.Reason.Contains("User cancelled operation"));
        }

        [TestMethod]
        public async Task Should_run_finally_filters_when_pipeline_succeeds()
        {
            // given
            var sut = new DataPipe<TestMessage> { TelemetryMode = TelemetryMode.PipelineAndFilters };
            sut.Use(new ExceptionAspect<TestMessage>());
            sut.Use(new TelemetryAspect<TestMessage>(new JsonConsoleTelemetryAdapter()));
            sut.Add(new NoOpFilter());
            sut.Finally(new AlwaysRunFilter());
            var msg = new TestMessage { Service = si };

            // when
            await sut.Invoke(msg);

            // then
            Assert.AreEqual("AlwaysRunFilter", msg.__Debug);
        }

        [TestMethod]
        public async Task Should_run_finally_filters_when_pipeline_cancelled()
        {
            // given
            var sut = new DataPipe<TestMessage> { TelemetryMode = TelemetryMode.PipelineAndFilters };
            sut.Use(new ExceptionAspect<TestMessage>());
            sut.Use(new TelemetryAspect<TestMessage>(new JsonConsoleTelemetryAdapter()));
            sut.Add(new CancellingFilter());
            sut.Finally(new AlwaysRunFilter());
            var msg = new TestMessage { Service = si };

            // when
            await sut.Invoke(msg);

            // then
            Assert.AreEqual("AlwaysRunFilter", msg.__Debug);
        }

        [TestMethod]
        public async Task Should_run_finally_filters_when_pipeline_errors()
        {
            // given
            var sut = new DataPipe<TestMessage> { TelemetryMode = TelemetryMode.PipelineAndFilters };
            sut.Use(new ExceptionAspect<TestMessage>());
            sut.Use(new TelemetryAspect<TestMessage>(new JsonConsoleTelemetryAdapter()));
            sut.Add(new ErroringFilter());
            sut.Finally(new AlwaysRunFilter());
            var msg = new TestMessage { Service = si };

            // when
            await sut.Invoke(msg);

            // then
            Assert.AreEqual("AlwaysRunFilter", msg.__Debug);
        }

        [TestMethod]
        public async Task Should_run_message_through_filter_block_multiple_times_with_ForEach_Filter()
        {
            // given
            var sut = new DataPipe<TestMessage> { TelemetryMode = TelemetryMode.PipelineAndFilters };
            sut.Use(new ExceptionAspect<TestMessage>());
            sut.Use(new TelemetryAspect<TestMessage>(new JsonConsoleTelemetryAdapter()));

            // Each word flows through the same filter instance via the message
            sut.Add(
                new ForEach<TestMessage, string>(
                    msg => msg.Words,
                    (msg, s) => msg.Instance = s, new ConcatenatingFilter()
                )
            );
            var msg = new TestMessage { Words = ["this", "was", "constructed", "via", "multiple", "passes", "of", "the", "ForEach", "filter"], Service = si };

            // when
            await sut.Invoke(msg);

            // then
            Assert.AreEqual("this was constructed via multiple passes of the ForEach filter", msg.__Debug.TrimEnd());
        }

        [TestMethod]
        public async Task Should_repeat_filter_execution_until_pipeline_is_explicitly_stopped_by_user()
        {
            // given
            var sut = new DataPipe<TestMessage> { TelemetryMode = TelemetryMode.PipelineAndFilters };
            sut.Use(new ExceptionAspect<TestMessage>());
            sut.Use(new TelemetryAspect<TestMessage>(new JsonConsoleTelemetryAdapter()));
            sut.Add(
                new Repeat<TestMessage>(
                    new IncrementingNumberFilter(),
                    new IfTrue<TestMessage>(x => x.__Debug == "123",
                        new LambdaFilter<TestMessage>(x =>
                        {
                            x.Execution.Stop();
                            return Task.CompletedTask;
                        }))));
            var msg = new TestMessage { Service = si };

            // when
            await sut.Invoke(msg);

            // then
            Assert.AreEqual("123", msg.__Debug.TrimEnd());
        }

        [TestMethod]
        public async Task Should_repeat_filter_execution_until_callback_condition_is_met()
        {
            // given
            var sut = new DataPipe<TestMessage> { TelemetryMode = TelemetryMode.PipelineAndFilters };
            sut.Use(new ExceptionAspect<TestMessage>());
            sut.Use(new TelemetryAspect<TestMessage>(new JsonConsoleTelemetryAdapter()));
            sut.Add(
                new RepeatUntil<TestMessage>(x => x.Number == 5,
                    new IncrementingNumberFilter()));
            var msg = new TestMessage { Number = 0, Service = si };

            // when
            await sut.Invoke(msg);

            // then
            Assert.AreEqual("12345", msg.__Debug.TrimEnd());
        }

        [TestMethod]
        public async Task Should_select_filter_to_execute_based_on_message_policy_using_if()
        {
            // given
            var sut = new DataPipe<TestMessage> { TelemetryMode = TelemetryMode.PipelineAndFilters };
            sut.Use(new ExceptionAspect<TestMessage>());
            sut.Use(new TelemetryAspect<TestMessage>(new JsonConsoleTelemetryAdapter()));
            sut.Add(
                new Policy<TestMessage>(msg =>
                {
                    if (msg.Number == 0)
                        return new IncrementingNumberFilter();
                    else
                        return new DecrementingNumberFilter();
                }));
            var msg1 = new TestMessage { Number = 0, Service = si };
            var msg2 = new TestMessage { Number = 1, Service = si };

            // when
            await sut.Invoke(msg1);
            await sut.Invoke(msg2);

            // then
            Assert.AreEqual(msg1.Number, 1);
            Assert.AreEqual(msg2.Number, 0);
        }

        [TestMethod]
        public async Task Should_select_filter_to_execute_based_on_message_policy_using_switch()
        {
            // given
            var sut = new DataPipe<TestMessage> { TelemetryMode = TelemetryMode.PipelineAndFilters };
            sut.Use(new ExceptionAspect<TestMessage>()); // to handle out of range exception
            sut.Use(new TelemetryAspect<TestMessage>(new JsonConsoleTelemetryAdapter()));
            sut.Add(
                new Policy<TestMessage>(msg => msg.Number switch
                {
                    0 => new IncrementingNumberFilter(),
                    1 => new DecrementingNumberFilter(),
                    _ => throw new ArgumentOutOfRangeException()
                }));
            var msg1 = new TestMessage { Number = 0, Service = si };
            var msg2 = new TestMessage { Number = 1, Service = si };
            var msg3 = new TestMessage { Number = 2, Service = si };

            // when
            await sut.Invoke(msg1);
            await sut.Invoke(msg2);
            await sut.Invoke(msg3);

            // then
            Assert.AreEqual(msg1.Number, 1);
            Assert.AreEqual(msg2.Number, 0);
            Assert.AreEqual(msg3.StatusCode, 500);
        }

        [TestMethod]
        public async Task Should_use_sequence_as_grouping_parent_for_multiple_filters_after_condition_true_policy_decision()
        {
            // given
            var msg = new TestMessage { Number = 0, Service = si };
            var sut = new DataPipe<TestMessage> { TelemetryMode = TelemetryMode.PipelineAndFilters };
            sut.Use(new ExceptionAspect<TestMessage>());
            sut.Use(new TelemetryAspect<TestMessage>(new JsonConsoleTelemetryAdapter()));
            sut.Add(
                new Policy<TestMessage>(m =>
                {
                    if (m.Number >= 0) return
                        new Sequence<TestMessage>(
                            new IncrementingNumberFilter(),
                            new IncrementingNumberFilter(),
                            new IncrementingNumberFilter());

                    return new NoOpFilter();
                }));


            // when
            await sut.Invoke(msg);

            // then
            Assert.AreEqual(msg.Number, 3);
        }

        [TestMethod]
        public async Task Should_use_else_filter_after_condition_false_policy_decision()
        {
            // given
            var msg = new TestMessage { Number = 0, Service = si };
            var sut = new DataPipe<TestMessage> { TelemetryMode = TelemetryMode.PipelineAndFilters };
            sut.Use(new ExceptionAspect<TestMessage>());
            sut.Use(new TelemetryAspect<TestMessage>(new JsonConsoleTelemetryAdapter()));
            sut.Add(
                new Policy<TestMessage>(m =>
                {
                    if (m.Number > 0) return
                        new Sequence<TestMessage>(
                            new IncrementingNumberFilter(),
                            new IncrementingNumberFilter(),
                            new IncrementingNumberFilter());

                    return new DecrementingNumberFilter();
                }));


            // when
            await sut.Invoke(msg);

            // then
            Assert.AreEqual(msg.Number, -1);
        }

        [TestMethod]
        public async Task Should_execute_all_filters_using_overload_for_multiple_filters_without_need_for_grouping_parent_filter()
        {
            // given
            var sut = new DataPipe<TestMessage> { TelemetryMode = TelemetryMode.PipelineAndFilters };
            sut.Use(new ExceptionAspect<TestMessage>());
            sut.Use(new TelemetryAspect<TestMessage>(new JsonConsoleTelemetryAdapter()));
            sut.Add(
                new IncrementingNumberFilter(),
                new IncrementingNumberFilter(),
                new IncrementingNumberFilter());
            var msg = new TestMessage { Number = 0, Service = si };

            // when
            await sut.Invoke(msg);

            // then
            Assert.AreEqual(msg.Number, 3);
        }

        [TestMethod]
        public async Task Should_execute_all_filters_in_composite_filter_on_condition()
        {
            // given
            var sut = new DataPipe<TestMessage> { TelemetryMode = TelemetryMode.PipelineAndFilters };
            sut.Use(new ExceptionAspect<TestMessage>());
            sut.Use(new TelemetryAspect<TestMessage>(new JsonConsoleTelemetryAdapter()));
            sut.Add(
                new IfTrue<TestMessage>(m => true,
                    new IncrementingNumberFilter(),
                    new IncrementingNumberFilter(),
                    new IncrementingNumberFilter()));
            var msg = new TestMessage { Number = 0, Service = si };

            // when
            await sut.Invoke(msg);

            // then
            Assert.AreEqual(msg.Number, 3);
        }

        [TestMethod]
        public async Task Should_bypass_execution_of_all_filters_in_composite_filter_on_condition()
        {
            // given
            var sut = new DataPipe<TestMessage> { TelemetryMode = TelemetryMode.PipelineAndFilters };
            sut.Use(new ExceptionAspect<TestMessage>());
            sut.Use(new TelemetryAspect<TestMessage>(new JsonConsoleTelemetryAdapter()));
            sut.Add(
                new IfTrue<TestMessage>(m => false,
                    new IncrementingNumberFilter(),
                    new IncrementingNumberFilter(),
                    new IncrementingNumberFilter()));
            var msg = new TestMessage { Number = 0, Service = si };

            // when
            await sut.Invoke(msg);

            // then
            Assert.AreEqual(msg.Number, 0);
        }

        [TestMethod]
        public async Task Should_not_emit_telemetry_by_default()
        {
            // given
            var telemetryEvents = new List<TelemetryEvent>();
            var sut = new DataPipe<TestMessage>();
            sut.Use(new ExceptionAspect<TestMessage>());
            sut.Add(new IncrementingNumberFilter());
            var msg = new TestMessage { OnTelemetry = (e) => telemetryEvents.Add(e) };

            // when
            await sut.Invoke(msg);

            // then
            Assert.AreEqual(0, telemetryEvents.Count);
        }

        [TestMethod]
        public async Task Should_not_emit_telemetry_when_mode_is_explicitly_off()
        {
            // given
            var telemetryEvents = new List<TelemetryEvent>();
            var sut = new DataPipe<TestMessage> { TelemetryMode = TelemetryMode.Off };
            sut.Use(new ExceptionAspect<TestMessage>());
            sut.Add(new IncrementingNumberFilter());
            var msg = new TestMessage { OnTelemetry = (e) => telemetryEvents.Add(e) };

            // when
            await sut.Invoke(msg);

            // then
            Assert.AreEqual(0, telemetryEvents.Count);
        }

        [TestMethod]
        public async Task Should_still_bubble_up_exceptions_when_telemetry_is_enabled()
        {
            // given
            var sut = new DataPipe<TestMessage> { TelemetryMode = TelemetryMode.PipelineAndErrors };
            sut.Use(new ExceptionAspect<TestMessage>());
            sut.Use(new TelemetryAspect<TestMessage>(new JsonConsoleTelemetryAdapter()));
            sut.Add(new ErroringFilter());
            var msg = new TestMessage { Service = si };

            // when
            await sut.Invoke(msg);

            // then
            Assert.AreEqual(500, msg.StatusCode);
        }

        [TestMethod]
        public async Task Should_emit_only_pipeline_events_when_mode_is_pipeline_only()
        {
            // given
            var telemetryEvents = new List<TelemetryEvent>();
            var sut = new DataPipe<TestMessage> { Name = "TestPipeline", TelemetryMode = TelemetryMode.PipelineOnly };
            sut.Use(new ExceptionAspect<TestMessage>());
            sut.Add(new IncrementingNumberFilter());
            sut.Add(new IncrementingNumberFilter());
            var msg = new TestMessage { OnTelemetry = (e) => telemetryEvents.Add(e), Service = si };

            // when
            await sut.Invoke(msg);

            // then
            Assert.AreEqual(2, telemetryEvents.Count);
            Assert.IsTrue(telemetryEvents.TrueForAll(e => e.Scope == TelemetryScope.Pipeline));
            Assert.AreEqual(TelemetryPhase.Start, telemetryEvents[0].Phase);
            Assert.AreEqual(TelemetryPhase.End, telemetryEvents[1].Phase);
            Assert.AreEqual("TestPipeline", telemetryEvents[0].Component);
        }

        [TestMethod]
        public async Task Should_emit_pipeline_and_error_events_when_mode_is_pipeline_and_errors()
        {
            // given
            var telemetryEvents = new List<TelemetryEvent>();
            var sut = new DataPipe<TestMessage> { TelemetryMode = TelemetryMode.PipelineAndErrors };
            sut.Use(new ExceptionAspect<TestMessage>());
            sut.Use(new ExceptionAspect<TestMessage>());
            sut.Add(new IncrementingNumberFilter());
            sut.Add(new ErroringFilter());
            sut.Add(new IncrementingNumberFilter());
            var msg = new TestMessage { OnTelemetry = (e) => telemetryEvents.Add(e), Service = si };

            // when
            await sut.Invoke(msg);

            // then
            // Should have pipeline events (start+end) + exception events for the ErroringFilter
            var pipelineEvents = telemetryEvents.FindAll(e => e.Scope == TelemetryScope.Pipeline);
            Assert.AreEqual(2, pipelineEvents.Count, "Should have 2 pipeline events");
            var exceptionEvents = telemetryEvents.FindAll(e => e.Outcome == TelemetryOutcome.Exception);
            Assert.AreEqual(2, exceptionEvents.Count, "Should have 2 exception events");
        }

        [TestMethod]
        public async Task Should_emit_pipeline_error_and_stop_events_when_mode_is_pipeline_errors_and_stops()
        {
            // given
            var telemetryEvents = new List<TelemetryEvent>();
            var sut = new DataPipe<TestMessage> { Name = "TestPipeline", TelemetryMode = TelemetryMode.PipelineErrorsAndStops };
            sut.Use(new ExceptionAspect<TestMessage>());
            sut.Add(new IncrementingNumberFilter());
            sut.Add(new CancellingFilter());
            sut.Add(new IncrementingNumberFilter());
            var msg = new TestMessage { OnTelemetry = (e) => telemetryEvents.Add(e), Service = si };

            // when
            await sut.Invoke(msg);

            // then
            // Should have pipeline events (start+end) + stopped event for the CancellingFilter
            var pipelineEvents = telemetryEvents.FindAll(e => e.Scope == TelemetryScope.Pipeline);
            Assert.AreEqual(2, pipelineEvents.Count, "Should have 2 pipeline events");
            var stoppedEvents = telemetryEvents.FindAll(e => e.Outcome == TelemetryOutcome.Stopped);
            Assert.AreEqual(1, stoppedEvents.Count, "Should have 1 stopped event");
            var businessEvents = telemetryEvents.FindAll(e => e.Role == FilterRole.Business);
            Assert.AreEqual(0, businessEvents.Count, "Should have 0 business events");
            Assert.AreEqual(TelemetryOutcome.Stopped, pipelineEvents[1].Outcome, "Pipeline should be marked as stopped");
        }

        [TestMethod]
        public async Task Should_emit_all_telemetry_events_when_mode_is_pipeline_and_filters()
        {
            // given
            var telemetryEvents = new List<TelemetryEvent>();
            var sut = new DataPipe<TestMessage> { TelemetryMode = TelemetryMode.PipelineAndFilters };
            sut.Use(new ExceptionAspect<TestMessage>());
            sut.Add(new IncrementingNumberFilter());
            sut.Add(new IncrementingNumberFilter());
            sut.Add(new IncrementingNumberFilter());
            var msg = new TestMessage { OnTelemetry = (e) => telemetryEvents.Add(e), Service = si };

            // when
            await sut.Invoke(msg);

            // then
            // 2 pipeline events (start+end) + 6 filter events (3 filters × 2 events each)
            Assert.AreEqual(8, telemetryEvents.Count);
            var pipelineEvents = telemetryEvents.FindAll(e => e.Scope == TelemetryScope.Pipeline);
            Assert.AreEqual(2, pipelineEvents.Count);
            var filterEvents = telemetryEvents.FindAll(e => e.Scope == TelemetryScope.Filter);
            Assert.AreEqual(6, filterEvents.Count);
        }

        [TestMethod]
        public async Task Should_capture_telemetry_duration_for_filters()
        {
            // given
            var telemetryEvents = new List<TelemetryEvent>();
            var sut = new DataPipe<TestMessage> { TelemetryMode = TelemetryMode.PipelineAndFilters };
            sut.Use(new ExceptionAspect<TestMessage>());
            sut.Add(async m => { await Task.Delay(10); m.Number++; });
            var msg = new TestMessage { OnTelemetry = (e) => telemetryEvents.Add(e), Service = si };

            // when
            await sut.Invoke(msg);

            // then
            var filterCompleteEvent = telemetryEvents.Find(e => e.Scope == TelemetryScope.Filter && e.Phase == TelemetryPhase.End);
            Assert.IsNotNull(filterCompleteEvent);
            Assert.IsTrue(filterCompleteEvent.Duration >= 10);
        }

        [TestMethod]
        public async Task Should_capture_exception_details_in_telemetry()
        {
            // given
            var telemetryEvents = new List<TelemetryEvent>();
            var sut = new DataPipe<TestMessage> { TelemetryMode = TelemetryMode.PipelineAndErrors };
            sut.Use(new ExceptionAspect<TestMessage>());
            sut.Use(new ExceptionAspect<TestMessage>());
            sut.Add(new ErroringFilter());
            var msg = new TestMessage { OnTelemetry = (e) => telemetryEvents.Add(e), Service = si };

            // when
            await sut.Invoke(msg);

            // then
            var exceptionEvent = telemetryEvents.Find(e => e.Outcome == TelemetryOutcome.Exception);
            Assert.IsNotNull(exceptionEvent);
            Assert.IsNotNull(exceptionEvent.Reason);
            Assert.IsTrue(exceptionEvent.Reason.Contains("not implemented"));
        }

        [TestMethod]
        public async Task Should_capture_stop_reason_in_telemetry()
        {
            // given
            var telemetryEvents = new List<TelemetryEvent>();
            var sut = new DataPipe<TestMessage> { TelemetryMode = TelemetryMode.PipelineErrorsAndStops };
            sut.Use(new ExceptionAspect<TestMessage>());
            sut.Add(new CancellingFilter());
            var msg = new TestMessage { OnTelemetry = (e) => telemetryEvents.Add(e), Service = si };

            // when
            await sut.Invoke(msg);

            // then
            var stoppedEvent = telemetryEvents.Find(e => e.Outcome == TelemetryOutcome.Stopped);
            Assert.IsNotNull(stoppedEvent);
            Assert.IsNotNull(stoppedEvent.Reason);
            Assert.IsTrue(stoppedEvent.Reason.Contains("User cancelled operation"));
        }

        [TestMethod]
        public async Task Should_mark_structural_filters_correctly_in_telemetry()
        {
            // given
            var telemetryEvents = new List<TelemetryEvent>();
            var sut = new DataPipe<TestMessage> { TelemetryMode = TelemetryMode.PipelineAndFilters };
            sut.Use(new ExceptionAspect<TestMessage>());
            sut.Use(new TelemetryAspect<TestMessage>(new BasicConsoleTelemetryAdapter()));
            sut.Add(new IfTrue<TestMessage>(m => true, new IncrementingNumberFilter()));
            var msg = new TestMessage { OnTelemetry = (e) => telemetryEvents.Add(e), Service = si };

            // when
            await sut.Invoke(msg);

            // then
            var ifTrueEvents = telemetryEvents.FindAll(e => e.Scope == TelemetryScope.Filter && e.Role == FilterRole.Structural);
            Assert.IsTrue(ifTrueEvents.Count == 2, "IfTrue should be marked as structural");
        }

        [TestMethod]
        public async Task Should_emit_telemetry_for_nested_structural_filters()
        {
            // given
            var telemetryEvents = new List<TelemetryEvent>();
            var sut = new DataPipe<TestMessage> { TelemetryMode = TelemetryMode.PipelineAndFilters };
            sut.Use(new ExceptionAspect<TestMessage>());
            sut.Add(
                new ForEach<TestMessage, string>(
                    msg => msg.Words,
                    (msg, s) => msg.Instance = s,
                    new IncrementingNumberFilter()));
            var msg = new TestMessage { Words = ["one", "two", "three"], OnTelemetry = (e) => telemetryEvents.Add(e), Service = si };

            // when
            await sut.Invoke(msg);

            // then
            var filterEvents = telemetryEvents.FindAll(e => e.Scope == TelemetryScope.Filter);
            Assert.IsTrue(filterEvents.Count >= 6, "expected 6 events - 3 iterations × 2 events (start+end) per filter");
        }

        [TestMethod]
        public async Task Should_maintain_correlation_id_across_all_telemetry_events()
        {
            // given
            var telemetryEvents = new List<TelemetryEvent>();
            var sut = new DataPipe<TestMessage> { TelemetryMode = TelemetryMode.PipelineAndFilters };
            sut.Use(new ExceptionAspect<TestMessage>());
            sut.Add(new IncrementingNumberFilter());
            sut.Add(new IncrementingNumberFilter());
            var msg = new TestMessage { OnTelemetry = (e) => telemetryEvents.Add(e), Service = si };

            // when
            await sut.Invoke(msg);

            // then
            Assert.IsTrue(telemetryEvents.Count > 0);
            var correlationId = telemetryEvents[0].MessageId;
            Assert.IsTrue(telemetryEvents.TrueForAll(e => e.MessageId == correlationId));
        }

        [TestMethod]
        public async Task Should_record_business_domain_annotations_in_telemetry_events()
        {
            // given
            var telemetryEvents = new List<TelemetryEvent>();
            var sut = new DataPipe<TestMessage> { TelemetryMode = TelemetryMode.PipelineAndFilters };
            sut.Use(new ExceptionAspect<TestMessage>());
            sut.Use(new ExceptionAspect<TestMessage>());
            sut.Add(new LambdaFilter<TestMessage>(async m =>
            {
                m.Execution.TelemetryAnnotations.Add("OrderId", "A12345");

                throw new Exception($"Validation failed for user: {m.Number}");
            }));
            var msg = new TestMessage { Number = 123, OnTelemetry = (e) => telemetryEvents.Add(e), Service = si };

            // when
            await sut.Invoke(msg);

            // then
            var annotatedEvent = telemetryEvents.Find(e => e.Phase == TelemetryPhase.End && e.Scope == TelemetryScope.Filter);
            Assert.IsNotNull(annotatedEvent);
            Assert.IsTrue(annotatedEvent.Attributes["OrderId"].ToString() == "A12345");
        }

        [TestMethod]
        public async Task Should_route_telemetry_to_multiple_adapters_when_stacking_telemetry_aspects()
        {
            // given
            var a1 = new TestTelemetryAdapter();
            var a2 = new TestTelemetryAdapter();

            var sut = new DataPipe<TestMessage> { TelemetryMode = TelemetryMode.PipelineAndFilters };
            sut.Use(new ExceptionAspect<TestMessage>());
            sut.Use(new TelemetryAspect<TestMessage>(a1));
            sut.Use(new TelemetryAspect<TestMessage>(a2));
            sut.Add(new IncrementingNumberFilter());

            var msg1 = new TestMessage { Number = 0, Service = si };
            var msg2 = new TestMessage { Number = 10, Service = si };

            // when
            await sut.Invoke(msg1);
            await sut.Invoke(msg2);

            // then
            Assert.IsTrue(a1.Events.Count > 0, "adapter1 should receive events");
            Assert.IsTrue(a2.Events.Count > 0, "adapter2 should receive events");
            Assert.AreEqual(a1.Events.Count, a2.Events.Count, "Both adapters should receive same number of events");
        }

        [TestMethod]
        public async Task Should_emit_telemetry_to_adapter_with_business_events_only_policy()
        {
            // given
            var telemetryEvents = new List<TelemetryEvent>();
            var adapter = new TestTelemetryAdapter(new BusinessOnlyPolicy());
            var sut = new DataPipe<TestMessage> { TelemetryMode = TelemetryMode.PipelineAndFilters };
            sut.Use(new ExceptionAspect<TestMessage>());
            sut.Use(new TelemetryAspect<TestMessage>(adapter));
            sut.Add(new IfTrue<TestMessage>(m => true, new IncrementingNumberFilter()));
            var msg = new TestMessage { OnTelemetry = (e) => telemetryEvents.Add(e), Service = si };
            
            // when
            await sut.Invoke(msg);

            // then
            Assert.AreEqual(6, telemetryEvents.Count); // 6 events - start+end for pipeline, IfTrue, and IncrementingNumberFilter
            Assert.AreEqual(2, adapter.Events.Count); // 2 business events only
        }

        [TestMethod]
        public async Task Should_conditionally_add_aspect_using_UseIf()
        {
            // given
            var addTelemetryAspect = true;
            var adapter = new TestTelemetryAdapter();
            var sut = new DataPipe<TestMessage> { TelemetryMode = TelemetryMode.PipelineAndFilters };
            sut.Use(new ExceptionAspect<TestMessage>());
            sut.UseIf(addTelemetryAspect, new TelemetryAspect<TestMessage>(adapter));
            sut.Add(new IncrementingNumberFilter());
            var msg = new TestMessage { Service = si };
            
            // when
            await sut.Invoke(msg);

            // then
            Assert.IsTrue(adapter.Events.Count > 0, "Telemetry events should be captured when aspect is added");
        }

        [TestMethod]
        public async Task Should_conditionally_not_add_aspect_using_UseIf()
        {
            // given
            var addTelemetryAspect = false;
            var adapter = new TestTelemetryAdapter();
            var sut = new DataPipe<TestMessage> { TelemetryMode = TelemetryMode.PipelineAndFilters };
            sut.Use(new ExceptionAspect<TestMessage>());
            sut.UseIf(addTelemetryAspect, new TelemetryAspect<TestMessage>(adapter));
            sut.Add(new IncrementingNumberFilter());
            var msg = new TestMessage { Service = si };
            
            // when
            await sut.Invoke(msg);

            // then
            Assert.AreEqual(0, adapter.Events.Count, "No telemetry events should be captured when aspect is not added");
        }
    }
}