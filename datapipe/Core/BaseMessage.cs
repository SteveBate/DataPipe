using System;
using System.Text.Json.Serialization;

namespace DataPipe.Core
{
    /// <summary>
    /// BaseMessage provides the bare minimum functionality required to use a DataPipe slice
    /// </summary>
    public abstract class BaseMessage
    {
        [JsonIgnore] public int StatusCode { get; set; } = 200;
        [JsonIgnore] public string StatusMessage { get; set; } = string.Empty;
        [JsonIgnore] public CancellationToken CancellationToken { get; } = new CancellationToken();
        [JsonIgnore] public Action<BaseMessage, Exception> OnError = delegate { };
        [JsonIgnore] public Action<BaseMessage> OnStart = delegate { };
        [JsonIgnore] public Action<BaseMessage> OnComplete = delegate { };
        [JsonIgnore] public Action<BaseMessage> OnSuccess = delegate { };
        [JsonIgnore] public Action<string> OnLog = delegate { };
        internal string Debug { get; set; }
    }
}