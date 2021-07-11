using System.Threading.Tasks;
using DataPipe.Core;

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
}
