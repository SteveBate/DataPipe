using System.Threading.Tasks;

namespace DataPipe.Core
{
    /// <summary>
    /// Aspects observe the pipeline.
    /// They wrap execution of filters to provide cross-cutting concerns (logging, metrics, error handling, etc.)
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public interface Aspect<T> where T : BaseMessage
    {
        Task Execute(T msg);
        Aspect<T> Next { get; set; }
    }
}