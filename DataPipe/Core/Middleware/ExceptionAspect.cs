using System;
using System.Threading.Tasks;
using DataPipe.Core.Contracts;

namespace DataPipe.Core.Middleware
{
    /// <summary>
    /// ExceptionAspect catches unhandled exceptions from downstream filters, sets error status on the message, and invokes the OnError callback.
    /// A StatusCode of 500 is set to indicate an internal server error, and for convenience, similar to HTTP conventions.
    /// CircuitBreakerOpenException is mapped to 503 (Service Unavailable) to distinguish circuit breaker rejections from server errors.
    /// RateLimitRejectedException is mapped to 429 (Too Many Requests) to distinguish rate limit rejections from server errors.
    /// Alternatively, you can create custom exception aspects such as a DatabaseExceptionAspect that writes unhandled exceptions to a database log, etc.
    /// </summary>
    public class ExceptionAspect<T> : Aspect<T>, Filter<T> where T : BaseMessage
    {
        public async Task Execute(T msg)
        {
            try
            {
                await Next.Execute(msg).ConfigureAwait(false);
            }
            catch (CircuitBreakerOpenException ex)
            {
                msg.StatusCode = 503;
                msg.StatusMessage = ex.Message;
                msg?.OnError?.Invoke(msg, ex);
            }
            catch (RateLimitRejectedException ex)
            {
                msg.StatusCode = 429;
                msg.StatusMessage = ex.Message;
                msg?.OnError?.Invoke(msg, ex);
            }
            catch (Exception ex)
            {
                msg.StatusCode = 500;
                msg.StatusMessage = ex.Message;
                msg?.OnError?.Invoke(msg, ex);
            }
        }

        public Aspect<T> Next { get; set; } = default!;
    }
}