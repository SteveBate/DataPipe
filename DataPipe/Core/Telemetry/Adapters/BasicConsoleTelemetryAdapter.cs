
using DataPipe.Core.Contracts;

namespace DataPipe.Core.Telemetry.Adapters;

/// <summary>
/// A telemetry adapter that outputs telemetry events to the console using their string representation.
/// </summary>
public sealed class BasicConsoleTelemetryAdapter : ITelemetryAdapter
{
    public void Flush()
    {
        Console.WriteLine("--- Telemetry Flush ---");
    }

    public void Handle(TelemetryEvent evt)
    {
        if (evt.Phase == TelemetryPhase.Start)
        {
            Console.WriteLine($"[{evt.Timestamp:HH:mm:ss.fff}] [{evt.MessageId}] - {evt.Component} - {evt.Scope}/{evt.Phase} - Role: {evt.Role}");
        }
        else
        {
            Console.WriteLine($"[{evt.Timestamp:HH:mm:ss.fff}] [{evt.MessageId}] - {evt.Component} - {evt.Scope}/{evt.Phase} - Role: {evt.Role} - Outcome: {evt.Outcome} - Attributes: {string.Join(", ", evt.Attributes.Select(kv => $"{kv.Key}: {kv.Value}"))} - Duration: {evt.Duration}ms");
        }
    }
}