using System.Threading.Tasks;
using System.Transactions;
using DataPipe.Core.Contracts;

namespace DataPipe.Core.Middleware
{
    /// <summary>
    /// TransactionAspect runs a slice under a single transaction. If everything goes well and at no time did a filter mark the message as stopped
    /// then the transaction will be commited at the end of the slice. If an exception occurs the transaction is automatically rolled back and the
    /// exception is passed on up the chain
    /// </summary>
    public class TransactionAspect<T> : Aspect<T>, Filter<T> where T : BaseMessage, IAmCommittable
    {
        private readonly bool _enlist;

        public TransactionAspect(bool enlist = false)
        {
            _enlist = enlist;
        }

        public async Task Execute(T msg)
        {
            try
            {
                var options = new TransactionOptions { IsolationLevel = IsolationLevel.ReadCommitted, Timeout = TransactionManager.MaximumTimeout };

                using (var scope = new TransactionScope(_enlist ? TransactionScopeOption.Required : TransactionScopeOption.RequiresNew, options, TransactionScopeAsyncFlowOption.Enabled))
                {
                    await Next.Execute(msg);
                    if (!msg.CancellationToken.Stopped && msg.Commit)
                    {
                        scope.Complete();
                    }
                }
            }
            catch (TransactionAbortedException)
            {
                // NO-OP - this exception is commonly thrown even when a scope completes correctly so silently handle it
            }
        }

        public Aspect<T> Next { get; set; }
    }
}
