using System;
using System.Threading.Tasks;

namespace DataPipe.Core.Middleware
{
    /// <summary>
    /// ExceptionAspect catches unhandled exceptions from downstream filters, sets error status on the message, and invokes the OnError callback.
    /// A StatusCode of 500 is set to indicate an internal server error, and for convenience, similar to HTTP conventions.
    /// Alternatively, you can create custom exception aspects such as a DatabaseExceptionAspect that writes unhandled exceptions to a database log.
    /// </summary>
    public class ExceptionAspect<T> : Aspect<T>, Filter<T> where T : BaseMessage
    {
        public async Task Execute(T msg)
        {
            try
            {
                await Next.Execute(msg);
            }
            catch (Exception ex)
            {
                msg.StatusCode = 500;
                msg.StatusMessage = ex.Message;
                msg?.OnError?.Invoke(msg, ex);
            }
        }

        public Aspect<T> Next { get; set; }
    }
}