using System.Threading.Tasks;

namespace DataPipe.Core
{
    /// <summary>
    /// Filters control execution.
    /// They receive a message, perform work on it, and can modify its state.
    /// All Filters are fully asynchronous, stateless and thread-safe meaning a pipeline is inherently concurrent.
    /// </summary>
    /// <typeparam name="T">The type of message being processed.</typeparam>
    public interface Filter<T> where T : BaseMessage
    {
        Task Execute(T msg);
    }


}