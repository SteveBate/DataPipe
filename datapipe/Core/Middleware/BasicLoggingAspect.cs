using System;
using System.Globalization;
using System.Threading.Tasks;

namespace DataPipe.Core.Middleware
{
    /// <summary>
    /// BasicLoggingAspect invokes the BaseMessage OnStep delegate
    /// </summary>
    public class BasicLoggingAspect<T> : Aspect<T>, Filter<T> where T : BaseMessage
    {
        private readonly string title;

        public BasicLoggingAspect(string title = null)
        {
            this.title = title;
        }

        public async Task Execute(T msg)
        {
            msg.OnLog = (s) => Console.WriteLine($"{DateTime.Now.ToString(CultureInfo.CurrentCulture)} - {s}");

            msg.OnLog?.Invoke($"START - {this.title ?? msg.GetType().Name}");
            try
            {
                await Next.Execute(msg);
            }
            catch (Exception ex)
            {
                msg.OnLog?.Invoke($"{ex.Message}");
                throw;
            }
            finally
            {
                msg.OnLog?.Invoke($"END - {this.title ?? msg.GetType().Name}" + Environment.NewLine);
            }
        }

        public Aspect<T> Next { get; set; }
    }
}