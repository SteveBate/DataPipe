using DataPipe.Core;
using DataPipe.Core.Filters;
using DataPipe.Core.Middleware;
using DataPipe.Sql.Filters;
using DataPipe.Tests.Support;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Net.Http;
using System.Threading.Tasks;

namespace DataPipe.Tests
{
    [TestClass]
    public class DataPipeTestFixture
    {
        [TestMethod]
        public void Should_arrange_aspects_in_correct_execution_order()
        {
            // given
            var sut = new DataPipe<TestMessage>();
            sut.Use(new ExceptionAspect<TestMessage>());
            sut.Use(new BasicLoggingAspect<TestMessage>(nameof(Should_arrange_aspects_in_correct_execution_order)));
            sut.Add(async m => {
                await Task.Delay(0);
            });

            // when
            var result = sut.ToString();

            // then
            Assert.AreEqual("ExceptionAspect`1 -> BasicLoggingAspect`1 -> DefaultAspect -> LambdaFilter`1", result);
        }

        [TestMethod]
        public async Task Should_execute_lambda_filters_indistinguishably_from_concrete_filters()
        {
            // given
            var sut = new DataPipe<TestMessage>();
            sut.Use(new ExceptionAspect<TestMessage>());
            sut.Use(new BasicLoggingAspect<TestMessage>(nameof(Should_execute_lambda_filters_indistinguishably_from_concrete_filters)));
            sut.Add(new LambdaFilter<TestMessage>(async m => { m.Number+= 1; }));
            sut.Add(async m => { m.Number += 1;});
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
            sut.Use(new BasicLoggingAspect<TestMessage>(nameof(Should_notify_when_started)));
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
            sut.Use(new BasicLoggingAspect<TestMessage>(nameof(Should_notify_when_successful)));
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
            sut.Use(new BasicLoggingAspect<TestMessage>(nameof(Should_notify_when_complete)));
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
            sut.Use(new BasicLoggingAspect<TestMessage>(nameof(Should_notify_error_when_using_exception_middleware)));
            sut.Add(new ErroringFilter());
            var msg = new TestMessage { OnError = (m, e) => m.StatusMessage = "error" };

            // when
            await sut.Invoke(msg);

            // then
            Assert.AreEqual("error", msg.StatusMessage);
            Assert.AreEqual(500, msg.StatusCode);
        }

        [TestMethod]
        public async Task Should_propagate_error_when_not_using_exception_middleware()
        {
            // given
            var sut = new DataPipe<TestMessage>();
            sut.Use(new BasicLoggingAspect<TestMessage>(nameof(Should_propagate_error_when_not_using_exception_middleware)));
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
            var sut = new DataPipe<TestMessage>();
            sut.Use(new ExceptionAspect<TestMessage>());
            sut.Use(new BasicLoggingAspect<TestMessage>(nameof(Should_retry_when_using_locally_composed_retry_and_transaction_filters)));
            sut.Add(new OnTimeoutRetry<TestMessage>(3,
                        new StartTransaction<TestMessage>(
                            new MockTimeoutErroringFilter())));
            var msg = new TestMessage { OnRetrying = (s) => Console.WriteLine("Retrying") };

            // when
            await sut.Invoke(msg);

            // then
            Assert.IsTrue(msg.Attempt == 3);
        }

        [TestMethod]
        public async Task Should_retry_with_custom_retry_and_default_delay_when_using_locally_composed_retry_and_transaction_filters()
        {
            const int MaxRetries = 3;

            // given
            var sut = new DataPipe<TestMessage>();
            sut.Use(new ExceptionAspect<TestMessage>());
            sut.Use(new BasicLoggingAspect<TestMessage>(nameof(Should_retry_with_custom_retry_and_default_delay_when_using_locally_composed_retry_and_transaction_filters)));
            sut.Add(
                new OnTimeoutRetry<TestMessage>(MaxRetries,
                retryWhen: (ex, msg) => ex is HttpRequestException,
                    new StartTransaction<TestMessage>(
                        new MockHttpErroringFilter())));
            var msg = new TestMessage { OnRetrying = (s) => Console.WriteLine("Retrying") };

            // when
            await sut.Invoke(msg);

            // then
            Assert.IsTrue(msg.Attempt == 3);
        }

