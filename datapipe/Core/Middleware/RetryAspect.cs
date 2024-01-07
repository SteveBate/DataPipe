using System;
using System.Threading.Tasks;
using DataPipe.Core.Contracts;

namespace DataPipe.Core.Middleware
{
    /// <summary>
    /// RetryAspect - only retry for issues of contention or latency (represented by EnvironmentException type) 
    /// </summary>
    [Obsolete("Use the OnTimeoutRetry composable filter instead")]
    public class RetryAspect<T> : Aspect<T>, Filter<T> where T : BaseMessage, IAmRetryable
    {
        public RetryAspect(int maxRetries)
        {
            _maxRetries = maxRetries;
        }

        public async Task Execute(T msg)
        {
            try
            {
                await Next.Execute(msg);
            }
            catch (Exception ex)
            {
                if (ex.Message.Contains("transport-level error") || ex.Message.Contains("deadlocked") || ex.Message.Contains("timeout"))
                {
                    if (msg.__Attempt < _maxRetries)
                    {
                        msg.__Attempt++;
                        msg.OnLog?.Invoke($"Retry handler detected an environmental issue: {ex.Message}");
                        msg.OnLog?.Invoke($"Retry attempt {msg.__Attempt} in {(waitPeriod * msg.__Attempt) / 1000} seconds");
                        await Task.Delay(waitPeriod * msg.__Attempt);
                        msg.OnRetrying?.Invoke(msg.__Attempt);
                        msg.LastAttempt = msg.__Attempt == _maxRetries;
                        await Execute(msg);
                        msg.OnLog?.Invoke("Retry successful");
                    }
                    else
                    {
                        // no more attempts left, pass it on up the chain
                        throw;
                    }
                }
                else
                {
                    throw;
                }
            }
        }

        public Aspect<T> Next { get; set; }

        private int waitPeriod = 3000;

        private readonly int _maxRetries;
    }
}
