using System.Threading.Tasks;
using DataPipe.Core;
using MongoDB.Driver;


/*
    For MongoDB connections, copy this class to your project and add MongoDBAspect to your DataPipe pipeline.

    MongoDBAspect provides a connection to an IMongoDatabase instance and makes it available through the rest of the pipeline
*/
public class MongoDBAspect<T> : Aspect<T>, Filter<T> where T : YourApplicationContext // extends BaseMessage
{
    private readonly IMongoDatabase _db;

    public MongoDBAspect(IMongoDatabase db)
    {            
        _db = db;
    }

    public async Task Execute(T msg)
    {
        msg.Db = _db;
        await Next.Execute(msg);
    }

    public Aspect<T> Next { get; set; }
}