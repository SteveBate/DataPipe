using System.Threading.Tasks;

namespace DataPipe.Core.Filters
{
    public class CancelPipeline<T> : Filter<T> where T : BaseMessage
    {
        public Task Execute(T msg)
        {
            msg.CancellationToken.Stop();
            return Task.CompletedTask;
        }
    }
}