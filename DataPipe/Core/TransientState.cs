using System.Collections.Generic;

namespace DataPipe.Core
{
    /// <summary>
    /// A lightweight bag for storing transient inter-filter state during pipeline execution.
    /// Values stored here exist only for the duration of a single pipeline invocation.
    /// Use this instead of declaring internal properties on message classes for intermediate data
    /// that is produced by one filter and consumed by another.
    /// </summary>
    public class TransientState
    {
        private Dictionary<string, object>? _values;

        public void Set<T>(string key, T value) =>
            (_values ??= new())[key] = value!;

        public T Get<T>(string key) =>
            _values != null && _values.TryGetValue(key, out var value)
                ? (T)value
                : throw new KeyNotFoundException($"Transient state key '{key}' not found.");

        public T? GetOrDefault<T>(string key, T? defaultValue = default) =>
            _values != null && _values.TryGetValue(key, out var value)
                ? (T)value
                : defaultValue;

        public bool Has(string key) =>
            _values != null && _values.ContainsKey(key);

        public void Remove(string key) =>
            _values?.Remove(key);

        public void Clear() =>
            _values?.Clear();
    }
}
