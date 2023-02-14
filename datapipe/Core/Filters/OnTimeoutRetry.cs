using System;
using System.Threading;
using System.Threading.Tasks;
using DataPipe.Core.Contracts;

namespace DataPipe.Core.Filters
{
    public class OnTimeoutRetry<T> : Filter<T> where T : BaseMessage, IOnRetry
    {
        private readonly Filter<T>[] _filters;

        public OnTimeoutRetry(params Filter<T>[] filters)
        {
            _filters = filters;
        }

        public async Task Execute(T msg)
        {
            msg.Attempt = 1;
            
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
            if (msg.Attempt <= msg.MaxRetries)
            {
                msg.OnLog?.Invoke($"Failed to connect to {ex.Source} - retrying...");
                Thread.Sleep(msg.Attempt * 2000); // sliding retry interval
                msg.Attempt += 1;
                return true;
            }

            return false;
        }
    }    
}