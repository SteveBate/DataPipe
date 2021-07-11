using System;

namespace DataPipe.Core.Contracts
{
    public interface IAmRetryable
    {
        int Attempt { get; set; }
        bool LastAttempt { get; set; }
        Action<int> OnRetrying { get; set; }
    }
}
