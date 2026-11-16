using System.Collections.Concurrent;

namespace HorizonControl.Api.Services;

public sealed class EnrollRateLimiter
{
    private readonly ConcurrentDictionary<string, Queue<DateTimeOffset>> _hits = new(StringComparer.Ordinal);
    private readonly TimeSpan _window = TimeSpan.FromMinutes(1);
    private readonly int _max = 20;

    public bool TryAcquire(string key)
    {
        var now = DateTimeOffset.UtcNow;
        var q = _hits.GetOrAdd(key, _ => new Queue<DateTimeOffset>());
        lock (q)
        {
            while (q.Count > 0 && now - q.Peek() > _window)
            {
                q.Dequeue();
            }

            if (q.Count >= _max)
            {
                return false;
            }

            q.Enqueue(now);
            return true;
        }
    }
}
