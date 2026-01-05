using DataPipe.Core.Contracts;

namespace DataPipe.Core.Telemetry.Policies;

/// <summary>
/// A telemetry policy that captures all telemetry events. Applied by default if no other policy is specified.
/// </summary>
public class DefaultCaptureEverythingPolicy : ITelemetryPolicy
{
    public bool ShouldInclude(TelemetryEvent evt) => true;
}
