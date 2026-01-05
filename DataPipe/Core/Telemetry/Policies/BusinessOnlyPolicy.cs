using DataPipe.Core.Contracts;

namespace DataPipe.Core.Telemetry.Policies;

/// <summary>
/// A telemetry policy that includes only business-related telemetry events.
/// </summary>
public class BusinessOnlyPolicy : ITelemetryPolicy
{
    public bool ShouldInclude(TelemetryEvent evt) => evt.Role == FilterRole.Business;
}
