using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using DataPipe.Core;
using DataPipe.Core.Contracts;
using DataPipe.Sql.Contracts;
using Microsoft.Data.SqlClient;

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

        // For Parallel tests — children to fan out to
        public List<ChildMessage> Children { get; set; } = [];

        // Thread-safe collection for gathering results from parallel branches
        public ConcurrentBag<int> Results { get; } = [];
    }

    class ChildMessage : BaseMessage
    {
        public int Id { get; set; }
        public int Value { get; set; }
        public string ParentConnectionString { get; set; }
    }

    class SqlTransactionTestMessage : BaseMessage, IUseSqlCommand, IAmCommittable
    {
        public SqlCommand Command { get; set; } = default!;
        public bool Commit { get; set; } = true;
    }
}
