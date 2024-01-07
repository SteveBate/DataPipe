using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using DataPipe.Core.Contracts;

namespace DataPipe.Core.Middleware
{
    public class ForEachAspect<T,T2> : Aspect<T>, Filter<T> where T : BaseMessage, IInstanceOf<T2>
    {
        public ForEachAspect(Func<IEnumerable<T2>> callback)
        {
            _callback = callback;
        }

        public async Task Execute(T msg)
        {
            foreach (var retval in _callback())
            {
                msg.CancellationToken.Reset();
                msg.Instance = retval;
                await Next.Execute(msg);
            }
        }

        public Aspect<T> Next { get; set; }

        private readonly Func<IEnumerable<T2>> _callback;
    }
}