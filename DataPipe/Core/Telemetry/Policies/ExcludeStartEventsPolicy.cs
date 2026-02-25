using DataPipe.Core.Contracts;

namespace DataPipe.Core.Telemetry.Policies
{
    /// <summary>
    /// A telemetry policy that excludes start events if specified.
    /// </summary>
    /// <param name="exclude"></param>
    public class ExcludeStartEventsPolicy(bool exclude) : ITelemetryPolicy
    {
        public bool ShouldInclude(TelemetryEvent evt) => !exclude || (evt.Scope == TelemetryScope.Pipeline && evt.DurationMs == null) || (evt.Phase != TelemetryPhase.Start);
    }
}
