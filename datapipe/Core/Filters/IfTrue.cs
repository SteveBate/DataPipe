using System;
using System.Threading.Tasks;

namespace DataPipe.Core.Filters
{
    /// <summary>
    /// IfTrue allows you to conditionally execute one or more filters i.e. branching logic within a pipeline.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public class IfTrue<T> : Filter<T> where T : BaseMessage
    {
        private readonly Func<T, bool> _callback;
        private readonly Filter<T>[] _filters;

        public IfTrue(Func<T, bool> callback, params Filter<T>[] filters)
        {
            _callback = callback;
            _filters = filters;
        }

        public async Task Execute(T msg)
        {
            if (_callback(msg))
            {
                foreach (var f in _filters)
                {
                    await f.Execute(msg);
                }
            }
        }
    }
}