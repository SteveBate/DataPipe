using System.Threading.Tasks;

namespace DataPipe.Core
{
    public interface Filter<T> where T : BaseMessage
    {
        Task Execute(T msg);
    }

    public class NullFilter<T> : Filter<T> where T : BaseMessage
    {
        public Task Execute(T msg)
        {
            return Task.CompletedTask;
        }        
    }
}