        [TestMethod]
        public async Task Should_retry_with_custom_retry_and_custom_delay_when_using_locally_composed_retry_and_transaction_filters()
        {
            const int MaxRetries = 3;

            // given
            var sut = new DataPipe<TestMessage>();
            sut.Use(new ExceptionAspect<TestMessage>());
            sut.Use(new BasicLoggingAspect<TestMessage>(nameof(Should_retry_with_custom_retry_and_custom_delay_when_using_locally_composed_retry_and_transaction_filters)));
            sut.Add(
                new OnTimeoutRetry<TestMessage>(MaxRetries, 
                retryWhen: (ex, msg) => ex is HttpRequestException,
                customDelay: (attempt ,msg) => TimeSpan.FromMilliseconds(100 * attempt),
                    new StartTransaction<TestMessage>(
                        new MockHttpErroringFilter())));
            var msg = new TestMessage { OnRetrying = (s) => Console.WriteLine("Retrying") };

            // when
            await sut.Invoke(msg);

            // then
            Assert.IsTrue(msg.Attempt == 3);
        }

        /// Filters can also be composed inside a purpose-specific filter
        /// to achieve the same functionaluity as the test above
        [TestMethod]
        public async Task Should_retry_when_using_externally_composed_retry_and_transaction_filters()
        {
            // given
            var maxRetries = 3;
            var sut = new DataPipe<TestMessage>();
            sut.Use(new ExceptionAspect<TestMessage>());
            sut.Use(new BasicLoggingAspect<TestMessage>(nameof(Should_retry_when_using_externally_composed_retry_and_transaction_filters)));
            sut.Add(new ComposedRetryWithTransactionFilter<TestMessage>(maxRetries, new MockTimeoutErroringFilter()));
            var msg = new TestMessage { OnRetrying = (s) => Console.WriteLine("Retrying") };

            // when
            await sut.Invoke(msg);

            // then
            Assert.IsTrue(msg.Attempt == 3);
        }

        [TestMethod]
        public async Task Should_retry_and_recover_after_one_attempt_when_using_retry_filter()
        {
            // given
            var maxRetries = 3;
            var sut = new DataPipe<TestMessage>();
            sut.Use(new ExceptionAspect<TestMessage>());
            sut.Use(new BasicLoggingAspect<TestMessage>(nameof(Should_retry_and_recover_after_one_attempt_when_using_retry_filter)));
            sut.Add(new OnTimeoutRetry<TestMessage>(maxRetries,
                        new StartTransaction<TestMessage>(
                            new MockRecoveringTimeoutErroringFilter())));
            var msg = new TestMessage { OnRetrying = (s) => Console.WriteLine("Retrying") };

            // when
            await sut.Invoke(msg);

            // then
            Assert.IsTrue(msg.Attempt == 1);
        }

        [TestMethod]
        public async Task Should_not_succeed_when_pipeline_cancelled_manually()
        {
            // given
            var success = false;
            var sut = new DataPipe<TestMessage>();
            sut.Use(new BasicLoggingAspect<TestMessage>(nameof(Should_not_succeed_when_pipeline_cancelled_manually)));
            sut.Add(new CancellingFilter());
            sut.Add(new NoOpFilter());
            var msg = new TestMessage { OnSuccess = (m) => success = true };

            // when
            await sut.Invoke(msg);

            // then
            Assert.IsFalse(success);
        }

