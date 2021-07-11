using System;
using System.Threading.Tasks;

namespace DataPipe.Core.Middleware
{
    public class WhileTrueAspect<T> : Aspect<T>, Filter<T> where T : BaseMessage
    {
        private readonly Func<T, bool> _fn;

        public WhileTrueAspect(Func<T, bool> fn)
        {
            _fn = fn;
        }

        public async Task Execute(T msg)
        {
            while (_fn(msg))
            {
                msg.CancellationToken.Reset();
                await Next.Execute(msg);
            }
        }

        public Aspect<T> Next { get; set; }
    }
}