using System;

namespace DataPipe.Core.Contracts
{
    public interface IAmRetryable
    {
        int Attempt { get; set; }
        int MaxRetries { get; set; }
        Action<int> OnRetrying { get; set; }
    }
}
