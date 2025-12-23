namespace DataPipe.Sql.Contracts
{
    public interface IAmCommittable
    {
        bool Commit { get; set; }
    }
}
