using DataPipe.Core;
using DataPipe.Core.Contracts;
using DataPipe.EntityFramework.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using System;
using System.Data;

namespace DataPipe.EntityFramework.Filters
{
    /// <summary>
    /// Wraps child filters in an Entity Framework transaction.
    /// Commits only if <c>msg.Commit</c> is true after all child filters complete successfully.
    /// Rolls back on exception or when <c>msg.Commit</c> is false.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Transaction Scope:</strong> The transaction is active only during the execution
    /// of the child filters. It is committed or rolled back before this filter returns.
    /// </para>
    /// <para>
    /// <strong>Scoped Resources:</strong> Like the DbContext provided by <see cref="OpenDbContext{T}"/>,
    /// the transaction should not be captured or accessed after the filter chain completes.
    /// All transactional work must occur within the synchronous-async flow of child filter execution.
    /// </para>
    /// <para>
    /// <strong>Commit Control:</strong> Set <c>msg.Commit = true</c> within your filters to signal
    /// that the transaction should be committed. If <c>msg.Commit</c> remains false (or is set to false),
    /// the transaction will be explicitly rolled back.
    /// </para>
    /// <para>
    /// <strong>Exception Handling:</strong> If any child filter throws an exception, the transaction
    /// is rolled back and the exception is re-thrown to be handled by upstream aspects or the caller.
    /// </para>
    /// <para>
    /// <strong>Non-Relational Providers:</strong> For non-relational EF providers (e.g., Cosmos DB),
    /// transactions are skipped and child filters execute without transactional wrapping.
    /// </para>
    /// </remarks>
    /// <typeparam name="T">The message type, which must implement both <see cref="IAmCommittable"/> and <see cref="IUseDbContext"/>.</typeparam>
    /// <example>
    /// <code>
    /// pipe.Run(new OpenDbContext&lt;OrderMessage&gt;(
    ///     msg => new AppDbContext(options),
    ///     new StartEfTransaction&lt;OrderMessage&gt;(
    ///         new ProcessOrder(),
    ///         new UpdateInventory(),
    ///         new MarkCommit()  // Sets msg.Commit = true
    ///     )
    /// ));
    /// </code>
    /// </example>
    public class StartEfTransaction<T> : Filter<T>
        where T : BaseMessage, IAmCommittable, IUseDbContext
    {
        private readonly IsolationLevel _isolationLevel;
        private readonly Filter<T>[] _filters;

        public StartEfTransaction(params Filter<T>[] filters)
            : this(IsolationLevel.ReadCommitted, filters)
        {
        }

        public StartEfTransaction(IsolationLevel isolationLevel, params Filter<T>[] filters)
        {
            _isolationLevel = isolationLevel;
            _filters = filters;
        }

        public async Task Execute(T msg)
        {
            var context = msg.DbContext
                ?? throw new InvalidOperationException("DbContext not available on message.");

            if (context.Database.IsRelational())
            {
                var transaction = await context.Database.BeginTransactionAsync(_isolationLevel, msg.CancellationToken);

                msg.OnLog?.Invoke($"EF TRANSACTION STARTED ({_isolationLevel})");
                try
                {
                    foreach (var f in _filters)
                    {
                        if (msg.Execution.IsStopped) break;
                        await f.Execute(msg);
                    }

                    if (msg.Commit)
                    {
                        if (msg.CancellationToken.IsCancellationRequested)
                        {
                            throw new OperationCanceledException("Operation was cancelled before committing transaction.");
                        }
                        
                        await transaction.CommitAsync(msg.CancellationToken);
                        msg.OnLog?.Invoke("EF TRANSACTION COMMITTED");
                    }
                    else
                    {
                        await transaction.RollbackAsync(msg.CancellationToken);
                        msg.OnLog?.Invoke("EF TRANSACTION ROLLED BACK (msg.Commit = false)");
                    }
                }
                catch (Exception ex)
                {
                    await transaction.RollbackAsync(msg.CancellationToken);
                    msg.OnLog?.Invoke($"EF TRANSACTION ROLLED BACK (exception: {ex.Message})");
                    throw;
                }
            }
            else
            {
                // Non-relational provider: just run the filters without transaction
                msg.OnLog?.Invoke("EF TRANSACTION SKIPPED (non-relational provider)");
                foreach (var f in _filters)
                {
                    if (msg.Execution.IsStopped) break;
                    await f.Execute(msg);
                }
            }
        }
    }
}