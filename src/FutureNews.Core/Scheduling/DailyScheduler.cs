using System.Diagnostics;

namespace FutureNews.Core.Scheduling;

/// <summary>
/// Fires a callback at a fixed local time every day. Survives system sleep by re-checking
/// the clock on wake, and can be triggered immediately.
/// </summary>
public sealed class DailyScheduler : IDisposable
{
    private readonly TimeSpan _timeOfDay;
    private readonly Func<CancellationToken, Task> _onTick;
    private readonly Action<string>? _log;
    private CancellationTokenSource? _cts;
    private Task? _loop;

    public DailyScheduler(TimeSpan timeOfDay, Func<CancellationToken, Task> onTick, Action<string>? log = null)
    {
        _timeOfDay = timeOfDay;
        _onTick = onTick;
        _log = log;
    }

    /// <summary>The time of day this scheduler triggers at.</summary>
    public TimeSpan TimeOfDay => _timeOfDay;

    /// <summary>The next occurrence of the configured time, in local time.</summary>
    public DateTimeOffset NextRun
    {
        get
        {
            var now = DateTimeOffset.Now;
            var next = new DateTimeOffset(now.Year, now.Month, now.Day, _timeOfDay.Hours, _timeOfDay.Minutes, 0, now.Offset);
            if (next <= now) next = next.AddDays(1);
            return next;
        }
    }

    public void Start()
    {
        if (_loop is not null) return;
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        _loop = Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                var delay = NextRun - DateTimeOffset.Now;
                if (delay < TimeSpan.Zero) delay = TimeSpan.FromSeconds(5);
                try
                {
                    await Task.Delay(delay, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }

                if (token.IsCancellationRequested) return;
                try
                {
                    await _onTick(token).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _log?.Invoke($"定时更新失败: {ex.Message}");
                }
            }
        }, CancellationToken.None);
    }

    /// <summary>Runs the callback immediately on a background thread.</summary>
    public void TriggerNow()
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await _onTick(CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _log?.Invoke($"手动更新失败: {ex.Message}");
            }
        });
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _loop = null;
    }
}
