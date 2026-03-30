using System;

namespace DataPipe.Core.Contracts
{
    public enum CircuitState { Closed, Open, HalfOpen }

    public class CircuitBreakerState
    {
        internal readonly object Lock = new();
        public CircuitState Status { get; set; } = CircuitState.Closed;
        public int FailureCount { get; set; }
        public DateTimeOffset? LockedUntil { get; set; }
    }
}
