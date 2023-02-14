using System;

namespace DataPipe.Core.Contracts
{
    [Obsolete("Use the OnTimeoutRetry filter and IOnRetry interface instead")]
    public interface IAmRetryable
    {
        int Attempt { get; set; }
        bool LastAttempt { get; set; }
        Action<int> OnRetrying { get; set; }
    }
}
