using System;
using DataPipe.Core;
using DataPipe.Core.Contracts;
using DataPipe.Sql.Contracts;

namespace DataPipe.Tests.Support
{
    class TestMessage : BaseMessage, IAmCommittable, IAmRetryable
    {
        // IAmRetryable implementation
        public int Attempt { get; set; }
        public int MaxRetries { get; set; }
        public Action<int> OnRetrying { get; set; }

        // IAmCommittable implementation
        public bool Commit { get; set; } = true;

        public string ConnectionString { get; set; }
        public string Instance { get; set; }
        public int Number { get; set; }
        public string[] Words { get; set; } = [];
    }
}
