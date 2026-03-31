namespace DataPipe.Core.Contracts
{
    /// <summary>
    /// Controls behaviour when the rate limit bucket is full.
    /// </summary>
    public enum RateLimitExceededBehavior
    {
        /// <summary>
        /// Wait until capacity is available before executing. Applies backpressure to the caller.
        /// </summary>
        Delay,

        /// <summary>
        /// Immediately reject the request with a 429 status and stop the pipeline.
        /// </summary>
        Reject
    }
}
