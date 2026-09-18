using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BlackGoldAncientSword.Framework.Core.Attributes;

namespace BlackGoldAncientSword.Framework.Http.Heybox
{

    [Component(ComponentLifetime.Singleton)]
    public sealed class HeyboxRequestCache
    {
        private const int MaxEntries = 256;

        private readonly object _sync = new();
        private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);

        public async Task<T> RunAsync<T>(string key, Func<Task<T>> factory, CancellationToken ct)
        {
            var task = GetOrStart(key, factory);
            return await task.WaitAsync(ct).ConfigureAwait(false);
        }

        public void Invalidate()
        {
            lock (_sync)
            {
                _entries.Clear();
            }
        }

        public void Invalidate(string key)
        {
            lock (_sync)
            {
                _entries.Remove(key);
            }
        }

        private Task<T> GetOrStart<T>(string key, Func<Task<T>> factory)
        {
            var now = DateTimeOffset.UtcNow;

            lock (_sync)
            {
                if (_entries.TryGetValue(key, out var existing) && existing.Task is Task<T> typed)
                {
                    if (!typed.IsCompleted) return typed;
                    if (typed.IsCompletedSuccessfully) return typed;
                    _entries.Remove(key);
                }

                var started = factory();
                if (_entries.Count >= MaxEntries) TrimUnlocked();
                _entries[key] = new Entry(now, started);
                return started;
            }
        }

        private void TrimUnlocked()
        {
            var removable = _entries
                .Where(kv => kv.Value.Task.IsCompleted)
                .OrderBy(kv => kv.Value.StartedAt)
                .Select(kv => kv.Key)
                .Take(Math.Max(1, _entries.Count - MaxEntries + 1))
                .ToList();

            foreach (var key in removable) _entries.Remove(key);
        }

        private sealed record Entry(DateTimeOffset StartedAt, Task Task);
    }
}
