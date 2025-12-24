using System.Threading.Tasks;
using System.Transactions;
using DataPipe.Core;
using DataPipe.Core.Contracts;

namespace DataPipe.Sql.Filters
{

    /// <summary>
    /// A filter that wraps the execution of child filters within a database transaction scope.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This filter creates a new transaction scope with configurable isolation level and executes
    /// a chain of child filters within that transaction context. The transaction is only committed
    /// if the message's <see cref="BaseMessage.Commit"/> property is true at the end of filter execution.
    /// </para>
    /// <para>
    /// Key features:
    /// <list type="bullet">
    /// <item><description>Supports custom isolation levels (defaults to ReadCommitted)</description></item>
    /// <item><description>Enables async-friendly transaction flow</description></item>
    /// <item><description>Respects execution stop signals via <see cref="BaseMessage.Execution.IsStopped"/></description></item>
    /// <item><description>Provides logging callbacks for transaction lifecycle events</description></item>
    /// <item><description>Automatically rolls back if commit conditions are not met</description></item>
    /// </list>
    /// </para>
    /// </remarks>
    /// <typeparam name="T">The message type, constrained to inherit from BaseMessage and implement IAmCommittable</typeparam>
    public class StartTransaction<T> : Filter<T> where T : BaseMessage, IAmCommittable
    {
        private readonly IsolationLevel _isolationLevel;
        private readonly Filter<T>[] _filters;
        
        public StartTransaction(params Filter<T>[] filters) 
            : this(IsolationLevel.ReadCommitted, filters)
        {
        }

        public StartTransaction(IsolationLevel isolationLevel, params Filter<T>[] filters)
        {
            _filters = filters;
            _isolationLevel = isolationLevel;
        }

        public async Task Execute(T msg)
        {
            var options = new TransactionOptions { IsolationLevel = _isolationLevel, Timeout = TransactionManager.MaximumTimeout };

            using (var scope = new TransactionScope(TransactionScopeOption.RequiresNew, options, TransactionScopeAsyncFlowOption.Enabled))
            {
                msg.OnLog?.Invoke("TRANSACTION STARTED");

                try
                {
                    foreach (var f in _filters)
                    {
                        if (msg.Execution.IsStopped) break;

                        await f.Execute(msg);
                    }

                    if (msg.Commit)
                    {
                        scope.Complete();
                        msg.OnLog?.Invoke("TRANSACTION COMMITTED");
                    }
                    else
                    {
                        msg.OnLog?.Invoke("TRANSACTION ROLLED BACK (msg.Commit = false)");
                    }
                }
                catch (System.Exception ex)
                {
                    msg.OnLog?.Invoke($"TRANSACTION ROLLED (EXCEPTION: {ex.Message})");
                    throw;

                }
            }
        }
    }
}