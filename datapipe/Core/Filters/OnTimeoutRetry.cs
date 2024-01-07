using System;
using System.Threading;
using System.Threading.Tasks;

namespace DataPipe.Core.Filters
{
    public class OnTimeoutRetry<T> : Filter<T> where T : BaseMessage
    {
        private readonly Filter<T>[] _filters;
        private int _maxRetries;

        public OnTimeoutRetry(int maxRetries, params Filter<T>[] filters)
        {
            _filters = filters;
            _maxRetries = maxRetries;
        }

        public async Task Execute(T msg)
        {
            msg.__MaxRetries = _maxRetries;
            msg.__Attempt = 1;
            
        start:
            try
            {
                foreach (var f in _filters)
                {
                    if (msg.CancellationToken.Stopped) break;

                    await f.Execute(msg);
                }
            }

            // specific TimeoutException
            catch (TimeoutException ex)
            {
                if (TryAgain(msg, ex)) goto start;

                throw;
            }

            // any other exception e.g. SqlException that has failed due to an inability to connect etc
            catch (Exception ex) when (ex.Message.Contains("transport-level error") || ex.Message.Contains("deadlocked") || ex.Message.Contains("timeout"))
            {
                if (TryAgain(msg, ex)) goto start;

                throw;
            }
        }

        private static bool TryAgain(T msg, Exception ex)
        {
            if (msg.__Attempt < msg.__MaxRetries)
            {
                msg.OnLog?.Invoke($"Failed to connect to {ex.Source} - retrying...");
                Thread.Sleep(msg.__Attempt * 2000); // sliding retry interval
                msg.__Attempt += 1;
                return true;
            }

            return false;
        }
    }    
}