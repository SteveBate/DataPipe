using System;

namespace DataPipe.Core.Contracts
{
    /// <summary>
    /// Thrown when a rate limiter rejects a request because the bucket is full
    /// and the behaviour is set to Reject.
    /// ExceptionAspect maps this to a 429 Too Many Requests status code.
    /// </summary>
    public class RateLimitRejectedException : Exception
    {
        public RateLimitRejectedException()
            : base("Execution blocked by rate limiter.") { }

        public RateLimitRejectedException(string message)
            : base(message) { }

        public RateLimitRejectedException(string message, Exception innerException)
            : base(message, innerException) { }
    }
}
