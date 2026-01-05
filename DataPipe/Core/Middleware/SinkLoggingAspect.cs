using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace DataPipe.Core.Middleware
{
    public enum PipeLineLogMode
    {
        Full,          // Start, steps, end, errors
        StartEndOnly,  // Start + end + errors
        ErrorsOnly     // Errors only
    }

    /// <summary>
    /// SinkLoggingAspect is an ILogger based aspect that writes to a configured logger in appsettings.
    /// It can be used with any ILogger provider, such as Console, File, Seq, Application Insights, etc.
    /// Pass a title to identify the executing pipeline, and a log level for how verbose messages should be. 
    /// Any telemtry data is ignored due to the IsTelemetry = false scope.
    /// The BaseMessage Tag property allows data not specically included in the scope by setting msg.Tag before entering the pipeline.
    /// </summary>
    public class SinkLoggingAspect<T> : Aspect<T>, Filter<T> where T : BaseMessage 
    {
        private readonly ILogger _logger;
        private readonly string _title;
        private readonly string _env;
        private readonly LogLevel _startEndLevel;
        private readonly PipeLineLogMode _mode;

        public SinkLoggingAspect(ILogger logger, string title = "", string env = "Development", LogLevel startEndLevel = LogLevel.Information, PipeLineLogMode mode = PipeLineLogMode.Full)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger)); 
            _title = title ?? string.Empty;
            _env = env;
            _startEndLevel = startEndLevel;
            _mode = mode;

        }

        public async Task Execute(T msg)
        {
            var titleToUse = string.IsNullOrEmpty(_title)
                ? msg.GetType().Name
                : _title;

            IEnumerable<KeyValuePair<string, object>>? scope = null;

            if (_logger.IsEnabled(_startEndLevel) || _logger.IsEnabled(LogLevel.Error))
            {
                scope =
                [
                    new KeyValuePair<string, object>("Environment", _env),
                    new KeyValuePair<string, object>("PipelineName", msg.PipelineName),
                    new KeyValuePair<string, object>("CorrelationId", msg.CorrelationId),
                    new KeyValuePair<string, object>("Tag", msg.Tag),
                    new KeyValuePair<string, object>("IsTelemetry", false)
                ];
            }

            Action<string>? handler = null;

            if (_mode == PipeLineLogMode.Full)
            {
                handler = m => WriteToLog(m, _startEndLevel, scope);
                msg.OnLog += handler;
            }

            if (_mode != PipeLineLogMode.ErrorsOnly)
            {
                WriteToLog($"START: {titleToUse}", _startEndLevel, scope);
            }

            try
            {
                if (Next == null)
                {
                    if (_mode != PipeLineLogMode.ErrorsOnly)
                    {
                        WriteToLog("No next aspect configured.", _startEndLevel, scope);
                    }
                    return;
                }

                await Next.Execute(msg);
            }
            catch (Exception ex)
            {
                WriteToErrorLog(msg, ex, scope);
                throw;
            }
            finally
            {
                if (handler != null)
                {
                    msg.OnLog -= handler;
                }

                if (_mode != PipeLineLogMode.ErrorsOnly)
                {
                    WriteToLog($"END: {titleToUse}", _startEndLevel, scope);
                }
            }
        }

        public Aspect<T> Next { get; set; } = default!;

        private void WriteToLog(string message, LogLevel level, IEnumerable<KeyValuePair<string, object>>? scope)
        {
            if (!_logger.IsEnabled(level)) return;

            if (scope == null)
            {
                _logger.Log(level, "{Message}", message);
                return;
            }

            using (_logger.BeginScope(scope))
            {
                _logger.Log(level, "{Message}", message);
            }
        }

        private void WriteToErrorLog(T msg, Exception ex, IEnumerable<KeyValuePair<string, object>>? scope)
        {
            // Always attempt to log errors; WriteToErrorLog is called when an exception occurs.
            if (scope == null)
            {
                _logger.LogError(ex, "Unhandled exception in pipeline {PipelineName}", msg.PipelineName);
                return;
            }

            using (_logger.BeginScope(scope))
            {
                _logger.LogError(ex, "Unhandled exception in pipeline {PipelineName}", msg.PipelineName);
            }
        }
    }
}