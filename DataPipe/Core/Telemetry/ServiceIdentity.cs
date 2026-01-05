namespace DataPipe.Core.Telemetry
{
    public sealed class ServiceIdentity
    {
        public string Name { get; init; } = string.Empty;         // "Orders.Api"
        public string Environment { get; init; } = string.Empty;   // "Prod", "Staging"
        public string Version { get; init; } = string.Empty;       // "1.0"
        public string InstanceId { get; init; } = string.Empty;    // Optional (machine name / container id, etc)
    }
}