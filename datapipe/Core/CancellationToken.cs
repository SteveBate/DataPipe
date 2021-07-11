namespace DataPipe.Core
{
    public class CancellationToken
    {
        public bool Stopped { get; private set; }

        public void Stop(string reason = "")
        {
            Reason = reason;
            Stopped = true;
        }

        public void Reset()
        {
            Reason = string.Empty;
            Stopped = false;
        }

        public string Reason { get; private set; }
    }
}
