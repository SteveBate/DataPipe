namespace DataPipe.Core.Contracts
{
    public interface IInstanceOf<T>
    {
         T Instance { get; set; }
    }
}