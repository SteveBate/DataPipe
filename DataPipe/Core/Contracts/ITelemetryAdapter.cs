using DataPipe.Core.Telemetry;

namespace DataPipe.Core.Contracts;

public interface ITelemetryAdapter
{
    void Handle(TelemetryEvent evt);
    void Flush();
}
