using System;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using System.Threading;

[assembly: InternalsVisibleTo("datapipe.tests")]

namespace DataPipe.Core
{
    /// <summary>
    /// BaseMessage provides the bare minimum functionality required to use a DataPipe slice.
    /// It serves as the foundation for all messages processed within the pipeline.
    /// Derive from this class to create custom message types with additional properties and methods as needed for your application.
    /// </summary>
    public abstract class BaseMessage
    {
        // --- Status information ---
        [JsonIgnore] public int StatusCode { get; set; } = 200;
        [JsonIgnore] public string StatusMessage { get; set; } = string.Empty;
        [JsonIgnore] public bool IsSuccess => StatusCode < 400;
        public void Fail(int code, string message)
        {
            StatusCode = code;
            StatusMessage = message;
        }

        // --- Real async cancellation ---
        [JsonIgnore] public CancellationToken CancellationToken { get; internal set; }

        // --- Flow control (replaces old CancellationToken.Stopped) ---
        [JsonIgnore] public PipelineExecution Execution { get; } = new PipelineExecution();

        // --- Lifecycle hooks ---
        [JsonIgnore] public Action<BaseMessage, Exception> OnError = delegate { };
        [JsonIgnore] public Action<BaseMessage> OnStart = delegate { };
        [JsonIgnore] public Action<BaseMessage> OnComplete = delegate { };
        [JsonIgnore] public Action<BaseMessage> OnSuccess = delegate { };
        [JsonIgnore] public Action<string> OnLog = delegate { };

        // --- Internals (for debugging) ---
        internal string __Debug { get; set; }

        // --- Constructor for async support ---
        protected BaseMessage(CancellationToken? token = null)
        {
            CancellationToken = token ?? CancellationToken.None;
        }

        // Convenience property
        public bool ShouldStop => Execution.IsStopped || CancellationToken.IsCancellationRequested;
    }
}