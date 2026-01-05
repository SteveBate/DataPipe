using DataPipe.Core.Contracts;

namespace DataPipe.Core.Telemetry.Policies;

/// <summary>
/// A telemetry policy that suppresses all telemetry events except for error events related to a specific pipeline.
/// </summary>
/// <param name="name">value specifying the name of the pipeline</param>
public class SuppressAllExceptErrorsPolicy(string name) : ITelemetryPolicy
{
    public bool ShouldInclude(TelemetryEvent evt)
    {
        // suppress authorize start and end events except for errors
        if (evt.PipelineName == name && evt.Outcome != TelemetryOutcome.Exception && (evt.Phase == TelemetryPhase.Start || evt.Phase == TelemetryPhase.End))
        {
            return false;
        }

        return true;
    }
}
