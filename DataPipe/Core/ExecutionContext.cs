namespace DataPipe.Core
{
    /// <summary>
    /// Represents the execution state of a pipeline.
    /// A pipeline can be stopped, and a reason for stopping can be provided.
    /// This is NOT the same as cancellation via CancellationToken.
    /// </summary>
    public class ExecutionContext
    {
        public bool IsStopped { get; private set; }
        public string? Reason { get; private set; }

        private Dictionary<string, object>? _telemetryAnnotations;

        public Dictionary<string, object> TelemetryAnnotations =>
            _telemetryAnnotations ??= new();

        public bool HasTelemetryAnnotations =>
            _telemetryAnnotations != null && _telemetryAnnotations.Count != 0;

        public void ClearTelemetryAnnotations() =>
            _telemetryAnnotations?.Clear();

        public void Stop(string? reason = null)
        {
            IsStopped = true;
            Reason = reason;
        }

        public void Reset()
        {
            IsStopped = false;
            Reason = null;
        }
    }
}
