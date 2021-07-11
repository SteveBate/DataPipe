using System;
using DataPipe.Core;
using DataPipe.Core.Contracts;

namespace DataPipe.Tests.Support
{
    class TestMessage : BaseMessage, IAmRetryable, IAmCommittable, IAttachState<string>
    {
        public int Attempt { get; set; }
        public bool LastAttempt { get; set; }
        public Action<int> OnRetrying { get; set; }
        public bool Commit { get; set; }
        public string ConnectionString { get; set; }
        public string Instance { get; set; }
    }
}
