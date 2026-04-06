using System;
using System.Collections.Generic;
using System.Threading;

namespace DataPipe.Core
{
    internal static class LogContext
    {
        private static readonly AsyncLocal<Stack<string?>?> TagStack = new();

        internal static string? CurrentTag
        {
            get
            {
                var stack = TagStack.Value;
                if (stack == null || stack.Count == 0)
                {
                    return null;
                }

                return stack.Peek();
            }
        }

        internal static IDisposable PushTag(string? tag)
        {
            var stack = TagStack.Value;
            if (stack == null)
            {
                stack = new Stack<string?>();
                TagStack.Value = stack;
            }

            stack.Push(Normalize(tag));
            return new TagScope();
        }

        private static string? Normalize(string? tag) => string.IsNullOrWhiteSpace(tag) ? null : tag;

        private sealed class TagScope : IDisposable
        {
            private bool _disposed;

            public void Dispose()
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;

                var stack = TagStack.Value;
                if (stack == null || stack.Count == 0)
                {
                    return;
                }

                stack.Pop();
                if (stack.Count == 0)
                {
                    TagStack.Value = null;
                }
            }
        }
    }
}