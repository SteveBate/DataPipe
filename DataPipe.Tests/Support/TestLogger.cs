#nullable enable
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace DataPipe.Tests.Support
{
    class TestLogger : ILogger
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = new();
        public List<(LogLevel Level, string Message, IReadOnlyDictionary<string, object?> Scope)> ScopedEntries { get; } = new();

        private readonly object _sync = new();
        private readonly AsyncLocal<ScopeNode?> _scope = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull
        {
            var parent = _scope.Value;
            _scope.Value = new ScopeNode(state, parent);
            return new ScopeDisposable(this, parent);
        }

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var message = formatter(state, exception);
            var scope = CaptureScope();

            lock (_sync)
            {
                Entries.Add((logLevel, message));
                ScopedEntries.Add((logLevel, message, scope));
            }
        }

        private IReadOnlyDictionary<string, object?> CaptureScope()
        {
            var scope = _scope.Value;
            if (scope == null)
            {
                return new Dictionary<string, object?>();
            }

            var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            var chain = new List<object>();
            while (scope != null)
            {
                chain.Add(scope.State);
                scope = scope.Parent;
            }

            foreach (var scopeItem in chain.Reverse<object>())
            {
                if (scopeItem is IEnumerable<KeyValuePair<string, object>> values)
                {
                    foreach (var kvp in values)
                    {
                        result[kvp.Key] = kvp.Value;
                    }
                    continue;
                }

                result["Scope"] = scopeItem;
            }

            return result;
        }

        private sealed class ScopeNode
        {
            public object State { get; }
            public ScopeNode? Parent { get; }

            public ScopeNode(object state, ScopeNode? parent)
            {
                State = state;
                Parent = parent;
            }
        }

        private sealed class ScopeDisposable : IDisposable
        {
            private readonly TestLogger _owner;
            private readonly ScopeNode? _parent;
            private bool _disposed;

            public ScopeDisposable(TestLogger owner, ScopeNode? parent)
            {
                _owner = owner;
                _parent = parent;
            }

            public void Dispose()
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                _owner._scope.Value = _parent;
            }
        }
    }
}
