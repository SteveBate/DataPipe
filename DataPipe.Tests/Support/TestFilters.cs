using DataPipe.Core;
using DataPipe.Core.Contracts;
using DataPipe.Core.Filters;
using DataPipe.Sql.Contracts;
using DataPipe.Sql.Filters;
using System;
using System.Net.Http;
using System.Threading.Tasks;

namespace DataPipe.Tests.Support
{
    class NoOpFilter : Filter<TestMessage>
    {
        public Task Execute(TestMessage msg)
        {
            // no op
            return Task.CompletedTask;
        }
    }

    class SetUpFilter : Filter<TestMessage>
    {
        public Task Execute(TestMessage msg)
        {
            msg.StatusMessage += "SetUp...";
            return Task.CompletedTask;
        }
    }

    class TearDownFilter : Filter<TestMessage>
    {
        public Task Execute(TestMessage msg)
        {
            msg.StatusMessage += "...TearDown";
            return Task.CompletedTask;
        }
    }

    class PrintFilter : Filter<TestMessage>
    {
        public Task Execute(TestMessage msg)
        {
            msg.StatusMessage += msg.Instance.ToString();
            return Task.CompletedTask;
        }
    }

    class ErroringFilter : Filter<TestMessage>
    {
        public Task Execute(TestMessage msg)
        {
            throw new System.NotImplementedException();
        }
    }

    class MockTimeoutErroringFilter : Filter<TestMessage>
    {
        public Task Execute(TestMessage msg)
        {
            throw new System.Exception("timeout");
        }
    }

    class MockHttpErroringFilter : Filter<TestMessage>
    {
        public Task Execute(TestMessage msg)
        {
            throw new HttpRequestException(HttpRequestError.InvalidResponse.ToString());
        }
    }

    class MockRecoveringTimeoutErroringFilter : Filter<TestMessage>
    {
        public Task Execute(TestMessage msg)
        {
            // Throw on first attempt, succeed on subsequent attempts
            if (msg.Attempt == 1)
            {
                throw new System.Exception("timeout");
            }

            return Task.CompletedTask;
        }
    }

    class CancellingFilter : Filter<TestMessage>
    {
        public Task Execute(TestMessage msg)
        {
            msg.Execution.Stop("User cancelled operation");
            return Task.CompletedTask;
        }
    }

    class AlwaysRunFilter : Filter<TestMessage>
    {
        public Task Execute(TestMessage msg)
        {
            msg.__Debug = "AlwaysRunFilter";
            return Task.CompletedTask;
        }
    }

    class ConcatenatingFilter : Filter<TestMessage>
    {
        public Task Execute(TestMessage msg)
        {
            msg.__Debug += $"{msg.Instance} ";
            return Task.CompletedTask;
        }
    }

    class IncrementingNumberFilter : Filter<TestMessage>
    {
        public Task Execute(TestMessage msg)
        {
            msg.Number += 1;
            msg.__Debug += msg.Number.ToString();
            return Task.CompletedTask;
        }
    }

    class DecrementingNumberFilter : Filter<TestMessage>
    {
        public Task Execute(TestMessage msg)
        {
            msg.Number -= 1;
            msg.__Debug += msg.Number.ToString();
            return Task.CompletedTask;
        }
    }

    class ComposedRetryWithTransactionFilter<T> : Filter<T> where T : BaseMessage, IAmCommittable, IAmRetryable
    {
        private readonly Filter<T>[] _filters;
        private Filter<T> _scope;

        public ComposedRetryWithTransactionFilter(int maxRetries, params Filter<T>[] filters)
        {
            _filters = filters;
            _scope = new OnTimeoutRetry<T>(maxRetries,
                null,
                customDelay: (attempt, msg) => TimeSpan.FromMilliseconds(10 * attempt),
                    new Sequence<T>(_filters));
        }

        public async Task Execute(T msg)
        {
            await _scope.Execute(msg);
        }
    }
}
