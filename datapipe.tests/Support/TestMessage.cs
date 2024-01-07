using System;
using DataPipe.Core;
using DataPipe.Core.Contracts;

namespace DataPipe.Tests.Support
{
    class TestMessage : BaseMessage, IAmCommittable, IAttachState<string>
    {
        public Action<int> OnRetrying { get; set; }
        public bool Commit { get; set; }
        public string ConnectionString { get; set; }
        public string Instance { get; set; }
        public int Number { get; set; }
    }
}
