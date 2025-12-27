using DataPipe.Core;
using DataPipe.EntityFramework.Contracts;
using Microsoft.EntityFrameworkCore;

namespace DataPipe.EntityFramework.Filters
{
    /// <summary>
    /// Opens a scoped DbContext for the duration of child filter execution.
    /// The DbContext is created via the provided factory and disposed automatically
    /// when all child filters have completed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Scoped Lifetime:</strong> The DbContext created by this filter has a strictly
    /// scoped lifetime. It is valid only during the execution of the child filters passed
    /// to this filter. Once <see cref="Execute"/> completes, the DbContext is disposed.
    /// </para>
    /// <para>
    /// <strong>Important:</strong> Child filters must not capture or store references to
    /// <c>msg.DbContext</c> for use after their <see cref="Filter{T}.Execute"/> method returns.
    /// Doing so will result in <see cref="ObjectDisposedException"/> when the reference is accessed.
    /// </para>
    /// <para>
    /// <strong>Correct usage:</strong> Perform all database operations synchronously within
    /// the filter's Execute method. Do not use fire-and-forget patterns (e.g., <c>Task.Run</c>
    /// without awaiting) that access the DbContext.
    /// </para>
    /// </remarks>
    /// <typeparam name="T">The message type, which must implement <see cref="IUseDbContext"/>.</typeparam>
    /// <example>
    /// <code>
    /// pipe.Add(new OpenDbContext&lt;OrderMessage&gt;(
    ///     msg => new AppDbContext(options),
    ///     new LoadOrder(),
    ///     new ValidateOrder(),
    ///     new SaveOrder()
    /// ));
    /// </code>
    /// </example>
    public class OpenDbContext<T> : Filter<T>
    where T : BaseMessage, IUseDbContext
    {
        private readonly Func<T, DbContext> _factory;
        private readonly Filter<T>[] _filters;

        public OpenDbContext(Func<T, DbContext> factory, params Filter<T>[] filters)
        {
            _factory = factory;
            _filters = filters;
        }

        public async Task Execute(T msg)
        {
            if (msg.DbContext != null)
            {
                msg.OnLog?.Invoke(
                    "WARNING: DbContext already set on message. Overwriting existing context.");
            }

            await using var context = _factory(msg);
            msg.DbContext = context;

            msg.OnLog?.Invoke("EF DbContext OPENED");

            foreach (var f in _filters)
            {
                if (msg.Execution.IsStopped) break;
                await f.Execute(msg);
            }

            msg.OnLog?.Invoke("EF DbContext CLOSED");
        }
    }

}
