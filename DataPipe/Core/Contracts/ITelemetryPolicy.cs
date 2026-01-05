using DataPipe.Core.Telemetry;

namespace DataPipe.Core.Contracts;

public interface ITelemetryPolicy
{
    bool ShouldInclude(TelemetryEvent evt);
}