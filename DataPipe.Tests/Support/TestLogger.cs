#nullable enable
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;

namespace DataPipe.Tests.Support
{
    class TestLogger : ILogger
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => new NoOpDisposable();

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Entries.Add((logLevel, formatter(state, exception)));
        }

        private class NoOpDisposable : IDisposable
        {
            public void Dispose() { }
        }
    }
}
