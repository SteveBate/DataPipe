using DataPipe.Core.Contracts;

namespace DataPipe.Core.Telemetry.Policies;

/// <summary>
/// A telemetry policy that includes only events with a duration greater than or equal to the specified minimum duration in milliseconds.
/// </summary>
/// <param name="ms">value specifying the minimum duration in milliseconds</param>
public class MinimumDurationPolicy(long ms) : ITelemetryPolicy
{
    public bool ShouldInclude(TelemetryEvent evt) => evt.Duration >= TimeSpan.FromMilliseconds(ms).Milliseconds;
}
