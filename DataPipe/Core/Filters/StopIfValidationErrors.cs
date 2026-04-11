using System.Threading.Tasks;

namespace DataPipe.Core.Filters
{
    /// <summary>
    /// Stops the pipeline if any validation errors have been collected on the message.
    /// Place this filter after all validation filters to halt execution with a combined
    /// error message, allowing API consumers to see all validation failures at once.
    ///
    /// Example:
    ///    pipe.Add(new ValidateCustomerName());
    ///    pipe.Add(new ValidateCustomerEmail());
    ///    pipe.Add(new StopIfValidationErrors<RegisterCustomerMessage>());
    ///    pipe.Add(new SaveCustomer()); // only runs if no validation errors
    /// </summary>
    public sealed class StopIfValidationErrors<T> : Filter<T> where T : BaseMessage
    {
        private readonly int _statusCode;
        private readonly string _separator;

        /// <summary>
        /// Creates a StopIfValidationErrors filter.
        /// </summary>
        /// <param name="statusCode">The status code to set on the message when stopping. Default 400 (Bad Request).</param>
        /// <param name="separator">The separator used to join multiple validation errors. Default "; ".</param>
        public StopIfValidationErrors(int statusCode = 400, string separator = "; ")
        {
            _statusCode = statusCode;
            _separator = separator;
        }

        public Task Execute(T msg)
        {
            if (msg.HasValidationErrors)
            {
                var combined = string.Join(_separator, msg.ValidationErrors);
                msg.StatusCode = _statusCode;
                msg.Execution.Stop(combined);
            }

            return Task.CompletedTask;
        }
    }
}
