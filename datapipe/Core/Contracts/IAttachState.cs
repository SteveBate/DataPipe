namespace DataPipe.Core.Contracts
{
    public interface IAttachState<T>
    {
         T Instance { get; set; }
    }
}