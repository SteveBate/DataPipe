using DataPipe.Core.Contracts;

namespace DataPipe.Core.Telemetry.Policies;

public sealed class CompositeTelemetryPolicy : ITelemetryPolicy
{
    private readonly IReadOnlyList<ITelemetryPolicy> _policies;

    public CompositeTelemetryPolicy(params ITelemetryPolicy[] policies)
    {
        _policies = policies;
    }

    public bool ShouldInclude(TelemetryEvent evt)
        => _policies.All(p => p.ShouldInclude(evt));
}
