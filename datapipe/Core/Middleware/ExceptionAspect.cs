using System;
using System.Threading.Tasks;

namespace DataPipe.Core.Middleware
{
    /// <summary>
    /// ExceptionAspect - handler to deal with unexpected errors. Invokes the OnError callback on the message if supplied
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