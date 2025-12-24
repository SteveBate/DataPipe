using Microsoft.EntityFrameworkCore;

namespace DataPipe.EntityFramework.Contracts
{
    public interface IUseDbContext
    {
        DbContext DbContext { get; set; }
    }
}
