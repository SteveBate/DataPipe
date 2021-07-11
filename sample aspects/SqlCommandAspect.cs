using System.Data.SqlClient;
using System.Threading.Tasks;
using DataPipe.Core;
using DataPipe.Extensions.Contracts;


/*
    For Sql Server connections, copy these two constructs to your project and add SqlCommandAspect to your DataPipe pipeline.
    
    SqlConnectionAspect - creates and opens a connection to sql server and makes it available throughout the current pipeline's execution context for any message implementing ISupportSql
*/

public interface ISupportSql
{
    string ConnectionString { get; set; }
    SqlCommand Command { get; set; }
}

public class SqlCommandAspect<T> : Aspect<T>, Filter<T> where T : BaseMessage, ISupportSql
{
    public async Task Execute(T msg)
    {
        using (var cn = new SqlConnection(msg.ConnectionString))
        {
            await cn.OpenAsync();
            using (msg.Command = cn.CreateCommand())
            {
                await Next.Execute(msg);
            }
        }
    }

    public Aspect<T> Next { get; set; }
}

