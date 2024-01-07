using DataPipe.Core;
using DataPipe.Core.Filters;
using DataPipe.Core.Middleware;
using DataPipe.Tests.Support;
using System;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace DataPipe.Tests
{
    public class DataPipeTestFixture
    {
        [Fact]
        public void Should_arrange_aspects_in_correct_execution_order()
        {
            // given
            var sut = new DataPipe<TestMessage>();
            sut.Use(new ExceptionAspect<TestMessage>());
            sut.Use(new BasicLoggingAspect<TestMessage>(nameof(Should_arrange_aspects_in_correct_execution_order)));
            sut.Run(new NoOpFilter());

            // when
            var result = sut.ToString();

            // then
            Assert.Equal("ExceptionAspect`1 -> BasicLoggingAspect`1 -> DefaultAspect -> NoOpFilter", result);
        }

        [Fact]
        public async Task Should_notify_when_started()
        {
            // given
            var sut = new DataPipe<TestMessage>();
            sut.Use(new BasicLoggingAspect<TestMessage>(nameof(Should_notify_when_started)));
            var msg = new TestMessage { OnStart = (m) => m.StatusMessage = "started" };

            // when
            await sut.Invoke(msg);

            // then
            Assert.Equal("started", msg.StatusMessage);
        }

        [Fact]
        public async Task Should_notify_when_successful()
        {
            // given
            var sut = new DataPipe<TestMessage>();
            sut.Use(new BasicLoggingAspect<TestMessage>(nameof(Should_notify_when_successful)));
            var msg = new TestMessage { OnSuccess = (m) => m.StatusMessage = "success" };

            // when
            await sut.Invoke(msg);

            // then
            Assert.Equal("success", msg.StatusMessage);
        }

        [Fact]
        public async Task Should_notify_when_complete()
        {
            // given
            var sut = new DataPipe<TestMessage>();
            sut.Use(new BasicLoggingAspect<TestMessage>(nameof(Should_notify_when_complete)));
            var msg = new TestMessage { OnComplete = (m) => m.StatusMessage = "complete" };

            // when
            await sut.Invoke(msg);

            // then
            Assert.Equal("complete", msg.StatusMessage);
        }

        [Fact]
        public async Task Should_notify_error_when_using_exception_middleware()
        {
            // given
            var sut = new DataPipe<TestMessage>();
            sut.Use(new ExceptionAspect<TestMessage>());
            sut.Use(new BasicLoggingAspect<TestMessage>(nameof(Should_notify_error_when_using_exception_middleware)));
            sut.Run(new ErroringFilter());
            var msg = new TestMessage { OnError = (m, e) => m.StatusMessage = "error" };

            // when
            await sut.Invoke(msg);

            // then
            Assert.Equal("error", msg.StatusMessage);
            Assert.Equal(500, msg.StatusCode);
        }

        [Fact]
        public async Task Should_propagate_error_when_not_using_exception_middleware()
        {
            // given
            var sut = new DataPipe<TestMessage>();
            sut.Use(new BasicLoggingAspect<TestMessage>(nameof(Should_propagate_error_when_not_using_exception_middleware)));
            sut.Run(new ErroringFilter());
            var msg = new TestMessage();

            // when
            await Assert.ThrowsAsync<NotImplementedException>(async () => await sut.Invoke(msg));
        }

        /// Filters can be composed inline to provide more complex functionality
        /// that affects only the filters within the current Run statement
        [Fact]
        public async Task Should_retry_when_using_locally_composed_retry_and_transaction_filters()
        {
            // given
            var sut = new DataPipe<TestMessage>();
            sut.Use(new ExceptionAspect<TestMessage>());
            sut.Use(new BasicLoggingAspect<TestMessage>(nameof(Should_retry_when_using_locally_composed_retry_and_transaction_filters)));            
            sut.Run(new OnTimeoutRetry<TestMessage>(2,
                        new StartTransaction<TestMessage>(
                            new MockTimeoutErroringFilter())));
            var msg = new TestMessage { OnRetrying = (s) => Console.WriteLine("Retrying"), MaxRetries = 2 };

            // when
            await sut.Invoke(msg);

            // then
            Assert.True(msg.Attempt == 3);
        }

        /// Filters can also be composed inside a purpose-specific filter
        /// to achieve the same functionaluity as the test above
        [Fact]
        public async Task Should_retry_when_using_externally_composed_retry_and_transaction_filters()
        {
            // given
            var sut = new DataPipe<TestMessage>();
            sut.Use(new ExceptionAspect<TestMessage>());
            sut.Use(new BasicLoggingAspect<TestMessage>(nameof(Should_retry_when_using_externally_composed_retry_and_transaction_filters)));            
            sut.Run(new ComposedRetryWithTransactionFilter<TestMessage>(new MockTimeoutErroringFilter()));
            var msg = new TestMessage { OnRetrying = (s) => Console.WriteLine("Retrying"), MaxRetries = 2 };

            // when
            await sut.Invoke(msg);

            // then
            Assert.True(msg.Attempt == 3);
        }

        [Fact]
        public async Task Should_retry_and_recover_after_one_attempt_when_using_RetryAspect()
        {
            // given
            var sut = new DataPipe<TestMessage>();
            sut.Use(new ExceptionAspect<TestMessage>());
            sut.Use(new BasicLoggingAspect<TestMessage>(nameof(Should_retry_and_recover_after_one_attempt_when_using_RetryAspect)));
            sut.Run(new OnTimeoutRetry<TestMessage>(3,
                        new StartTransaction<TestMessage>(
                            new MockRecoveringTimeoutErroringFilter())));
            var msg = new TestMessage { OnRetrying = (s) => Console.WriteLine("Retrying") };

            // when
            await sut.Invoke(msg);

            // then
            Assert.True(msg.Attempt == 1);
        }

        [Fact]
        public async Task Should_not_succeed_when_pipeline_cancelled_manually()
        {
            // given
            var success = false;
            var sut = new DataPipe<TestMessage>();
            sut.Use(new BasicLoggingAspect<TestMessage>(nameof(Should_not_succeed_when_pipeline_cancelled_manually)));
            sut.Run(new CancellingFilter());
            sut.Run(new NoOpFilter());
            var msg = new TestMessage { OnSuccess = (m) => success = true };

            // when
            await sut.Invoke(msg);

            // then
            Assert.False(success);
        }

        [Fact]
        public async Task Should_report_reason_when_pipeline_cancelled_manually()
        {
            // given
            var sut = new DataPipe<TestMessage>();
            sut.Use(new BasicLoggingAspect<TestMessage>(nameof(Should_report_reason_when_pipeline_cancelled_manually)));
            sut.Run(new CancellingFilter());
            sut.Run(new NoOpFilter());
            var msg = new TestMessage();

            // when
            await sut.Invoke(msg);

            // then
            Assert.True(msg.CancellationToken.Stopped);
            Assert.Contains("User cancelled operation", msg.CancellationToken.Reason);
        }    

        [Fact]
        public async Task Should_run_finally_filters_when_pipeline_succeeds()
        {
            // given
            var sut = new DataPipe<TestMessage>();
            sut.Use(new BasicLoggingAspect<TestMessage>(nameof(Should_run_finally_filters_when_pipeline_succeeds)));
            sut.Run(new NoOpFilter());
            sut.Finally(new AlwaysRunFilter());
            var msg = new TestMessage();

            // when
            await sut.Invoke(msg);

            // then
            Assert.Equal("AlwaysRunFilter", msg.Debug);
        }

        [Fact]
        public async Task Should_run_finally_filters_when_pipeline_cancelled()
        {
            // given
            var sut = new DataPipe<TestMessage>();
            sut.Use(new BasicLoggingAspect<TestMessage>(nameof(Should_run_finally_filters_when_pipeline_cancelled)));
            sut.Run(new CancellingFilter());
            sut.Finally(new AlwaysRunFilter());
            var msg = new TestMessage();

            // when
            await sut.Invoke(msg);

            // then
            Assert.Equal("AlwaysRunFilter", msg.Debug);
        }    
    
        [Fact]
        public async Task Should_run_finally_filters_when_pipeline_errors()
        {
            // given
            var sut = new DataPipe<TestMessage>();
            sut.Use(new ExceptionAspect<TestMessage>());
            sut.Use(new BasicLoggingAspect<TestMessage>(nameof(Should_run_finally_filters_when_pipeline_errors)));
            sut.Run(new ErroringFilter());
            sut.Finally(new AlwaysRunFilter());
            var msg = new TestMessage();

            // when
            await sut.Invoke(msg);

            // then
            Assert.Equal("AlwaysRunFilter", msg.Debug);
        }

        [Fact]
        public async Task Should_run_message_through_pipeline_multiple_times_with_ForEachAspect()
        {
            var words = new[] { "this", "was", "constructed", "via", "multiple", "passes", "of", "the", "pipeline" };

            // given
            var sut = new DataPipe<TestMessage>();
            sut.Use(new BasicLoggingAspect<TestMessage>(nameof(Should_run_message_through_pipeline_multiple_times_with_ForEachAspect)));
            sut.Use(new ForEachAspect<TestMessage, string>(() => words.Select(w => w)));
            sut.Run(new ConcatenatingFilter());
            var msg = new TestMessage();

            // when
            await sut.Invoke(msg);

            // then
            Assert.Equal("this was constructed via multiple passes of the pipeline", msg.Debug.TrimEnd());
        }

        [Fact]
        public async Task Should_run_message_through_pipeline_multiple_times_with_Repeat_filter()
        {
            // given
            var sut = new DataPipe<TestMessage>();
            sut.Use(new BasicLoggingAspect<TestMessage>(nameof(Should_run_message_through_pipeline_multiple_times_with_ForEachAspect)));
            sut.Run(new Repeat<TestMessage>(
                new IncrementingNumberFilter(),
                new IfTrue<TestMessage>(x => x.Debug == "123",
                    new CancelPipeline<TestMessage>())
            ));
            var msg = new TestMessage();

            // when
            await sut.Invoke(msg);

            // then
            Assert.Equal("123", msg.Debug.TrimEnd());
        }
    }
}
