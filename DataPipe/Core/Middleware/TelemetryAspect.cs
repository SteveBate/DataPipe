using DataPipe.Core.Contracts;
using DataPipe.Core.Telemetry;

namespace DataPipe.Core.Middleware
{
    /// <summary>
    /// A minimal aspect that forwards telemetry events to a user-provided sink, in essence a router.
    /// Adapters can be created to forward telemetry to different outputs (console, file, Azure, OTel, etc.).
    /// </summary>
    public class TelemetryAspect<T> : Aspect<T>, Filter<T> where T : BaseMessage
    {
        private readonly ITelemetryAdapter _adapter;

        public TelemetryAspect(ITelemetryAdapter adapter)
        {
            _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
            Next = default!;
        }

        public async Task Execute(T msg)
        {
            var previousComplete = msg.OnComplete;
            msg.OnComplete = (e) =>
            {
                previousComplete?.Invoke(e); // call previous delegates
                _adapter.Flush();
            };

            // Preserve existing OnTelemetry
            var previous = msg.OnTelemetry;
            msg.OnTelemetry = e =>
            {
                previous?.Invoke(e);   // call previous delegates
                _adapter.Handle(e);    // send to adapter
            };

            try
            {
                await Next.Execute(msg);
            }
            finally
            {
                msg.OnTelemetry = previous;
                msg.OnComplete = previousComplete;
            }
        }

        public Aspect<T> Next { get; set; }
    }
}