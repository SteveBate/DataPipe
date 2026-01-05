using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DataPipe.Core.Telemetry
{
    public record TelemetryEvent
    {
        public Guid MessageId { get; init; }
        public string? Component { get; init; }
        public string? PipelineName { get; init; }
        public ServiceIdentity? Service { get; init; }
        public FilterRole Role { get; set; }
        public TelemetryOutcome? Outcome { get; set; }
        public TelemetryScope Scope { get; set; }
        public TelemetryPhase Phase { get; init; } // Started, Completed, Stopped, Faulted
        public DateTimeOffset Timestamp { get; init; }
        public string? Reason { get; init; }
        public Exception? Exception { get; init; }
        public long? Duration { get; set; }
        public IDictionary<string, object>? Attributes { get; init; } = new Dictionary<string, object>();
    }

    public enum FilterRole
    {
        None,
        Business,
        Structural
    }
    public enum TelemetryPhase
    {
        Start,
        End
    }
    public enum TelemetryScope
    {
        Filter,
        Pipeline
    }
    public enum TelemetryOutcome
    {
        None,
        Started,
        Success,
        Stopped,
        Exception
    }
}
