using System;
using System.Globalization;
using System.Threading.Tasks;

namespace DataPipe.Core.Middleware
{
    /// <summary>
    /// BasicConsoleLoggingAspect invokes the BaseMessage OnLog delegate and simply writes to console.
    /// It serves as a basic example of a logging aspect to help you create custom logging aspects.
    /// </summary>
    public class BasicConsoleLoggingAspect<T> : Aspect<T>, Filter<T> where T : BaseMessage
    {
        private readonly string? title;
        private readonly bool stackTrace;

        public BasicConsoleLoggingAspect(string? title = null, bool stackTrace = false)
        {
            this.title = title;
            this.stackTrace = stackTrace;
            Next = default!;
        }

        public async Task Execute(T msg)
        {
            var previous = msg.OnLog;
            msg.OnLog = (s) =>
            {
                previous?.Invoke(s);
                Console.WriteLine($"{DateTime.Now.ToString(CultureInfo.CurrentCulture)} - {s}");
            };

            msg.OnLog?.Invoke($"START: {this.title ?? msg.GetType().Name}");
            try
            {
                await Next.Execute(msg);
            }
            catch (Exception ex)
            {
                msg.OnLog?.Invoke($"ERROR: {ex.Message}{(this.stackTrace ? ex.StackTrace : string.Empty)}");
                throw;
            }
            finally
            {
                msg.OnLog?.Invoke($"END: {this.title ?? msg.GetType().Name}" + Environment.NewLine);
                msg.OnLog = previous;
            }
        }

        public Aspect<T> Next { get; set; }
    }
}