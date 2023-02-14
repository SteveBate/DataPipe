using System.Data.SqlClient;
using System.Threading.Tasks;
using DataPipe.Core;


/*
    For Sql Server connections using the System.Data.SqlClient nuget package, copy these two constructs to your project
    and add pipe.Run(new OpenSqlConnection( ...inner filters go here... )) as a filter to your DataPipe pipeline.
    
    OpenSqlConnection<T> - creates, opens, and closes a connection to sql server.
    It lives for as long as needed to run the inner filters instantiated in its constructor.

    The inner filters can access the SqlConnection instance because the message desecnds BaseMessage and MUST implement the
    ISupportSql interface.
*/

public interface ISupportSql
{
    string ConnectionString { get; set; }
    SqlCommand Command { get; set; }
}

public class OpenSqlConnection<T> : Filter<T> where T : BaseMessage, ISupportSql
{
    private readonly Filter<T>[] _filters;

    public OpenSqlConnection(params Filter<T>[] filters)
    {
        _filters = filters;
    }

    public async Task Execute(T msg)
    {
        using (var cnn = new SqlConnection(msg.ConnectionString))
        {
            await cnn.OpenAsync();
            using (msg.Command = cnn.CreateCommand())
            {
                foreach (var f in _filters)
                {
                    if (msg.CancellationToken.Stopped) break;

                    await f.Execute(msg);
                }
            }
        }
    }
}