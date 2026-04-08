using DataPipe.Core;
using DataPipe.Core.Contracts;
using DataPipe.Core.Filters;
using DataPipe.Core.Middleware;
using DataPipe.Core.Telemetry;
using DataPipe.Core.Telemetry.Adapters;
using DataPipe.Core.Telemetry.Policies;
using DataPipe.Sql.Filters;
using DataPipe.Tests.Support;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Threading;
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
            sut.Use(new TelemetryAspect<TestMessage>(new TestTelemetryAdapter()));
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
            sut.Use(new TelemetryAspect<TestMessage>(new TestTelemetryAdapter()));
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
            sut.Use(new TelemetryAspect<TestMessage>(new TestTelemetryAdapter()));
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
            sut.Use(new TelemetryAspect<TestMessage>(new TestTelemetryAdapter()));
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
            sut.Use(new TelemetryAspect<TestMessage>(new TestTelemetryAdapter()));
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
            sut.Use(new TelemetryAspect<TestMessage>(new TestTelemetryAdapter()));
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
            sut.Use(new TelemetryAspect<TestMessage>(new TestTelemetryAdapter()));
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
            sut.Use(new TelemetryAspect<TestMessage>(new TestTelemetryAdapter()));
            sut.Add(new NoOpFilter());
            sut.Finally(new AlwaysRunFilter());
            var msg = new TestMessage { Service = si };

            // when
            await sut.Invoke(msg);

            // then
            Assert.AreEqual("AlwaysRunFilter", msg.Debug);
        }

        [TestMethod]
        public async Task Should_run_finally_filters_when_pipeline_cancelled()
        {
            // given
            var sut = new DataPipe<TestMessage> { TelemetryMode = TelemetryMode.PipelineAndFilters };
            sut.Use(new ExceptionAspect<TestMessage>());
            sut.Use(new TelemetryAspect<TestMessage>(new TestTelemetryAdapter()));
            sut.Add(new CancellingFilter());
            sut.Finally(new AlwaysRunFilter());
            var msg = new TestMessage { Service = si };

            // when
            await sut.Invoke(msg);

            // then
            Assert.AreEqual("AlwaysRunFilter", msg.Debug);
        }

        [TestMethod]
        public async Task Should_run_finally_filters_when_pipeline_errors()
        {
            // given
            var sut = new DataPipe<TestMessage> { TelemetryMode = TelemetryMode.PipelineAndFilters };
            sut.Use(new ExceptionAspect<TestMessage>());
            sut.Use(new TelemetryAspect<TestMessage>(new TestTelemetryAdapter()));
            sut.Add(new ErroringFilter());
            sut.Finally(new AlwaysRunFilter());
            var msg = new TestMessage { Service = si };

            // when
            await sut.Invoke(msg);

            // then
            Assert.AreEqual("AlwaysRunFilter", msg.Debug);
        }

        [TestMethod]
        public async Task Should_run_lambda_finally_filters_when_pipeline_succeeds()
        {
            // given
            var sut = new DataPipe<TestMessage> { TelemetryMode = TelemetryMode.PipelineAndFilters };
            sut.Use(new ExceptionAspect<TestMessage>());
            sut.Use(new TelemetryAspect<TestMessage>(new TestTelemetryAdapter()));
            sut.Add(new NoOpFilter());
            sut.Finally(msg =>
            {
                msg.Debug = "LambdaFinally";
                return Task.CompletedTask;
            });
            var msg = new TestMessage { Service = si };

            // when
            await sut.Invoke(msg);

            // then
            Assert.AreEqual("LambdaFinally", msg.Debug);
        }

        [TestMethod]
        public async Task Should_run_lambda_finally_filters_when_pipeline_cancelled()
        {
            // given
            var sut = new DataPipe<TestMessage> { TelemetryMode = TelemetryMode.PipelineAndFilters };
            sut.Use(new ExceptionAspect<TestMessage>());
            sut.Use(new TelemetryAspect<TestMessage>(new TestTelemetryAdapter()));
            sut.Add(new CancellingFilter());
            sut.Finally(msg =>
            {
                msg.Debug = "LambdaFinally";
                return Task.CompletedTask;
            });
            var msg = new TestMessage { Service = si };

            // when
            await sut.Invoke(msg);

            // then
            Assert.AreEqual("LambdaFinally", msg.Debug);
        }

        [TestMethod]
        public async Task Should_run_lambda_finally_filters_when_pipeline_errors()
        {
            // given
            var sut = new DataPipe<TestMessage> { TelemetryMode = TelemetryMode.PipelineAndFilters };
            sut.Use(new ExceptionAspect<TestMessage>());
            sut.Use(new TelemetryAspect<TestMessage>(new TestTelemetryAdapter()));
            sut.Add(new ErroringFilter());
            sut.Finally(msg =>
            {
                msg.Debug = "LambdaFinally";
                return Task.CompletedTask;
            });
            var msg = new TestMessage { Service = si };

            // when
            await sut.Invoke(msg);

            // then
            Assert.AreEqual("LambdaFinally", msg.Debug);
        }

        [TestMethod]
        public async Task Should_run_message_through_filter_block_multiple_times_with_ForEach_Filter()
        {
            // given
            var children = Enumerable.Range(1, 10)
                .Select(i => new ChildMessage { Id = i, Value = 0 })
                .ToList();
            var sut = new DataPipe<TestMessage> { TelemetryMode = TelemetryMode.PipelineAndFilters };
            sut.Use(new ExceptionAspect<TestMessage>());
            sut.Use(new TelemetryAspect<TestMessage>(new TestTelemetryAdapter()));

            // Each child flows through the same filter instance sequentially.
            sut.Add(
                new ForEach<TestMessage, ChildMessage>(
                    msg => msg.Children,
                    new IncrementChildValueFilter()
                )
            );
            var msg = new TestMessage { Children = children, Service = si };

            // when
            await sut.Invoke(msg);

            // then
            Assert.IsTrue(children.All(c => c.Value == 1));
        }

        [TestMethod]
        public async Task Should_repeat_filter_execution_until_pipeline_is_explicitly_stopped_by_user()
        {
            // given
            var sut = new DataPipe<TestMessage> { TelemetryMode = TelemetryMode.PipelineAndFilters };
            sut.Use(new ExceptionAspect<TestMessage>());
            sut.Use(new TelemetryAspect<TestMessage>(new TestTelemetryAdapter()));
            sut.Add(
                new Repeat<TestMessage>(
                    new IncrementingNumberFilter(),
                    new IfTrue<TestMessage>(x => x.Debug == "123",
                        new LambdaFilter<TestMessage>(x =>
                        {
                            x.Execution.Stop();
                            return Task.CompletedTask;
                        }))));
            var msg = new TestMessage { Service = si };

            // when
            await sut.Invoke(msg);

            // then
            Assert.AreEqual("123", msg.Debug.TrimEnd());
        }

        [TestMethod]
        public async Task Should_repeat_filter_execution_until_callback_condition_is_met()
        {
            // given
            var sut = new DataPipe<TestMessage> { TelemetryMode = TelemetryMode.PipelineAndFilters };
            sut.Use(new ExceptionAspect<TestMessage>());
            sut.Use(new TelemetryAspect<TestMessage>(new TestTelemetryAdapter()));
            sut.Add(
                new RepeatUntil<TestMessage>(x => x.Number == 5,
                    new IncrementingNumberFilter()));
            var msg = new TestMessage { Number = 0, Service = si };

            // when
            await sut.Invoke(msg);

            // then
            Assert.AreEqual("12345", msg.Debug.TrimEnd());
        }

        [TestMethod]
        public async Task Should_select_filter_to_execute_based_on_message_policy_using_if()
        {
            // given
            var sut = new DataPipe<TestMessage> { TelemetryMode = TelemetryMode.PipelineAndFilters };
            sut.Use(new ExceptionAspect<TestMessage>());
            sut.Use(new TelemetryAspect<TestMessage>(new TestTelemetryAdapter()));
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
            sut.Use(new TelemetryAspect<TestMessage>(new TestTelemetryAdapter()));
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
            sut.Use(new TelemetryAspect<TestMessage>(new TestTelemetryAdapter()));
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
            sut.Use(new TelemetryAspect<TestMessage>(new TestTelemetryAdapter()));
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
            sut.Use(new TelemetryAspect<TestMessage>(new TestTelemetryAdapter()));
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
            sut.Use(new TelemetryAspect<TestMessage>(new TestTelemetryAdapter()));
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
            sut.Use(new TelemetryAspect<TestMessage>(new TestTelemetryAdapter()));
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
            sut.Use(new TelemetryAspect<TestMessage>(new TestTelemetryAdapter()));
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
            Assert.IsTrue(filterCompleteEvent.DurationMs >= 10);
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
            sut.Use(new TelemetryAspect<TestMessage>(new TestTelemetryAdapter()));
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
            var children = new List<ChildMessage>
            {
                new() { Id = 1, Value = 0 },
                new() { Id = 2, Value = 0 },
                new() { Id = 3, Value = 0 }
            };
            var sut = new DataPipe<TestMessage> { TelemetryMode = TelemetryMode.PipelineAndFilters };
            sut.Use(new ExceptionAspect<TestMessage>());
            sut.Add(
                new ForEach<TestMessage, ChildMessage>(
                    msg => msg.Children,
                    new IncrementChildValueFilter()));
            var msg = new TestMessage { Children = children, OnTelemetry = (e) => telemetryEvents.Add(e), Service = si };

            // when
            await sut.Invoke(msg);

            // then
            var childFilterEvents = telemetryEvents.FindAll(e =>
                e.Component == "IncrementChildValueFilter" && e.Scope == TelemetryScope.Filter);
            Assert.AreEqual(6, childFilterEvents.Count, "expected 6 events - 3 iterations × 2 events (start+end) for child filter");
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
        public async Task Should_emit_telemetry_to_adapter_with_pipeline_startend_and_business_events_only_policy()
        {
            // given
            var telemetryEvents = new List<TelemetryEvent>();
            var adapter = new TestTelemetryAdapter(new RolePolicy(TelemetryRole.Business));
            var sut = new DataPipe<TestMessage> { TelemetryMode = TelemetryMode.PipelineAndFilters };
            sut.Use(new ExceptionAspect<TestMessage>());
            sut.Use(new TelemetryAspect<TestMessage>(adapter));
            sut.Add(new IfTrue<TestMessage>(m => true, new IncrementingNumberFilter()));
            var msg = new TestMessage { OnTelemetry = (e) => telemetryEvents.Add(e), Service = si };
            
            // when
            await sut.Invoke(msg);

            // then
            Assert.AreEqual(6, telemetryEvents.Count); // 6 events - start+end for pipeline, IfTrue, and IncrementingNumberFilter
            Assert.AreEqual(4, adapter.Events.Count); // 4 events total via adapter and policy
            Assert.AreEqual(2, adapter.Events.Count(e => e.Role == FilterRole.Business)); // 2 business events
            Assert.AreEqual(2, adapter.Events.Count(e => e.Role == FilterRole.None)); // 2 pipeline events
            Assert.IsTrue(telemetryEvents.First().Scope == TelemetryScope.Pipeline);
            Assert.IsTrue(telemetryEvents.Last().Scope == TelemetryScope.Pipeline);
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

        [TestMethod]
        public void Should_only_include_events_when_all_policies_in_composite_agree()
        {
            // given
            var businessOnlyPolicy = new RolePolicy(TelemetryRole.Business);
            var structuralOnlyPolicy = new RolePolicy(TelemetryRole.Structural);
            var compositePolicy = new CompositeTelemetryPolicy(businessOnlyPolicy, structuralOnlyPolicy);
            var adapter = new TestTelemetryAdapter(compositePolicy);

            var businessEvent = new TelemetryEvent
            {
                MessageId = Guid.NewGuid(),
                Role = FilterRole.Business,
                Phase = TelemetryPhase.Start,
                Scope = TelemetryScope.Filter,
                Timestamp = DateTimeOffset.UtcNow
            };

            var structuralEvent = new TelemetryEvent
            {
                MessageId = Guid.NewGuid(),
                Role = FilterRole.Structural,
                Phase = TelemetryPhase.Start,
                Scope = TelemetryScope.Filter,
                Timestamp = DateTimeOffset.UtcNow
            };

            var noneEvent = new TelemetryEvent
            {
                MessageId = Guid.NewGuid(),
                Role = FilterRole.None,
                Phase = TelemetryPhase.Start,
                Scope = TelemetryScope.Filter,
                Timestamp = DateTimeOffset.UtcNow
            };

            // when - handle events through the adapter with composite policy
            adapter.Handle(businessEvent);
            adapter.Handle(structuralEvent);
            adapter.Handle(noneEvent);
            adapter.Flush();

            // then - events should only be included if they satisfy ALL policies (Business AND Structural)
            // Since no single event is both Business and Structural, no events should be captured
            Assert.AreEqual(0, adapter.Events.Count, 
                "Composite policy requires events to satisfy ALL policies; no event should match both Business and Structural roles");
        }

        [TestMethod]
        public void Should_include_events_with_composite_policy_when_multiple_policies_agree()
        {
            // given
            var businessOnlyPolicy = new RolePolicy(TelemetryRole.Business);
            var defaultPolicy = new DefaultCaptureEverythingPolicy();
            var compositePolicy = new CompositeTelemetryPolicy(businessOnlyPolicy, defaultPolicy);
            var adapter = new TestTelemetryAdapter(compositePolicy);

            var businessEvent = new TelemetryEvent
            {
                MessageId = Guid.NewGuid(),
                Role = FilterRole.Business,
                Phase = TelemetryPhase.Start,
                Scope = TelemetryScope.Filter,
                Timestamp = DateTimeOffset.UtcNow
            };

            var structuralEvent = new TelemetryEvent
            {
                MessageId = Guid.NewGuid(),
                Role = FilterRole.Structural,
                Phase = TelemetryPhase.Start,
                Scope = TelemetryScope.Filter,
                Timestamp = DateTimeOffset.UtcNow
            };

            // when - handle events through the adapter with composite policy
            adapter.Handle(businessEvent);
            adapter.Handle(structuralEvent);
            adapter.Flush();

            // then - only the business event should be captured (satisfies both policies)
            Assert.AreEqual(1, adapter.Events.Count, 
                "Composite policy should include only events that satisfy ALL policies");
            Assert.AreEqual(FilterRole.Business, adapter.Events[0].Role);
        }

        // TransientState tests

        [TestMethod]
        public async Task Should_store_and_retrieve_transient_state_between_filters()
        {
            // given
            var sut = new DataPipe<TestMessage>();
            sut.Use(new ExceptionAspect<TestMessage>());
            sut.Add(async msg => { msg.State.Set("name", "DataPipe"); });
            sut.Add(async msg => { msg.Debug = msg.State.Get<string>("name"); });
            var msg = new TestMessage();

            // when
            await sut.Invoke(msg);

            // then
            Assert.AreEqual("DataPipe", msg.Debug);
        }

        [TestMethod]
        public async Task Should_store_and_retrieve_typed_values_in_transient_state()
        {
            // given
            var sut = new DataPipe<TestMessage>();
            sut.Use(new ExceptionAspect<TestMessage>());
            sut.Add(async msg =>
            {
                msg.State.Set("count", 42);
                msg.State.Set("price", 19.99m);
                msg.State.Set("timestamp", new DateTime(2026, 3, 23));
            });
            sut.Add(async msg =>
            {
                var count = msg.State.Get<int>("count");
                var price = msg.State.Get<decimal>("price");
                var timestamp = msg.State.Get<DateTime>("timestamp");
                msg.Debug = $"{count}-{price}-{timestamp:yyyy-MM-dd}";
            });
            var msg = new TestMessage();

            // when
            await sut.Invoke(msg);

            // then
            Assert.AreEqual("42-19.99-2026-03-23", msg.Debug);
        }

        [TestMethod]
        public async Task Should_throw_when_getting_missing_transient_state_key()
        {
            // given
            var sut = new DataPipe<TestMessage>();
            sut.Use(new ExceptionAspect<TestMessage>());
            sut.Add(async msg => { var x = msg.State.Get<string>("missing"); });
            var msg = new TestMessage();

            // when
            await sut.Invoke(msg);

            // then
            Assert.AreEqual(500, msg.StatusCode);
        }

        [TestMethod]
        public async Task Should_return_default_when_using_GetOrDefault_for_missing_key()
        {
            // given
            var sut = new DataPipe<TestMessage>();
            sut.Use(new ExceptionAspect<TestMessage>());
            sut.Add(async msg =>
            {
                msg.Debug = msg.State.GetOrDefault<string>("missing", "fallback");
            });
            var msg = new TestMessage();

            // when
            await sut.Invoke(msg);

            // then
            Assert.AreEqual("fallback", msg.Debug);
        }

        [TestMethod]
        public async Task Should_check_existence_of_transient_state_key()
        {
            // given
            var sut = new DataPipe<TestMessage>();
            sut.Use(new ExceptionAspect<TestMessage>());
            sut.Add(async msg => { msg.State.Set("exists", true); });
            sut.Add(async msg =>
            {
                msg.Debug = $"{msg.State.Has("exists")}-{msg.State.Has("missing")}";
            });
            var msg = new TestMessage();

            // when
            await sut.Invoke(msg);

            // then
            Assert.AreEqual("True-False", msg.Debug);
        }

        [TestMethod]
        public async Task Should_remove_transient_state_key()
        {
            // given
            var sut = new DataPipe<TestMessage>();
            sut.Use(new ExceptionAspect<TestMessage>());
            sut.Add(async msg =>
            {
                msg.State.Set("temp", "value");
                msg.State.Remove("temp");
                msg.Debug = msg.State.Has("temp").ToString();
            });
            var msg = new TestMessage();

            // when
            await sut.Invoke(msg);

            // then
            Assert.AreEqual("False", msg.Debug);
        }

        [TestMethod]
        public async Task Should_overwrite_transient_state_value_with_same_key()
        {
            // given
            var sut = new DataPipe<TestMessage>();
            sut.Use(new ExceptionAspect<TestMessage>());
            sut.Add(async msg => { msg.State.Set("val", "first"); });
            sut.Add(async msg => { msg.State.Set("val", "second"); });
            sut.Add(async msg => { msg.Debug = msg.State.Get<string>("val"); });
            var msg = new TestMessage();

            // when
            await sut.Invoke(msg);

            // then
            Assert.AreEqual("second", msg.Debug);
        }

        // ── OnCircuitBreak ──────────────────────────────────────────────

        [TestMethod]
        public async Task Should_execute_wrapped_filters_when_circuit_is_closed()
        {
            // given
            var state = new CircuitBreakerState();
            var sut = new DataPipe<TestMessage>();
            sut.Use(new ExceptionAspect<TestMessage>());
            sut.Add(new OnCircuitBreak<TestMessage>(state,
                filters: new IncrementingNumberFilter()));
            var msg = new TestMessage { Number = 0 };

            // when
            await sut.Invoke(msg);

            // then
            Assert.AreEqual(1, msg.Number);
            Assert.AreEqual(CircuitState.Closed, state.Status);
        }

        [TestMethod]
        public async Task Should_trip_circuit_to_open_after_reaching_failure_threshold()
        {
            // given
            var state = new CircuitBreakerState();
            var failureThreshold = 3;
            var sut = new DataPipe<TestMessage>();
            sut.Use(new ExceptionAspect<TestMessage>());
            sut.Add(new OnCircuitBreak<TestMessage>(state,
                failureThreshold: failureThreshold,
                filters: new AlwaysFailingFilter()));

            // when — invoke enough times to reach the threshold
            for (int i = 0; i < failureThreshold; i++)
            {
                await sut.Invoke(new TestMessage());
            }

            // then
            Assert.AreEqual(CircuitState.Open, state.Status);
            Assert.AreEqual(failureThreshold, state.FailureCount);
            Assert.IsNotNull(state.LockedUntil);
        }

        [TestMethod]
        public async Task Should_fail_fast_when_circuit_is_open()
        {
            // given
            var state = new CircuitBreakerState
            {
                Status = CircuitState.Open,
                LockedUntil = DateTimeOffset.UtcNow.AddMinutes(5)
            };
            var sut = new DataPipe<TestMessage>();
            sut.Use(new ExceptionAspect<TestMessage>());
            sut.Add(new OnCircuitBreak<TestMessage>(state,
                filters: new IncrementingNumberFilter()));
            var msg = new TestMessage { Number = 0 };

            // when
            await sut.Invoke(msg);

            // then — filter was NOT executed; circuit blocked it
            Assert.AreEqual(0, msg.Number);
            Assert.AreEqual(503, msg.StatusCode);
        }

        [TestMethod]
        public async Task Should_transition_to_half_open_after_break_duration_expires()
        {
            // given — circuit is open but break duration has elapsed
            var state = new CircuitBreakerState
            {
                Status = CircuitState.Open,
                LockedUntil = DateTimeOffset.UtcNow.AddSeconds(-1)
            };
            var sut = new DataPipe<TestMessage>();
            sut.Use(new ExceptionAspect<TestMessage>());
            sut.Add(new OnCircuitBreak<TestMessage>(state,
                filters: new IncrementingNumberFilter()));
            var msg = new TestMessage { Number = 0 };

            // when
            await sut.Invoke(msg);

            // then — probe succeeded and circuit is closed again
            Assert.AreEqual(1, msg.Number);
            Assert.AreEqual(CircuitState.Closed, state.Status);
            Assert.AreEqual(0, state.FailureCount);
        }

        [TestMethod]
        public async Task Should_reopen_circuit_when_half_open_probe_fails()
        {
            // given — circuit is open but break duration has elapsed (probe attempt)
            var state = new CircuitBreakerState
            {
                Status = CircuitState.Open,
                LockedUntil = DateTimeOffset.UtcNow.AddSeconds(-1)
            };
            var sut = new DataPipe<TestMessage>();
            sut.Use(new ExceptionAspect<TestMessage>());
            sut.Add(new OnCircuitBreak<TestMessage>(state,
                filters: new AlwaysFailingFilter()));
            var msg = new TestMessage();

            // when — the probe attempt fails
            await sut.Invoke(msg);

            // then — circuit trips straight back to Open
            Assert.AreEqual(CircuitState.Open, state.Status);
            Assert.IsNotNull(state.LockedUntil);
        }

        [TestMethod]
        public async Task Should_reset_failure_count_on_success()
        {
            // given — circuit has accumulated some failures but not tripped yet
            var state = new CircuitBreakerState { FailureCount = 3 };
            var sut = new DataPipe<TestMessage>();
            sut.Use(new ExceptionAspect<TestMessage>());
            sut.Add(new OnCircuitBreak<TestMessage>(state,
                failureThreshold: 5,
                filters: new IncrementingNumberFilter()));
            var msg = new TestMessage { Number = 0 };

            // when
            await sut.Invoke(msg);

            // then
            Assert.AreEqual(1, msg.Number);
            Assert.AreEqual(0, state.FailureCount);
            Assert.AreEqual(CircuitState.Closed, state.Status);
        }

        [TestMethod]
        public async Task Should_share_circuit_state_across_multiple_pipeline_invocations()
        {
            // given — shared state simulating a DI singleton
            var sharedState = new CircuitBreakerState();
            var failureThreshold = 2;

            var pipeline1 = new DataPipe<TestMessage>();
            pipeline1.Use(new ExceptionAspect<TestMessage>());
            pipeline1.Add(new OnCircuitBreak<TestMessage>(sharedState,
                failureThreshold: failureThreshold,
                filters: new AlwaysFailingFilter()));

            var pipeline2 = new DataPipe<TestMessage>();
            pipeline2.Use(new ExceptionAspect<TestMessage>());
            pipeline2.Add(new OnCircuitBreak<TestMessage>(sharedState,
                failureThreshold: failureThreshold,
                filters: new IncrementingNumberFilter()));

            // when — pipeline1 trips the circuit
            for (int i = 0; i < failureThreshold; i++)
            {
                await pipeline1.Invoke(new TestMessage());
            }

            // then — pipeline2 is also blocked
            var msg = new TestMessage { Number = 0 };
            await pipeline2.Invoke(msg);
            Assert.AreEqual(0, msg.Number, "Pipeline2 should have been blocked by the shared open circuit");
            Assert.AreEqual(503, msg.StatusCode);
        }

        [TestMethod]
        public async Task Should_emit_circuit_breaker_telemetry_events_with_circuit_state_attributes()
        {
            // given
            var state = new CircuitBreakerState();
            var adapter = new TestTelemetryAdapter();
            var sut = new DataPipe<TestMessage> { TelemetryMode = TelemetryMode.PipelineAndFilters };
            sut.Use(new ExceptionAspect<TestMessage>());
            sut.Use(new TelemetryAspect<TestMessage>(adapter));
            sut.Add(new OnCircuitBreak<TestMessage>(state,
                filters: new IncrementingNumberFilter()));
            var msg = new TestMessage { Number = 0, Service = si };

            // when
            await sut.Invoke(msg);

            // then — should have circuit breaker start and end events with attributes
            var cbEvents = adapter.Events
                .Where(e => e.Component == "OnCircuitBreak" && e.Scope == TelemetryScope.Filter)
                .ToList();

            Assert.IsTrue(cbEvents.Count >= 2, "Expected at least start and end events for OnCircuitBreak");

            var startEvent = cbEvents.First(e => e.Phase == TelemetryPhase.Start);
            Assert.AreEqual("Closed", startEvent.Attributes["circuit-state"].ToString());
            Assert.AreEqual(5, (int)startEvent.Attributes["failure-threshold"]);

            var endEvent = cbEvents.First(e => e.Phase == TelemetryPhase.End);
            Assert.AreEqual("Closed", endEvent.Attributes["circuit-state"].ToString());
            Assert.AreEqual(0, (int)endEvent.Attributes["failure-count"]);
            Assert.AreEqual(false, (bool)endEvent.Attributes["circuit-tripped"]);
        }

        [TestMethod]
        public async Task Should_emit_telemetry_with_circuit_tripped_attribute_when_circuit_trips()
        {
            // given
            var state = new CircuitBreakerState();
            var adapter = new TestTelemetryAdapter();
            var sut = new DataPipe<TestMessage> { TelemetryMode = TelemetryMode.PipelineAndFilters };
            sut.Use(new ExceptionAspect<TestMessage>());
            sut.Use(new TelemetryAspect<TestMessage>(adapter));
            sut.Add(new OnCircuitBreak<TestMessage>(state,
                failureThreshold: 1,
                filters: new AlwaysFailingFilter()));
            var msg = new TestMessage { Service = si };

            // when — single failure trips the circuit
            await sut.Invoke(msg);

            // then
            var endEvent = adapter.Events
                .First(e => e.Component == "OnCircuitBreak" && e.Phase == TelemetryPhase.End);

            Assert.AreEqual(true, (bool)endEvent.Attributes["circuit-tripped"]);
            Assert.AreEqual("Open", endEvent.Attributes["circuit-state"].ToString());
            Assert.AreEqual(TelemetryOutcome.Exception, endEvent.Outcome);
        }

        [TestMethod]
        public async Task Should_fast_fail_concurrent_requests_while_half_open_probe_is_in_progress()
        {
            // given — circuit is half-open (another request is already probing)
            var state = new CircuitBreakerState
            {
                Status = CircuitState.HalfOpen,
                FailureCount = 3
            };
            var sut = new DataPipe<TestMessage>();
            sut.Use(new ExceptionAspect<TestMessage>());
            sut.Add(new OnCircuitBreak<TestMessage>(state,
                filters: new IncrementingNumberFilter()));
            var msg = new TestMessage { Number = 0 };

            // when — a second request arrives while probe is in progress
            await sut.Invoke(msg);

            // then — filter was NOT executed; circuit blocked it
            Assert.AreEqual(0, msg.Number, "Request should have been blocked while another probe is in progress");
            Assert.AreEqual(503, msg.StatusCode);
        }

        [TestMethod]
        public async Task Should_set_503_status_code_when_circuit_breaker_rejects_request()
        {
            // given — circuit is open
            var state = new CircuitBreakerState
            {
                Status = CircuitState.Open,
                LockedUntil = DateTimeOffset.UtcNow.AddMinutes(5)
            };
            var sut = new DataPipe<TestMessage>();
            sut.Use(new ExceptionAspect<TestMessage>());
            sut.Add(new OnCircuitBreak<TestMessage>(state,
                filters: new IncrementingNumberFilter()));
            var msg = new TestMessage { Number = 0 };

            // when
            await sut.Invoke(msg);

            // then — 503 Service Unavailable, not 500
            Assert.AreEqual(503, msg.StatusCode);
            Assert.IsTrue(msg.StatusMessage.Contains("circuit breaker"));
        }

        // ── OnRateLimit ─────────────────────────────────────────────────

        [TestMethod]
        public async Task Should_execute_wrapped_filters_when_bucket_has_capacity()
        {
            // given
            var state = new RateLimiterState();
            var sut = new DataPipe<TestMessage>();
            sut.Use(new ExceptionAspect<TestMessage>());
            sut.Add(new OnRateLimit<TestMessage>(state,
                capacity: 10,
                leakInterval: TimeSpan.FromMilliseconds(100),
                filters: new IncrementingNumberFilter()));
            var msg = new TestMessage { Number = 0 };

            // when
            await sut.Invoke(msg);

            // then
            Assert.AreEqual(1, msg.Number);
            Assert.IsTrue(msg.IsSuccess);
        }

        [TestMethod]
        public async Task Should_reject_when_bucket_is_full_and_behavior_is_reject()
        {
            // given — pre-fill the bucket to capacity
            var state = new RateLimiterState();
            var capacity = 3;
            lock (state.Lock)
            {
                for (int i = 0; i < capacity; i++)
                {
                    state.Tokens.Enqueue(DateTimeOffset.UtcNow);
                }
            }

            var sut = new DataPipe<TestMessage>();
            sut.Use(new ExceptionAspect<TestMessage>());
            sut.Add(new OnRateLimit<TestMessage>(state,
                capacity: capacity,
                leakInterval: TimeSpan.FromMinutes(5), // won't drain during this test
                behavior: RateLimitExceededBehavior.Reject,
                filters: new IncrementingNumberFilter()));
            var msg = new TestMessage { Number = 0 };

            // when
            await sut.Invoke(msg);

            // then — filter was NOT executed; rate limiter blocked it
            Assert.AreEqual(0, msg.Number);
            Assert.AreEqual(429, msg.StatusCode);
        }

        [TestMethod]
        public async Task Should_set_429_status_code_when_rate_limiter_rejects_request()
        {
            // given — bucket full, reject mode
            var state = new RateLimiterState();
            lock (state.Lock)
            {
                state.Tokens.Enqueue(DateTimeOffset.UtcNow);
            }
            var sut = new DataPipe<TestMessage>();
            sut.Use(new ExceptionAspect<TestMessage>());
            sut.Add(new OnRateLimit<TestMessage>(state,
                capacity: 1,
                leakInterval: TimeSpan.FromMinutes(5),
                behavior: RateLimitExceededBehavior.Reject,
                filters: new IncrementingNumberFilter()));
            var msg = new TestMessage { Number = 0 };

            // when
            await sut.Invoke(msg);

            // then — 429 Too Many Requests, not 500
            Assert.AreEqual(429, msg.StatusCode);
            Assert.IsTrue(msg.StatusMessage.Contains("rate limiter"));
        }

        [TestMethod]
        public async Task Should_delay_until_capacity_is_available_when_behavior_is_delay()
        {
            // given — fill the bucket, but use a short leak interval so it drains quickly
            var state = new RateLimiterState();
            var capacity = 2;
            var leakInterval = TimeSpan.FromMilliseconds(50);
            lock (state.Lock)
            {
                for (int i = 0; i < capacity; i++)
                {
                    state.Tokens.Enqueue(DateTimeOffset.UtcNow);
                }
            }

            var sut = new DataPipe<TestMessage>();
            sut.Use(new ExceptionAspect<TestMessage>());
            sut.Add(new OnRateLimit<TestMessage>(state,
                capacity: capacity,
                leakInterval: leakInterval,
                behavior: RateLimitExceededBehavior.Delay,
                filters: new IncrementingNumberFilter()));
            var msg = new TestMessage { Number = 0 };
            var sw = Stopwatch.StartNew();

            // when — should wait for at least one token to drain
            await sut.Invoke(msg);
            sw.Stop();

            // then — filter DID execute (after waiting)
            Assert.AreEqual(1, msg.Number);
            Assert.IsTrue(msg.IsSuccess);
            // Must have waited at least the leak interval for one token to drain
            Assert.IsTrue(sw.ElapsedMilliseconds >= 40, $"Expected wait of at least ~50ms, actual: {sw.ElapsedMilliseconds}ms");
        }

        [TestMethod]
        public async Task Should_drain_expired_tokens_and_allow_new_requests()
        {
            // given — fill bucket with tokens that are already expired
            var state = new RateLimiterState();
            var leakInterval = TimeSpan.FromMilliseconds(50);
            lock (state.Lock)
            {
                // Add tokens that are old enough to have expired
                for (int i = 0; i < 5; i++)
                {
                    state.Tokens.Enqueue(DateTimeOffset.UtcNow.AddMilliseconds(-100));
                }
            }

            var sut = new DataPipe<TestMessage>();
            sut.Use(new ExceptionAspect<TestMessage>());
            sut.Add(new OnRateLimit<TestMessage>(state,
                capacity: 5,
                leakInterval: leakInterval,
                filters: new IncrementingNumberFilter()));
            var msg = new TestMessage { Number = 0 };

            // when — expired tokens should drain, making room
            await sut.Invoke(msg);

            // then — succeeded without delay
            Assert.AreEqual(1, msg.Number);
            Assert.IsTrue(msg.IsSuccess);
        }

        [TestMethod]
        public async Task Should_allow_multiple_requests_within_capacity()
        {
            // given
            var state = new RateLimiterState();
            var capacity = 10;

            // when — invoke 10 times within capacity
            for (int i = 0; i < capacity; i++)
            {
                var sut = new DataPipe<TestMessage>();
                sut.Use(new ExceptionAspect<TestMessage>());
                sut.Add(new OnRateLimit<TestMessage>(state,
                    capacity: capacity,
                    leakInterval: TimeSpan.FromMinutes(5), // won't drain
                    filters: new IncrementingNumberFilter()));
                var msg = new TestMessage { Number = i };
                await sut.Invoke(msg);

                // then — each should succeed
                Assert.AreEqual(i + 1, msg.Number, $"Request {i} should have succeeded");
                Assert.IsTrue(msg.IsSuccess);
            }

            // then — bucket is now full
            Assert.AreEqual(capacity, state.CurrentQueueDepth);
        }

        [TestMethod]
        public async Task Should_reject_request_after_capacity_exhausted_in_reject_mode()
        {
            // given — fill bucket to capacity via actual invocations
            var state = new RateLimiterState();
            var capacity = 3;

            for (int i = 0; i < capacity; i++)
            {
                var pipe = new DataPipe<TestMessage>();
                pipe.Use(new ExceptionAspect<TestMessage>());
                pipe.Add(new OnRateLimit<TestMessage>(state,
                    capacity: capacity,
                    leakInterval: TimeSpan.FromMinutes(5),
                    behavior: RateLimitExceededBehavior.Reject,
                    filters: new NoOpFilter()));
                await pipe.Invoke(new TestMessage());
            }

            // when — one more request should be rejected
            var sut = new DataPipe<TestMessage>();
            sut.Use(new ExceptionAspect<TestMessage>());
            sut.Add(new OnRateLimit<TestMessage>(state,
                capacity: capacity,
                leakInterval: TimeSpan.FromMinutes(5),
                behavior: RateLimitExceededBehavior.Reject,
                filters: new IncrementingNumberFilter()));
            var msg = new TestMessage { Number = 0 };
            await sut.Invoke(msg);

            // then
            Assert.AreEqual(0, msg.Number, "Filter should not have executed");
            Assert.AreEqual(429, msg.StatusCode);
        }

        [TestMethod]
        public async Task Should_share_rate_limiter_state_across_pipeline_invocations()
        {
            // given — shared state simulating a DI singleton
            var sharedState = new RateLimiterState();
            var capacity = 2;

            // Fill from pipeline1
            var pipeline1 = new DataPipe<TestMessage>();
            pipeline1.Use(new ExceptionAspect<TestMessage>());
            pipeline1.Add(new OnRateLimit<TestMessage>(sharedState,
                capacity: capacity,
                leakInterval: TimeSpan.FromMinutes(5),
                behavior: RateLimitExceededBehavior.Reject,
                filters: new NoOpFilter()));

            for (int i = 0; i < capacity; i++)
            {
                await pipeline1.Invoke(new TestMessage());
            }

            // when — pipeline2 uses the same state, should be blocked
            var pipeline2 = new DataPipe<TestMessage>();
            pipeline2.Use(new ExceptionAspect<TestMessage>());
            pipeline2.Add(new OnRateLimit<TestMessage>(sharedState,
                capacity: capacity,
                leakInterval: TimeSpan.FromMinutes(5),
                behavior: RateLimitExceededBehavior.Reject,
                filters: new IncrementingNumberFilter()));
            var msg = new TestMessage { Number = 0 };
            await pipeline2.Invoke(msg);

            // then — pipeline2 was blocked by shared bucket
            Assert.AreEqual(0, msg.Number, "Pipeline2 should have been blocked by the shared full bucket");
            Assert.AreEqual(429, msg.StatusCode);
        }

        [TestMethod]
        public async Task Should_emit_rate_limiter_telemetry_events_with_attributes()
        {
            // given
            var state = new RateLimiterState();
            var adapter = new TestTelemetryAdapter();
            var sut = new DataPipe<TestMessage> { TelemetryMode = TelemetryMode.PipelineAndFilters };
            sut.Use(new ExceptionAspect<TestMessage>());
            sut.Use(new TelemetryAspect<TestMessage>(adapter));
            sut.Add(new OnRateLimit<TestMessage>(state,
                capacity: 10,
                leakInterval: TimeSpan.FromMilliseconds(100),
                filters: new IncrementingNumberFilter()));
            var msg = new TestMessage { Number = 0, Service = si };

            // when
            await sut.Invoke(msg);

            // then — should have rate limiter start and end events with attributes
            var rlEvents = adapter.Events
                .Where(e => e.Component == "OnRateLimit" && e.Scope == TelemetryScope.Filter)
                .ToList();

            Assert.IsTrue(rlEvents.Count >= 2, "Expected at least start and end events for OnRateLimit");

            var startEvent = rlEvents.First(e => e.Phase == TelemetryPhase.Start);
            Assert.AreEqual(10, (int)startEvent.Attributes["capacity"]);
            Assert.AreEqual(100.0, (double)startEvent.Attributes["leak-interval-ms"]);
            Assert.AreEqual("Delay", startEvent.Attributes["behavior"].ToString());

            var endEvent = rlEvents.First(e => e.Phase == TelemetryPhase.End);
            Assert.AreEqual(10, (int)endEvent.Attributes["capacity"]);
            Assert.AreEqual(false, (bool)endEvent.Attributes["rejected"]);
            Assert.AreEqual(TelemetryOutcome.Success, endEvent.Outcome);
        }

        [TestMethod]
        public async Task Should_emit_telemetry_with_rejected_attribute_when_rate_limit_rejects()
        {
            // given — bucket full, reject mode
            var state = new RateLimiterState();
            lock (state.Lock)
            {
                state.Tokens.Enqueue(DateTimeOffset.UtcNow);
            }
            var adapter = new TestTelemetryAdapter();
            var sut = new DataPipe<TestMessage> { TelemetryMode = TelemetryMode.PipelineAndFilters };
            sut.Use(new ExceptionAspect<TestMessage>());
            sut.Use(new TelemetryAspect<TestMessage>(adapter));
            sut.Add(new OnRateLimit<TestMessage>(state,
                capacity: 1,
                leakInterval: TimeSpan.FromMinutes(5),
                behavior: RateLimitExceededBehavior.Reject,
                filters: new IncrementingNumberFilter()));
            var msg = new TestMessage { Service = si };

            // when
            await sut.Invoke(msg);

            // then
            var endEvent = adapter.Events
                .First(e => e.Component == "OnRateLimit" && e.Phase == TelemetryPhase.End);

            Assert.AreEqual(true, (bool)endEvent.Attributes["rejected"]);
            Assert.AreEqual(TelemetryOutcome.Exception, endEvent.Outcome);
        }

        [TestMethod]
        public async Task Should_respect_cancellation_token_while_waiting_in_delay_mode()
        {
            // given — full bucket with long leak interval
            var state = new RateLimiterState();
            lock (state.Lock)
            {
                state.Tokens.Enqueue(DateTimeOffset.UtcNow);
            }
            var cts = new CancellationTokenSource();
            var sut = new DataPipe<TestMessage>();
            // No ExceptionAspect — we want the cancellation to propagate
            sut.Add(new OnRateLimit<TestMessage>(state,
                capacity: 1,
                leakInterval: TimeSpan.FromMinutes(5), // won't drain naturally
                behavior: RateLimitExceededBehavior.Delay,
                filters: new IncrementingNumberFilter()));
            var msg = new TestMessage { Number = 0, CancellationToken = cts.Token };

            // when — cancel after a short delay
            cts.CancelAfter(TimeSpan.FromMilliseconds(50));

            // then — should throw TaskCanceledException from the Task.Delay
            await Assert.ThrowsExceptionAsync<TaskCanceledException>(async () => await sut.Invoke(msg));
            Assert.AreEqual(0, msg.Number, "Filter should not have executed");
        }

        [TestMethod]
        public async Task Should_handle_stopped_pipeline_within_rate_limited_filters()
        {
            // given
            var state = new RateLimiterState();
            var sut = new DataPipe<TestMessage>();
            sut.Use(new ExceptionAspect<TestMessage>());
            sut.Add(new OnRateLimit<TestMessage>(state,
                capacity: 10,
                leakInterval: TimeSpan.FromMilliseconds(100),
                filters: new CancellingFilter()));
            sut.Add(new IncrementingNumberFilter()); // should not execute
            var msg = new TestMessage { Number = 0 };

            // when
            await sut.Invoke(msg);

            // then — pipeline was stopped inside the rate-limited scope
            Assert.IsTrue(msg.Execution.IsStopped);
            Assert.AreEqual(0, msg.Number, "Subsequent filter should not have executed after stop");
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentOutOfRangeException))]
        public void Should_throw_when_rate_limiter_capacity_is_zero()
        {
            new OnRateLimit<TestMessage>(new RateLimiterState(),
                capacity: 0,
                leakInterval: TimeSpan.FromMilliseconds(100),
                filters: new NoOpFilter());
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentOutOfRangeException))]
        public void Should_throw_when_rate_limiter_capacity_is_negative()
        {
            new OnRateLimit<TestMessage>(new RateLimiterState(),
                capacity: -1,
                leakInterval: TimeSpan.FromMilliseconds(100),
                filters: new NoOpFilter());
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentOutOfRangeException))]
        public void Should_throw_when_rate_limiter_leak_interval_is_zero()
        {
            new OnRateLimit<TestMessage>(new RateLimiterState(),
                capacity: 10,
                leakInterval: TimeSpan.Zero,
                filters: new NoOpFilter());
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentOutOfRangeException))]
        public void Should_throw_when_rate_limiter_leak_interval_is_negative()
        {
            new OnRateLimit<TestMessage>(new RateLimiterState(),
                capacity: 10,
                leakInterval: TimeSpan.FromMilliseconds(-1),
                filters: new NoOpFilter());
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Should_throw_when_rate_limiter_state_is_null()
        {
            new OnRateLimit<TestMessage>(null!,
                capacity: 10,
                leakInterval: TimeSpan.FromMilliseconds(100),
                filters: new NoOpFilter());
        }

        // ──────────────────────────────────────────────────────────────────
        // FilterRunner refactoring coverage tests
        // ──────────────────────────────────────────────────────────────────

        [TestMethod]
        public async Task Should_emit_structural_end_with_exception_outcome_when_IfTrue_filter_throws()
        {
            // given
            var adapter = new TestTelemetryAdapter();
            var sut = new DataPipe<TestMessage> { TelemetryMode = TelemetryMode.PipelineAndFilters };
            sut.Use(new ExceptionAspect<TestMessage>());
            sut.Use(new TelemetryAspect<TestMessage>(adapter));
            sut.Add(new IfTrue<TestMessage>(_ => true, new ErroringFilter()));
            var msg = new TestMessage { Service = si };

            // when
            await sut.Invoke(msg);

            // then — IfTrue structural End event should have Exception outcome
            var ifTrueEnd = adapter.Events
                .First(e => e.Component == "IfTrue" && e.Phase == TelemetryPhase.End);
            Assert.AreEqual(TelemetryOutcome.Exception, ifTrueEnd.Outcome);
        }

        [TestMethod]
        public async Task Should_stop_mid_collection_in_ForEach()
        {
            // given — 4 children, stop after first
            var adapter = new TestTelemetryAdapter();
            var children = new List<ChildMessage>
            {
                new() { Id = 1, Value = 0 },
                new() { Id = 2, Value = 0 },
                new() { Id = 3, Value = 0 },
                new() { Id = 4, Value = 0 }
            };
            var sut = new DataPipe<TestMessage> { TelemetryMode = TelemetryMode.PipelineAndFilters };
            sut.Use(new ExceptionAspect<TestMessage>());
            sut.Use(new TelemetryAspect<TestMessage>(adapter));
            sut.Add(new ForEach<TestMessage, ChildMessage>(
                msg => msg.Children,
                new StopChildFilter()));  // stops on first item
            var msg = new TestMessage { Children = children, Service = si };

            // when
            await sut.Invoke(msg);

            // then — pipeline stopped, only one StopChildFilter telemetry event
            Assert.IsTrue(msg.Execution.IsStopped);
            var cancelEvents = adapter.Events
                .Where(e => e.Component == "StopChildFilter" && e.Phase == TelemetryPhase.End)
                .ToList();
            Assert.AreEqual(1, cancelEvents.Count, "Should only execute filter for first item");
        }

        [TestMethod]
        public async Task Should_emit_telemetry_for_single_filter_in_Policy()
        {
            // given
            var adapter = new TestTelemetryAdapter();
            var sut = new DataPipe<TestMessage> { TelemetryMode = TelemetryMode.PipelineAndFilters };
            sut.Use(new TelemetryAspect<TestMessage>(adapter));
            sut.Add(new Policy<TestMessage>(_ => new IncrementingNumberFilter()));
            var msg = new TestMessage { Number = 0, Service = si };

            // when
            await sut.Invoke(msg);

            // then — telemetry for the selected filter plus structural events
            Assert.AreEqual(1, msg.Number);
            var policyStart = adapter.Events
                .First(e => e.Component == "Policy" && e.Phase == TelemetryPhase.Start);
            Assert.IsTrue(policyStart.Attributes.ContainsKey("decision"));
            Assert.AreEqual("IncrementingNumberFilter", policyStart.Attributes["decision"]);

            var filterEnd = adapter.Events
                .First(e => e.Component == "IncrementingNumberFilter" && e.Phase == TelemetryPhase.End);
            Assert.AreEqual(TelemetryOutcome.Success, filterEnd.Outcome);
        }

        [TestMethod]
        public async Task Should_emit_structural_telemetry_for_Sequence()
        {
            // given
            var adapter = new TestTelemetryAdapter();
            var sut = new DataPipe<TestMessage> { TelemetryMode = TelemetryMode.PipelineAndFilters };
            sut.Use(new TelemetryAspect<TestMessage>(adapter));
            sut.Add(new Sequence<TestMessage>(
                new IncrementingNumberFilter(),
                new IncrementingNumberFilter()));
            var msg = new TestMessage { Number = 0, Service = si };

            // when
            await sut.Invoke(msg);

            // then — both filters executed, Start and End events for each child
            Assert.AreEqual(2, msg.Number);
            // Sequence has EmitTelemetryEvent=true so parent emits for it;
            // child filters should have their own Start/End events
            var childStarts = adapter.Events
                .Where(e => e.Component == "IncrementingNumberFilter" && e.Phase == TelemetryPhase.Start)
                .ToList();
            var childEnds = adapter.Events
                .Where(e => e.Component == "IncrementingNumberFilter" && e.Phase == TelemetryPhase.End)
                .ToList();
            Assert.AreEqual(2, childStarts.Count);
            Assert.AreEqual(2, childEnds.Count);
            Assert.IsTrue(childEnds.All(e => e.Outcome == TelemetryOutcome.Success));
        }

        [TestMethod]
        public async Task Should_emit_stopped_log_when_filter_stops_inside_structural()
        {
            // given
            var logs = new List<string>();
            var sut = new DataPipe<TestMessage>();
            sut.Use(new ExceptionAspect<TestMessage>());
            sut.Add(new Sequence<TestMessage>(
                new CancellingFilter(),
                new IncrementingNumberFilter()));  // should not run
            var msg = new TestMessage { Number = 0 };
            msg.OnLog = log => logs.Add(log);

            // when
            await sut.Invoke(msg);

            // then — STOPPED log was emitted, second filter did not execute
            Assert.AreEqual(0, msg.Number);
            Assert.IsTrue(logs.Any(l => l.Contains("STOPPED:")), "Expected a STOPPED log message");
        }

        [TestMethod]
        public async Task Should_log_non_zero_duration_for_top_level_filter_when_telemetry_is_off()
        {
            // given
            var logs = new List<string>();
            var sut = new DataPipe<TestMessage> { EnableTimings = true };
            sut.Use(new ExceptionAspect<TestMessage>());
            sut.Add(new DelayExecution<TestMessage>(TimeSpan.FromMilliseconds(30)));
            var msg = new TestMessage { OnLog = log => logs.Add(log) };

            // when
            await sut.Invoke(msg);

            // then
            var completed = logs.FirstOrDefault(l => l.StartsWith("COMPLETED: DelayExecution "));
            Assert.IsNotNull(completed, "Expected a completed log for DelayExecution");

            var openParen = completed!.LastIndexOf('(');
            var closeParen = completed.LastIndexOf("ms)", StringComparison.Ordinal);
            var durationText = completed.Substring(openParen + 1, closeParen - openParen - 1);
            Assert.IsTrue(long.TryParse(durationText, out var durationMs), $"Expected numeric duration in log: {completed}");
            Assert.IsTrue(durationMs > 0, $"Expected duration > 0ms, actual: {durationMs}ms");
        }

        [TestMethod]
        public async Task Should_log_non_zero_duration_for_structural_child_filter_when_telemetry_is_off()
        {
            // given
            var logs = new List<string>();
            var sut = new DataPipe<TestMessage> { EnableTimings = true };
            sut.Use(new ExceptionAspect<TestMessage>());
            sut.Add(new Sequence<TestMessage>(new SlowFilter(30)));
            var msg = new TestMessage { OnLog = log => logs.Add(log) };

            // when
            await sut.Invoke(msg);

            // then
            var completed = logs.FirstOrDefault(l => l.StartsWith("COMPLETED: SlowFilter "));
            Assert.IsNotNull(completed, "Expected a completed log for SlowFilter");

            var openParen = completed!.LastIndexOf('(');
            var closeParen = completed.LastIndexOf("ms)", StringComparison.Ordinal);
            var durationText = completed.Substring(openParen + 1, closeParen - openParen - 1);
            Assert.IsTrue(long.TryParse(durationText, out var durationMs), $"Expected numeric duration in log: {completed}");
            Assert.IsTrue(durationMs > 0, $"Expected duration > 0ms, actual: {durationMs}ms");
        }

        [TestMethod]
        public async Task Should_log_duration_when_telemetry_is_enabled_even_if_timings_flag_is_off()
        {
            // given
            var logs = new List<string>();
            var sut = new DataPipe<TestMessage> { TelemetryMode = TelemetryMode.PipelineAndFilters, EnableTimings = false };
            sut.Use(new ExceptionAspect<TestMessage>());
            sut.Use(new TelemetryAspect<TestMessage>(new TestTelemetryAdapter()));
            sut.Add(new DelayExecution<TestMessage>(TimeSpan.FromMilliseconds(30)));
            var msg = new TestMessage { OnLog = log => logs.Add(log), Service = si };

            // when
            await sut.Invoke(msg);

            // then
            var completed = logs.FirstOrDefault(l => l.StartsWith("COMPLETED: DelayExecution "));
            Assert.IsNotNull(completed, "Expected a completed log for DelayExecution");
            Assert.IsTrue(completed!.Contains("ms)"), $"Expected duration in log when telemetry is enabled: {completed}");
        }

        // ── LoggingAspect mode tests ──────────────────────────────────────────────

        [TestMethod]
        public async Task Should_log_start_steps_end_in_Full_mode()
        {
            // given
            var logger = new TestLogger();
            var sut = new DataPipe<TestMessage>();
            sut.Use(new ExceptionAspect<TestMessage>());
            sut.Use(new LoggingAspect<TestMessage>(logger, "FullTest", mode: PipeLineLogMode.Full));
            sut.Add(new IncrementingNumberFilter());
            var msg = new TestMessage { Number = 0 };

            // when
            await sut.Invoke(msg);

            // then — START, at least one step log, and END are present
            Assert.AreEqual(1, msg.Number);
            Assert.IsTrue(logger.Entries.Any(e => e.Message.Contains("START: FullTest")), "Expected START log");
            Assert.IsTrue(logger.Entries.Any(e => e.Message.Contains("END: FullTest")), "Expected END log");
            // Full mode hooks OnLog, so step messages from the pipeline appear
            Assert.IsTrue(logger.Entries.Count >= 3, $"Expected at least 3 log entries, got {logger.Entries.Count}");
        }

        [TestMethod]
        public async Task Should_log_start_end_only_in_StartEndOnly_mode()
        {
            // given
            var logger = new TestLogger();
            var sut = new DataPipe<TestMessage>();
            sut.Use(new ExceptionAspect<TestMessage>());
            sut.Use(new LoggingAspect<TestMessage>(logger, "StartEndTest", mode: PipeLineLogMode.StartEndOnly));
            sut.Add(new IncrementingNumberFilter());
            var msg = new TestMessage { Number = 0 };

            // when
            await sut.Invoke(msg);

            // then — START and END present, no step-level messages
            Assert.AreEqual(1, msg.Number);
            var startEntries = logger.Entries.Where(e => e.Message.Contains("START: StartEndTest")).ToList();
            var endEntries = logger.Entries.Where(e => e.Message.Contains("END: StartEndTest")).ToList();
            Assert.AreEqual(1, startEntries.Count, "Expected exactly one START log");
            Assert.AreEqual(1, endEntries.Count, "Expected exactly one END log");
            // No step logs — only START and END
            Assert.AreEqual(2, logger.Entries.Count, $"Expected exactly 2 log entries (START+END), got {logger.Entries.Count}");
        }

        [TestMethod]
        public async Task Should_log_errors_only_in_ErrorsOnly_mode()
        {
            // given
            var logger = new TestLogger();
            var sut = new DataPipe<TestMessage>();
            sut.Use(new ExceptionAspect<TestMessage>());
            sut.Use(new LoggingAspect<TestMessage>(logger, "ErrorsTest", mode: PipeLineLogMode.ErrorsOnly));
            sut.Add(new IncrementingNumberFilter());
            var msg = new TestMessage { Number = 0 };

            // when — no exception path
            await sut.Invoke(msg);

            // then — no logs at all on the happy path
            Assert.AreEqual(1, msg.Number);
            Assert.AreEqual(0, logger.Entries.Count, $"Expected no log entries in ErrorsOnly mode on success, got {logger.Entries.Count}");
        }

        [TestMethod]
        public async Task Should_log_error_in_ErrorsOnly_mode_on_exception()
        {
            // given
            var logger = new TestLogger();
            var sut = new DataPipe<TestMessage>();
            sut.Use(new ExceptionAspect<TestMessage>());
            sut.Use(new LoggingAspect<TestMessage>(logger, "ErrorsOnlyEx", mode: PipeLineLogMode.ErrorsOnly));
            sut.Add(new ErroringFilter());
            var msg = new TestMessage { Number = 0 };

            // when
            await sut.Invoke(msg);

            // then — only an error log entry
            Assert.IsTrue(logger.Entries.Any(e => e.Level == LogLevel.Error), "Expected an Error-level log when exception occurs in ErrorsOnly mode");
            Assert.IsFalse(logger.Entries.Any(e => e.Message.Contains("START:")), "No START log expected in ErrorsOnly mode");
            Assert.IsFalse(logger.Entries.Any(e => e.Message.Contains("END:")), "No END log expected in ErrorsOnly mode");
        }

        // ── Dispose tests ─────────────────────────────────────────────────────────

        [TestMethod]
        public void Should_clear_delegates_and_state_on_Dispose()
        {
            // given
            var msg = new TestMessage { Number = 42 };
            msg.OnError = (m, ex) => { };
            msg.OnStart = m => { };
            msg.OnComplete = m => { };
            msg.OnSuccess = m => { };
            msg.OnLog = s => { };
            msg.OnTelemetry = e => { };
            msg.State.Set("key", "value");

            // when
            msg.Dispose();

            // then — all lifecycle hooks cleared
            Assert.IsNull(msg.OnError);
            Assert.IsNull(msg.OnStart);
            Assert.IsNull(msg.OnComplete);
            Assert.IsNull(msg.OnSuccess);
            Assert.IsNull(msg.OnLog);
            Assert.IsNull(msg.OnTelemetry);
        }

        [TestMethod]
        public void Should_be_idempotent_on_double_Dispose()
        {
            // given
            var msg = new TestMessage { Number = 0 };

            // when
            msg.Dispose();
            msg.Dispose(); // second call should not throw

            // then — still cleared
            Assert.IsNull(msg.OnError);
            Assert.IsNull(msg.OnLog);
        }

        // ── Concurrency stress tests ──────────────────────────────────────────────

        [TestMethod]
        public async Task Should_handle_concurrent_pipeline_invocations()
        {
            // given — a shared pipeline, each invocation gets its own message
            var sut = new DataPipe<TestMessage>();
            sut.Use(new ExceptionAspect<TestMessage>());
            sut.Add(new IncrementingNumberFilter());
            sut.Add(new IncrementingNumberFilter());

            const int concurrency = 50;
            var messages = Enumerable.Range(0, concurrency)
                .Select(_ => new TestMessage { Number = 0 })
                .ToArray();

            // when — run all concurrently
            await Task.WhenAll(messages.Select(m => sut.Invoke(m)));

            // then — every message should have been incremented twice
            foreach (var msg in messages)
            {
                Assert.AreEqual(2, msg.Number, "Each message should pass through both filters");
                Assert.IsTrue(msg.IsSuccess, "Each message should succeed");
            }
        }

        [TestMethod]
        public async Task Should_isolate_state_across_concurrent_invocations()
        {
            // given — a filter that writes a unique value to State and captures it before Dispose
            var results = new System.Collections.Concurrent.ConcurrentDictionary<Guid, string>();
            var sut = new DataPipe<TestMessage>();
            sut.Use(new ExceptionAspect<TestMessage>());
            sut.Add(new LambdaFilter<TestMessage>(msg =>
            {
                msg.State.Set("id", msg.CorrelationId.ToString());
                return Task.CompletedTask;
            }));
            sut.Add(new LambdaFilter<TestMessage>(msg =>
            {
                // capture on a second filter so the first has fully completed
                results[msg.CorrelationId] = msg.State.Get<string>("id");
                return Task.CompletedTask;
            }));

            const int concurrency = 50;
            var messages = Enumerable.Range(0, concurrency)
                .Select(_ => new TestMessage { Number = 0 })
                .ToArray();

            // when
            await Task.WhenAll(messages.Select(m => sut.Invoke(m)));

            // then — each message's captured State held its own CorrelationId
            foreach (var msg in messages)
            {
                Assert.IsTrue(results.ContainsKey(msg.CorrelationId), "Result should be captured");
                Assert.AreEqual(msg.CorrelationId.ToString(), results[msg.CorrelationId],
                    "State should be isolated per message");
            }
        }
    }
}