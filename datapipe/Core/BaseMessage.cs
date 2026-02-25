using System;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using System.Threading;
using DataPipe.Core.Telemetry;

[assembly: InternalsVisibleTo("DataPipe.Tests")]

namespace DataPipe.Core
{
    /// <summary>
    /// BaseMessage provides the bare minimum functionality required to use a DataPipe slice.
    /// It serves as the foundation for all messages processed within the pipeline.
    /// Derive from this class to create custom message types with additional properties and methods as needed for your application.
    /// </summary>
    public abstract class BaseMessage : IDisposable
    {
        public string? Actor { get; set; }
        public Guid CorrelationId { get; set; } = Guid.NewGuid();
        public ServiceIdentity? Service { get; set; }
        public string PipelineName { get; set; } = string.Empty;

        // Status information
        [JsonIgnore] public int StatusCode { get; set; } = 200;
        [JsonIgnore] public string StatusMessage { get; set; } = string.Empty;
        [JsonIgnore] public bool IsSuccess => StatusCode < 400;
        public void Fail(int code, string message)
        {
            StatusCode = code;
            StatusMessage = message;
        }

        // Async cancellation
        [JsonIgnore] public CancellationToken CancellationToken { get; set; }

        // Flow control (replaces old CancellationToken.Stopped)
        [JsonIgnore] public ExecutionContext Execution { get; } = new();

        // Lifecycle hooks
        [JsonIgnore] public Action<BaseMessage, Exception>? OnError = delegate { };
        [JsonIgnore] public Action<BaseMessage>? OnStart = delegate { };
        [JsonIgnore] public Action<BaseMessage>? OnComplete = delegate { };
        [JsonIgnore] public Action<BaseMessage>? OnSuccess = delegate { };
        [JsonIgnore] public Action<string>? OnLog = delegate { };
        [JsonIgnore] public Action<TelemetryEvent>? OnTelemetry = delegate { };

        // Telemetry configuration
        [JsonIgnore] public TelemetryMode TelemetryMode { get; internal set; } = TelemetryMode.Off;

        // Internals (for debugging)
        internal string? __Debug { get; set; }

        // general purpose tag, useful for stuffing with extra info
        public string Tag { get; set; } = string.Empty;

        // Constructor for async support
        protected BaseMessage()
        {
            CancellationToken = CancellationToken.None;
        }

        // Convenience property
        public bool ShouldStop => Execution.IsStopped || CancellationToken.IsCancellationRequested;

        // Helper method to check if telemetry should be emitted based on mode and event
        public bool ShouldEmitTelemetry(TelemetryEvent telemetryEvent)
        {
            return TelemetryMode switch
            {
                TelemetryMode.Off => false,
                TelemetryMode.PipelineOnly => telemetryEvent.Scope == TelemetryScope.Pipeline,
                TelemetryMode.PipelineAndErrors => telemetryEvent.Scope == TelemetryScope.Pipeline || 
                                                   telemetryEvent.Outcome == TelemetryOutcome.Exception,
                TelemetryMode.PipelineErrorsAndStops => telemetryEvent.Scope == TelemetryScope.Pipeline || 
                                                        telemetryEvent.Outcome == TelemetryOutcome.Exception ||
                                                        telemetryEvent.Outcome == TelemetryOutcome.Stopped,
                TelemetryMode.PipelineAndFilters => true,
                _ => false
            };
        }

        private bool _disposed = false;

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    // Clear all delegate references to allow GC
                    OnError = null;
                    OnStart = null;
                    OnComplete = null;
                    OnSuccess = null;
                    OnLog = null;
                    OnTelemetry = null;
                }
                _disposed = true;
            }
        }
    }
}