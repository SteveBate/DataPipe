using System.Threading.Tasks;

namespace DataPipe.Core
{
    public interface Aspect<T> where T : BaseMessage
    {
        Task Execute(T msg);
        Aspect<T> Next { get; set; }
    }
}