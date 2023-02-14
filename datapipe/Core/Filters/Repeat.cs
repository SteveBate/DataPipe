using System.Threading.Tasks;

namespace DataPipe.Core.Filters
{
    public class Repeat<T> : Filter<T> where T : BaseMessage
    {
        private readonly Filter<T>[] _filters;

        public Repeat(params Filter<T>[] filters)
        {
            _filters = filters;
        }

        public async Task Execute(T msg)
        {
            do
            {
                foreach (var f in _filters)
                {
                    if (msg.CancellationToken.Stopped) break;

                    await f.Execute(msg);
                }

            } while (!msg.CancellationToken.Stopped);

            msg.CancellationToken.Reset(); // for any filters that come after
        }
    }
}