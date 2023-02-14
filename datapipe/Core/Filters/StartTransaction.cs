using System.Threading.Tasks;
using System.Transactions;
using DataPipe.Core.Contracts;

namespace DataPipe.Core.Filters
{
    public class StartTransaction<T> : Filter<T> where T : BaseMessage, IAmCommittable
    {
        private readonly Filter<T>[] _filters;

        public StartTransaction(params Filter<T>[] filters)
        {
            _filters = filters;
        }

        public async Task Execute(T msg)
        {
            var options = new TransactionOptions { IsolationLevel = IsolationLevel.ReadCommitted, Timeout = TransactionManager.MaximumTimeout };

            using (var scope = new TransactionScope(TransactionScopeOption.RequiresNew, options, TransactionScopeAsyncFlowOption.Enabled))
            {
                foreach (var f in _filters)
                {
                    if (msg.CancellationToken.Stopped) break;

                    await f.Execute(msg);
                }

                if (msg.Commit)
                {
                    scope.Complete();
                }
            }
        }
    }
}