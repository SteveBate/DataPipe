using DataPipe.Core.Contracts;

namespace DataPipe.Core.Telemetry.Policies;

/// <summary>
/// A telemetry policy that includes only structural-related telemetry events.
/// </summary>
public class StructuralOnlyPolicy : ITelemetryPolicy
{
    public bool ShouldInclude(TelemetryEvent evt) => evt.Role == FilterRole.Structural;
}