        [TestMethod]
        public async Task Should_report_reason_when_pipeline_cancelled_manually()
        {
            // given
            var sut = new DataPipe<TestMessage>();
            sut.Use(new BasicLoggingAspect<TestMessage>(nameof(Should_report_reason_when_pipeline_cancelled_manually)));
            sut.Add(new CancellingFilter());
            sut.Add(new NoOpFilter());
            var msg = new TestMessage();

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
            var sut = new DataPipe<TestMessage>();
            sut.Use(new BasicLoggingAspect<TestMessage>(nameof(Should_run_finally_filters_when_pipeline_succeeds)));
            sut.Add(new NoOpFilter());
            sut.Finally(new AlwaysRunFilter());
            var msg = new TestMessage();

            // when
            await sut.Invoke(msg);

            // then
            Assert.AreEqual("AlwaysRunFilter", msg.__Debug);
        }

        [TestMethod]
        public async Task Should_run_finally_filters_when_pipeline_cancelled()
        {
            // given
            var sut = new DataPipe<TestMessage>();
            sut.Use(new BasicLoggingAspect<TestMessage>(nameof(Should_run_finally_filters_when_pipeline_cancelled)));
            sut.Add(new CancellingFilter());
            sut.Finally(new AlwaysRunFilter());
            var msg = new TestMessage();

            // when
            await sut.Invoke(msg);

            // then
            Assert.AreEqual("AlwaysRunFilter", msg.__Debug);
        }

        [TestMethod]
        public async Task Should_run_finally_filters_when_pipeline_errors()
        {
            // given
            var sut = new DataPipe<TestMessage>();
            sut.Use(new ExceptionAspect<TestMessage>());
            sut.Use(new BasicLoggingAspect<TestMessage>(nameof(Should_run_finally_filters_when_pipeline_errors)));
            sut.Add(new ErroringFilter());
            sut.Finally(new AlwaysRunFilter());
            var msg = new TestMessage();

            // when
            await sut.Invoke(msg);

            // then
            Assert.AreEqual("AlwaysRunFilter", msg.__Debug);
        }

        [TestMethod]
        public async Task Should_run_message_through_filter_block_multiple_times_with_ForEach_Filter()
        {
            // given
            var sut = new DataPipe<TestMessage>();
            sut.Use(new BasicLoggingAspect<TestMessage>(nameof(Should_run_message_through_filter_block_multiple_times_with_ForEach_Filter)));

            // Each word flows through the same filter instance via the message
            sut.Add(
                new ForEach<TestMessage, string>(
                    msg => msg.Words,
                    (msg, s) => msg.Instance = s, new ConcatenatingFilter()
                )
            );
            var msg = new TestMessage { Words = ["this", "was", "constructed", "via", "multiple", "passes", "of", "the", "ForEach", "filter"] };

            // when
            await sut.Invoke(msg);

            // then
            Assert.AreEqual("this was constructed via multiple passes of the ForEach filter", msg.__Debug.TrimEnd());
        }

        [TestMethod]
        public async Task Should_repeat_filter_execution_until_pipeline_is_explicitly_stopped_by_user()
        {
            // given
            var sut = new DataPipe<TestMessage>();
            sut.Use(new BasicLoggingAspect<TestMessage>(nameof(Should_repeat_filter_execution_until_pipeline_is_explicitly_stopped_by_user)));
            sut.Add(
                new Repeat<TestMessage>(
                    new IncrementingNumberFilter(),
                    new IfTrue<TestMessage>(x => x.__Debug == "123",
                        new LambdaFilter<TestMessage>(x => 
                        {
                            x.Execution.Stop();
                            return Task.CompletedTask;
                        }))));
            var msg = new TestMessage();

            // when
            await sut.Invoke(msg);

            // then
            Assert.AreEqual("123", msg.__Debug.TrimEnd());
        }

        [TestMethod]
        public async Task Should_repeat_filter_execution_until_callback_condition_is_met()
        {
            // given
            var sut = new DataPipe<TestMessage>();
            sut.Use(new BasicLoggingAspect<TestMessage>(nameof(Should_repeat_filter_execution_until_callback_condition_is_met)));
            sut.Add(
                new RepeatUntil<TestMessage>(x => x.Number == 5,
                    new IncrementingNumberFilter()));
            var msg = new TestMessage { Number = 0 };

            // when
            await sut.Invoke(msg);

            // then
            Assert.AreEqual("12345", msg.__Debug.TrimEnd());
        }

