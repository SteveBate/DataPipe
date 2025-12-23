namespace DataPipe.Core
{
    /// <summary>
    /// Represents the execution state of a pipeline.
    /// A pipeline can be stopped, and a reason for stopping can be provided.
    /// This is NOT the same as cancellation via CancellationToken.
    /// </summary>
    public class PipelineExecution
    {
        public bool IsStopped { get; private set; }
        public string Reason { get; private set; }

        public void Stop(string reason = "")
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
