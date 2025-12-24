using DataPipe.Core;
using DataPipe.Sql.Contracts;
using Microsoft.Data.SqlClient;
using System.Threading.Tasks;

namespace DataPipe.Sql.Filters
{

    /// <summary>
    /// Manages the lifecycle of a SQL database connection and orchestrates the execution of chained filters.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This class is responsible for:
    /// <list type="bullet">
    /// <item><description>Opening a SQL connection using the provided connection string</description></item>
    /// <item><description>Creating a SQL command object associated with the connection</description></item>
    /// <item><description>Sequentially executing a chain of filters that process the command</description></item>
    /// <item><description>Properly disposing of resources (connection and command) when execution completes</description></item>
    /// </list>
    /// </para>
    /// <para>
    /// The filter chain execution respects the <see cref="BaseMessage.Execution"/> state, allowing
    /// any filter in the chain to signal early termination by setting <see cref="Execution.IsStopped"/> to true.
    /// </para>
    /// </remarks>
    /// <typeparam name="T">The message type that must inherit from <see cref="BaseMessage"/> and implement <see cref="ISqlCommand"/></typeparam>
    /// <param name="connectionString">The SQL connection string used to establish a database connection</param>
    /// <param name="filters">A variable-length array of filters to execute sequentially in the pipeline</param>
    public sealed class OpenSqlConnection<T>(string connectionString, params Filter<T>[] filters) : Filter<T> where T : BaseMessage, ISqlCommand
    {
        /// <summary>
        /// Asynchronously executes the filter chain with an open SQL connection and command.
        /// </summary>
        /// <param name="msg">The message object containing the SQL command context to be populated and processed</param>
        /// <returns>A task representing the asynchronous operation</returns>
        public async Task Execute(T msg)
        {
            using var cnn = new SqlConnection(connectionString);
            await cnn.OpenAsync(msg.CancellationToken);

            if (msg.Command != null)
            {
                msg.OnLog?.Invoke("WARNING: msg.Command was already set. Overwriting existing command.");
            }

            msg.OnLog?.Invoke("SQL CONNECTION OPENED");
            using (msg.Command = cnn.CreateCommand())
            {
                foreach (var f in filters)
                {
                    if (msg.Execution.IsStopped) break;

                    await f.Execute(msg);
                }
                msg.OnLog?.Invoke("SQL CONNECTION CLOSED");
            }
        }
    }
}
