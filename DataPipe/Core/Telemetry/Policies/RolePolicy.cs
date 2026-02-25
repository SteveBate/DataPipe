using DataPipe.Core.Contracts;

namespace DataPipe.Core.Telemetry.Policies;

/// <summary>
/// A telemetry policy that includes only business-related telemetry events.
/// </summary>
public class RolePolicy(TelemetryRole role) : ITelemetryPolicy
{
    public bool ShouldInclude(TelemetryEvent evt) => role switch
    {
        TelemetryRole.All => true,
        TelemetryRole.Business => evt.Role == FilterRole.Business || evt.Scope == TelemetryScope.Pipeline,
        TelemetryRole.Structural => evt.Role == FilterRole.Structural || evt.Scope == TelemetryScope.Pipeline,
        _ => false,
    };
}
