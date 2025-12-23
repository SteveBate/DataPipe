using Microsoft.Data.SqlClient;

namespace DataPipe.Sql.Contracts
{
    public interface ISqlCommand
    {
        SqlCommand Command { get; set; }
    }
}
