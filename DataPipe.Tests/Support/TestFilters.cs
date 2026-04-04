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

    class AlwaysFailingFilter : Filter<TestMessage>
    {
        public Task Execute(TestMessage msg)
        {
            throw new InvalidOperationException("Simulated external failure");
        }
    }

    class FailNTimesThenSucceedFilter : Filter<TestMessage>
    {
        private int _callCount;
        private readonly int _failCount;

        public FailNTimesThenSucceedFilter(int failCount) => _failCount = failCount;

        public Task Execute(TestMessage msg)
        {
            _callCount++;
            if (_callCount <= _failCount)
                throw new InvalidOperationException($"Failure {_callCount}");

            msg.Number = _callCount;
            return Task.CompletedTask;
        }
    }

    class SlowFilter : Filter<TestMessage>
    {
        private readonly int _delayMs;

        public SlowFilter(int delayMs) => _delayMs = delayMs;

        public async Task Execute(TestMessage msg)
        {
            await Task.Delay(_delayMs, msg.CancellationToken);
            msg.Number += 1;
        }
    }

    class RecordExceptionFilter : Filter<TestMessage>
    {
        public Task Execute(TestMessage msg)
        {
            var ex = msg.State.Get<Exception>("TryCatch.Exception");
            msg.StatusMessage = $"caught:{ex.GetType().Name}";
            return Task.CompletedTask;
        }
    }

    // --- Child message filters for Parallel tests ---

    class IncrementChildValueFilter : Filter<ChildMessage>
    {
        public Task Execute(ChildMessage msg)
        {
            msg.Value += 1;
            return Task.CompletedTask;
        }
    }

    class SlowChildFilter : Filter<ChildMessage>
    {
        private readonly int _delayMs;

        public SlowChildFilter(int delayMs) => _delayMs = delayMs;

        public async Task Execute(ChildMessage msg)
        {
            await Task.Delay(_delayMs, msg.CancellationToken);
            msg.Value += 1;
        }
    }

    class FailingChildFilter : Filter<ChildMessage>
    {
        public Task Execute(ChildMessage msg)
        {
            throw new InvalidOperationException($"Child {msg.Id} failed");
        }
    }

    class FailOddChildFilter : Filter<ChildMessage>
    {
        public Task Execute(ChildMessage msg)
        {
            if (msg.Id % 2 != 0)
                throw new InvalidOperationException($"Odd child {msg.Id} failed");
            
            msg.Value += 1;
            return Task.CompletedTask;
        }
    }
}
