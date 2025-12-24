using System;
using System.Threading.Tasks;
using DataPipe.Core.Contracts;

namespace DataPipe.Core.Filters
{
    /// <summary>
    /// OnTimeoutRetry provides a fully asynchronous, stateless retry mechanism
    /// for any set of filters within a DataPipe pipeline. 
    /// It allows you to specify:
    ///   - Maximum retry attempts
    ///   - Which exceptions should trigger a retry
    ///   - Custom delay strategies between retries (linear, exponential, etc.)
    /// 
    /// Example Usages:
    /// 
    /// Default retry:
    ///    pipe.Run(new OnTimeoutRetry<TestMessage>(3, new SomeFilter()));
    ///    - Retries up to 3 times
    ///    - Linear 2s, 4s, 6s delay
    ///    - Retries on TimeoutException or transient DB/network errors
    /// 
    /// Retry only on HttpRequestException:
    ///    pipe.Run(new OnTimeoutRetry<TestMessage>(
    ///        5, new SomeFilter(),
    ///        retryWhen: (ex, msg) => ex is HttpRequestException));
    /// 
    /// Exponential backoff delay strategy:
    ///    pipe.Run(new OnTimeoutRetry<TestMessage>(
    ///        5, new SomeFilter(),
    ///        customDelay: (attempt, msg) => TimeSpan.FromSeconds(Math.Pow(2, attempt))));
    /// 
    /// Fully custom example:
    ///    pipe.Run(new OnTimeoutRetry<TestMessage>(
    ///        3, new SomeFilter(),
    ///        retryWhen: (ex, msg) => ex is TimeoutException || ex.Message.Contains("busy"),
    ///        customDelay: (attempt, msg) => TimeSpan.FromSeconds(attempt * 1.5)));
    /// </summary>
    public class OnTimeoutRetry<T> : Filter<T> where T : BaseMessage, IAmRetryable
    {
        private readonly Filter<T>[] _filters;
        private readonly int _maxRetries;

        // Optional function to determine if an exception should trigger a retry
        private readonly Func<Exception, T, bool> _retryWhen;

        // Optional function to determine the delay before retrying (sliding, exponential, etc.)
        private readonly Func<int, T, TimeSpan> _defaultDelay;

        public OnTimeoutRetry(
            int maxRetries,
            params Filter<T>[] filters
        ) : this(maxRetries, null, null, filters) { } // preserves old behavior

        public OnTimeoutRetry(
            int maxRetries,
            Func<Exception, T, bool>? retryWhen = null,
            params Filter<T>[] filters
        ) : this(maxRetries, retryWhen, null, filters) { } // preserves old behavior

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="maxRetries">Maximum number of retry attempts</param>
        /// <param name="filters">Filters to execute under retry logic</param>
        /// <param name="retryWhen">Optional function to define which exceptions are retryable</param>
        /// <param name="customDelay">Optional function to define delay between retries</param>
        public OnTimeoutRetry(
            int maxRetries,
            Func<Exception, T, bool>? retryWhen = null,
            Func<int, T, TimeSpan>? customDelay = null,
            params Filter<T>[] filters)
        {
            _filters = filters;
            _maxRetries = maxRetries;

            // Default retry logic for transient errors if none provided
            _retryWhen = retryWhen ?? DefaultRetryWhen;

            // Default linear sliding delay (2s * attempt number)
            _defaultDelay = customDelay ?? DefaultDelay;
        }

        /// <summary>
        /// Executes the filters, retrying when necessary according to the defined conditions
        /// </summary>
        /// <param name="msg">Message flowing through the pipeline</param>
        public async Task Execute(T msg)
        {
            // Track max retries and current attempt in the message for debugging
            msg.MaxRetries = _maxRetries;
            msg.Attempt = 1;

            // Loop until the message is completed or marked as stopped
            while (!msg.Execution.IsStopped)
            {
                try
                {
                    // Run each filter sequentially
                    foreach (var f in _filters)
                    {
                        if (msg.Execution.IsStopped) break;
                        await f.Execute(msg);
                    }

                    // Exit loop on successful execution
                    break;
                }
                catch (Exception ex) when (_retryWhen(ex, msg))
                {
                    // Handle retries asynchronously
                    if (!await TryAgainAsync(msg, ex)) throw;
                }
            }
        }

        /// <summary>
        /// Handles incrementing the attempt counter and applying delay between retries
        /// </summary>
        /// <param name="msg">Message being processed</param>
        /// <param name="ex">The exception that triggered retry</param>
        /// <returns>True if retry will occur, false if max attempts reached</returns>
        private async Task<bool> TryAgainAsync(T msg, Exception ex)
        {
            if (msg.Attempt < msg.MaxRetries)
            {
                // Call optional hook before incrementing attempt
                msg.OnRetrying?.Invoke(msg.Attempt);

                msg.OnLog?.Invoke($"Attempt {msg.Attempt}/{msg.MaxRetries} failed for {ex.Source} - retrying...");
                msg.Attempt++;

                // Wait asynchronously according to the delay strategy
                await Task.Delay(_defaultDelay(msg.Attempt, msg));

                return true;
            }

            // No more retries left
            return false;
        }

        /// <summary>
        /// Default retry logic for transient errors:
        /// - TimeoutException
        /// - Transport-level database/network errors
        /// - Deadlocks
        /// </summary>
        private static bool DefaultRetryWhen(Exception ex, T _) =>
            ex is TimeoutException ||
            ex.Message.Contains("transport-level error") ||
            ex.Message.Contains("deadlocked") ||
            ex.Message.Contains("timeout");

        /// <summary>
        /// Default linear sliding delay between retries
        /// </summary>
        private static TimeSpan DefaultDelay(int attempt, T _) => TimeSpan.FromSeconds(attempt * 2);
    }
}