using System;

namespace DataPipe.Core.Contracts
{
    /// <summary>
    /// Thrown when a circuit breaker is open and the request is rejected without executing the wrapped filters.
    /// ExceptionAspect maps this to a 503 Service Unavailable status code.
    /// </summary>
    public class CircuitBreakerOpenException : Exception
    {
        public CircuitBreakerOpenException()
            : base("Execution blocked by open circuit breaker.") { }

        public CircuitBreakerOpenException(string message)
            : base(message) { }

        public CircuitBreakerOpenException(string message, Exception innerException)
            : base(message, innerException) { }
    }
}
