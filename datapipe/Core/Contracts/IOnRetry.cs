namespace DataPipe.Core.Contracts
{
    public interface IOnRetry
    {
        int Attempt { get; set; }
        int MaxRetries { get; set; }
    }
}