        [TestMethod]
        public async Task Should_select_filter_to_execute_based_on_message_policy_using_if()
        {
            // given
            var sut = new DataPipe<TestMessage>();
            sut.Use(new BasicLoggingAspect<TestMessage>(nameof(Should_select_filter_to_execute_based_on_message_policy_using_if)));
            sut.Add(
                new Policy<TestMessage>(msg =>
                {
                    if (msg.Number == 0)
                        return new IncrementingNumberFilter();
                    else
                        return new DecrementingNumberFilter();
                }));
            var msg1 = new TestMessage { Number = 0 };
            var msg2 = new TestMessage { Number = 1 };

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
            var sut = new DataPipe<TestMessage>();
            sut.Use(new ExceptionAspect<TestMessage>()); // to handle out of range exception
            sut.Use(new BasicLoggingAspect<TestMessage>(nameof(Should_select_filter_to_execute_based_on_message_policy_using_switch)));
            sut.Add(
                new Policy<TestMessage>(msg => msg.Number switch
                {
                    0 => new IncrementingNumberFilter(),
                    1 => new DecrementingNumberFilter(),
                    _ => throw new ArgumentOutOfRangeException()
                }));
            var msg1 = new TestMessage { Number = 0 };
            var msg2 = new TestMessage { Number = 1 };
            var msg3 = new TestMessage { Number = 2 };

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
        public async Task Should_use_sequence_as_grouping_parent_for_multiple_filters_that_need_To_run_after_policy_decision()
        {
            // given
            var msg = new TestMessage { Number = 0 };
            var sut = new DataPipe<TestMessage>();
            sut.Use(new BasicLoggingAspect<TestMessage>(nameof(Should_use_sequence_as_grouping_parent_for_multiple_filters_that_need_To_run_after_policy_decision)));
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
        public async Task Should_execute_all_filters_using_overload_for_multiple_filters_without_need_for_grouping_parent_filter()
        {
            // given
            var sut = new DataPipe<TestMessage>();
            sut.Use(new BasicLoggingAspect<TestMessage>(nameof(Should_execute_all_filters_using_overload_for_multiple_filters_without_need_for_grouping_parent_filter)));
            sut.Add(
                new IncrementingNumberFilter(),
                new IncrementingNumberFilter(),
                new IncrementingNumberFilter());
            var msg = new TestMessage { Number = 0 };

            // when
            await sut.Invoke(msg);

            // then
            Assert.AreEqual(msg.Number, 3);
        }

        [TestMethod]
        public async Task Should_execute_all_filters_in_composite_filter_on_condition()
        {
            // given
            var sut = new DataPipe<TestMessage>();
            sut.Use(new BasicLoggingAspect<TestMessage>(nameof(Should_execute_all_filters_in_composite_filter_on_condition)));
            sut.Add(
                new IfTrue<TestMessage>(m => true,
                    new IncrementingNumberFilter(),
                    new IncrementingNumberFilter(),
                    new IncrementingNumberFilter()));
            var msg = new TestMessage { Number = 0 };

            // when
            await sut.Invoke(msg);

            // then
            Assert.AreEqual(msg.Number, 3);
        }

        [TestMethod]
        public async Task Should_bypass_execution_of_all_filters_in_composite_filter_on_condition()
        {
            // given
            var sut = new DataPipe<TestMessage>();
            sut.Use(new BasicLoggingAspect<TestMessage>(nameof(Should_bypass_execution_of_all_filters_in_composite_filter_on_condition)));
            sut.Add(
                new IfTrue<TestMessage>(m => false,
                    new IncrementingNumberFilter(),
                    new IncrementingNumberFilter(),
                    new IncrementingNumberFilter()));
            var msg = new TestMessage { Number = 0 };

            // when
            await sut.Invoke(msg);

            // then
            Assert.AreEqual(msg.Number, 0);
        }
    }
}
