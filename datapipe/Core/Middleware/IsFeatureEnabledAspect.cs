using System.Threading.Tasks;
using DataPipe.Core.Contracts;

namespace DataPipe.Core.Middleware
{
    public class IsFeatureEnabledAspect<T, TS> : Aspect<T>, Filter<T> where T : BaseMessage, IAttachState<TS>
    {
        public IsFeatureEnabledAspect(string propName)
        {
            _propName = propName;
        }

        public async Task Execute(T msg)
        {
            var propertyExists = msg.Instance.GetType().GetProperty($"{_propName}");
            if (propertyExists != null && propertyExists.PropertyType == typeof(bool) && (bool)propertyExists.GetValue(msg.Instance, null))
            {
                await Next.Execute(msg);
            }
        }

        public Aspect<T> Next { get; set; }

        private readonly string _propName;
    }
}