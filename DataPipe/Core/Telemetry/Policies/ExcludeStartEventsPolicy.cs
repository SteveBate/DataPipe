using DataPipe.Core.Contracts;

namespace DataPipe.Core.Telemetry.Policies
{
    /// <summary>
    /// A telemetry policy that excludes filter-level Start events to reduce telemetry volume.
    /// Pipeline-level Start and End events are always preserved. When <paramref name="excludeFilterStartEvents"/>
    /// is false, all events pass through unchanged.
    /// </summary>
    /// <param name="excludeFilterStartEvents">When true, filter-level Start events are excluded.</param>
    public class ExcludeStartEventsPolicy(bool excludeFilterStartEvents) : ITelemetryPolicy
    {
        public bool ShouldInclude(TelemetryEvent evt) => !excludeFilterStartEvents || evt.Scope == TelemetryScope.Pipeline || evt.Phase != TelemetryPhase.Start;
    }
}
