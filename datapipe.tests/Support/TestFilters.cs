using System.Threading.Tasks;
using DataPipe.Core;
using DataPipe.Core.Contracts;
using DataPipe.Core.Filters;

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

    class MockRecoveringTimeoutErroringFilter : Filter<TestMessage>
    {
        public Task Execute(TestMessage msg)
        {
            if (msg.Attempt < 1)
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
            msg.CancellationToken.Stop("User cancelled operation");
            return Task.CompletedTask;
        }
    }

    class AlwaysRunFilter : Filter<TestMessage>
    {
        public Task Execute(TestMessage msg)
        {
            msg.Debug = "AlwaysRunFilter";
            return Task.CompletedTask;
        }
    }

    class ConcatenatingFilter : Filter<TestMessage>
    {
        public Task Execute(TestMessage msg)
        {
            msg.Debug += $"{msg.Instance} ";
            return Task.CompletedTask;
        }
    }

    class IncrementingNumberFilter : Filter<TestMessage>
    {
        public Task Execute(TestMessage msg)
        {
            msg.Number += 1;
            msg.Debug += msg.Number.ToString();
            return Task.CompletedTask;
        }
    }

    class ComposedRetryWithTransactionFilter<T> : Filter<T> where T : BaseMessage, IAmCommittable
    {
        private readonly Filter<T>[] _filters;
        private Filter<T> _scope;

        public ComposedRetryWithTransactionFilter(params Filter<T>[] filters)
        {
            _filters = filters;
            _scope = new OnTimeoutRetry<T>(3, new StartTransaction<T>(_filters));
        }

        public Task Execute(T msg)
        {
            _scope.Execute(msg);
            return Task.CompletedTask;
        }
    }
}
