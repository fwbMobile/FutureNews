namespace FutureNews.Core.Diagnostics;

/// <summary>
/// Tracks per-source reliability so a repeatedly failing provider is skipped for a while
/// instead of slowing every daily run down with timeouts.
/// </summary>
public sealed class SourceHealth
{
    private readonly Dictionary<string, Entry> _entries = new();
    private readonly object _sync = new();

    private sealed class Entry
    {
        public int ConsecutiveFailures;
        public DateTimeOffset? DisabledUntil;
        public DateTimeOffset LastSuccess = DateTimeOffset.MinValue;
        public string LastError = "";
    }

    /// <summary>Number of failures before a source is temporarily disabled.</summary>
    public int FailureThreshold { get; init; } = 2;

    /// <summary>How long a failing source stays disabled.</summary>
    public TimeSpan Cooldown { get; init; } = TimeSpan.FromHours(6);

    /// <summary>Restores previously persisted state so a broken source stays skipped between runs.</summary>
    public void Restore(IReadOnlyDictionary<string, (int Failures, DateTimeOffset? DisabledUntil, string Error)> state)
    {
        lock (_sync)
        {
            foreach (var (sourceId, item) in state)
            {
                var entry = Get(sourceId);
                entry.ConsecutiveFailures = item.Failures;
                entry.DisabledUntil = item.DisabledUntil;
                entry.LastError = item.Error;
            }
        }
    }

    /// <summary>Exports the current state for persistence.</summary>
    public Dictionary<string, (int Failures, DateTimeOffset? DisabledUntil, string Error, DateTimeOffset LastSuccess)> Export()
    {
        lock (_sync)
        {
            return _entries.ToDictionary(
                kv => kv.Key,
                kv => (kv.Value.ConsecutiveFailures, kv.Value.DisabledUntil, kv.Value.LastError, kv.Value.LastSuccess),
                StringComparer.OrdinalIgnoreCase);
        }
    }

    public bool IsAvailable(string sourceId)
    {
        lock (_sync)
        {
            if (!_entries.TryGetValue(sourceId, out var entry)) return true;
            if (entry.DisabledUntil is null) return true;
            if (entry.DisabledUntil <= DateTimeOffset.UtcNow)
            {
                entry.DisabledUntil = null;
                entry.ConsecutiveFailures = 0;
                return true;
            }
            return false;
        }
    }

    public void RecordSuccess(string sourceId)
    {
        lock (_sync)
        {
            var entry = Get(sourceId);
            entry.ConsecutiveFailures = 0;
            entry.DisabledUntil = null;
            entry.LastSuccess = DateTimeOffset.UtcNow;
            entry.LastError = "";
        }
    }

    public void RecordFailure(string sourceId, string error)
    {
        lock (_sync)
        {
            var entry = Get(sourceId);
            entry.ConsecutiveFailures++;
            entry.LastError = error;
            if (entry.ConsecutiveFailures >= FailureThreshold)
            {
                entry.DisabledUntil = DateTimeOffset.UtcNow + Cooldown;
            }
        }
    }

    public string Describe(string sourceId)
    {
        lock (_sync)
        {
            if (!_entries.TryGetValue(sourceId, out var entry)) return "正常";
            if (entry.DisabledUntil is { } until && until > DateTimeOffset.UtcNow)
                return $"已暂停至 {until.LocalDateTime:MM-dd HH:mm}（{entry.LastError}）";
            if (entry.ConsecutiveFailures > 0) return $"最近失败 {entry.ConsecutiveFailures} 次：{entry.LastError}";
            return "正常";
        }
    }

    public IReadOnlyDictionary<string, string> Snapshot()
    {
        lock (_sync)
        {
            return _entries.ToDictionary(kv => kv.Key, kv => Describe(kv.Key));
        }
    }

    private Entry Get(string sourceId)
    {
        if (!_entries.TryGetValue(sourceId, out var entry))
        {
            entry = new Entry();
            _entries[sourceId] = entry;
        }
        return entry;
    }
}
