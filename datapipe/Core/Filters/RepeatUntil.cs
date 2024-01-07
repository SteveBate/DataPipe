using System;
using System.Threading.Tasks;

namespace DataPipe.Core.Filters
{
    public class RepeatUntil<T> : Filter<T> where T : BaseMessage
    {
        private readonly Filter<T>[] _filters;
        private Func<T, bool> _callback;

        public RepeatUntil(Func<T, bool> callback, params Filter<T>[] filters)
        {
            _filters = filters;
            _callback = callback;
        }

        public async Task Execute(T msg)
        {
            if (!_callback(msg))
            {
                do
                {
                    foreach (Filter<T> filter in _filters)
                    {
                        if (msg.CancellationToken.Stopped)
                        {
                            break;
                        }

                        await filter.Execute(msg);
                    }
                }
                while (!msg.CancellationToken.Stopped);

                msg.CancellationToken.Reset();
            }
        }
    }
}