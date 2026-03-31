using System;
using System.Collections.Generic;

namespace DataPipe.Core.Contracts
{
    /// <summary>
    /// Shared state for the OnRateLimit structural filter, implementing a leaky bucket.
    /// Designed to be shared across pipeline invocations — in a Web API, register as
    /// a singleton in DI so all pipelines hitting the same resource share the same bucket.
    ///
    /// Each instance represents one rate-limited resource (e.g. one external API, one database).
    /// Use separate instances for separate resources.
    /// </summary>
    public class RateLimiterState
    {
        internal readonly object Lock = new();
        internal readonly Queue<DateTimeOffset> Tokens = new();

        /// <summary>
        /// Current number of unconsumed tokens (pending requests) in the bucket.
        /// </summary>
        public int CurrentQueueDepth
        {
            get { lock (Lock) { return Tokens.Count; } }
        }
    }
}
