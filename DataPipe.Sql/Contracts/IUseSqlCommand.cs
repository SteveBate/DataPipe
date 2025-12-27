using Microsoft.Data.SqlClient;

namespace DataPipe.Sql.Contracts
{
    public interface IUseSqlCommand
    {
        SqlCommand Command { get; set; }
    }
}
