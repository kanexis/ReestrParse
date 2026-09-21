namespace ReestrParse.Infrastructure.Selenium.Eias;

/// <summary>
/// Общий для всех details-workers предохранитель от request storm.
/// Если несколько worker-ов почти одновременно получают сбой DevExpress pager,
/// новые catalog-попытки ненадолго притормаживаются. Это не обход ограничения сайта,
/// а наоборот — уменьшение нагрузки при признаках деградации callback-ов.
/// </summary>
internal static class EiasCatalogBackoffGate
{
    private static readonly object Sync = new();
    private static readonly Queue<DateTime> RecentNavigationFailures = new();

    private static readonly TimeSpan FailureWindow = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan BackoffDuration = TimeSpan.FromSeconds(12);
    private const int FailureThreshold = 3;

    private static DateTime _backoffUntilUtc = DateTime.MinValue;

    public static TimeSpan GetRemainingDelay()
    {
        lock (Sync)
        {
            var now = DateTime.UtcNow;
            Prune(now);

            return _backoffUntilUtc > now
                ? _backoffUntilUtc - now
                : TimeSpan.Zero;
        }
    }

    public static void ReportNavigationFailure()
    {
        lock (Sync)
        {
            var now = DateTime.UtcNow;
            Prune(now);

            // Ошибки, которые прилетели от уже запущенных callback-ов во время
            // активного backoff, не продлевают паузу бесконечно.
            if (_backoffUntilUtc > now)
                return;

            RecentNavigationFailures.Enqueue(now);
            if (RecentNavigationFailures.Count < FailureThreshold)
                return;

            _backoffUntilUtc = now + BackoffDuration;
            RecentNavigationFailures.Clear();
        }
    }

    public static void WaitIfNeeded(CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var remaining = GetRemainingDelay();
            if (remaining <= TimeSpan.Zero)
                return;

            var sleep = remaining > TimeSpan.FromMilliseconds(250)
                ? TimeSpan.FromMilliseconds(250)
                : remaining;

            if (sleep > TimeSpan.Zero)
                Thread.Sleep(sleep);
        }
    }

    private static void Prune(DateTime now)
    {
        while (RecentNavigationFailures.Count > 0 &&
               now - RecentNavigationFailures.Peek() > FailureWindow)
        {
            RecentNavigationFailures.Dequeue();
        }

        if (_backoffUntilUtc <= now)
            _backoffUntilUtc = DateTime.MinValue;
    }
}
