using System.Collections.Concurrent;

namespace ELORA.Web.Services.Ai;

/// <summary>
/// Простейший ограничитель частоты для публичного чата: не больше N вопросов в минуту
/// с одного адреса. Нужен потому, что endpoint тратит ключ владельца, а не посетителя:
/// без ограничения один скрипт выберет всю бесплатную квоту за пару минут.
/// </summary>
/// <remarks>
/// Хранилище — в памяти процесса. Этого достаточно: у сайта один экземпляр,
/// а цель ограничителя — не защита от целевой атаки, а защита от случайного и грубого потока.
/// </remarks>
public sealed class ChatRateLimiter
{
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

    private readonly ConcurrentDictionary<string, Queue<DateTime>> _hits = new(StringComparer.Ordinal);
    private DateTime _lastSweep = DateTime.UtcNow;

    public bool TryPass(string key, int perMinute, out int retryAfterSeconds)
    {
        retryAfterSeconds = 0;
        var limit = Math.Max(1, perMinute);
        var now = DateTime.UtcNow;

        SweepIfDue(now);

        var queue = _hits.GetOrAdd(key, _ => new Queue<DateTime>());

        lock (queue)
        {
            while (queue.Count > 0 && now - queue.Peek() > Window) queue.Dequeue();

            if (queue.Count >= limit)
            {
                var oldest = queue.Peek();
                retryAfterSeconds = Math.Max(1, (int)Math.Ceiling((Window - (now - oldest)).TotalSeconds));
                return false;
            }

            queue.Enqueue(now);
            return true;
        }
    }

    /// <summary>Раз в десять минут выкидываем адреса, о которых уже нечего помнить.</summary>
    private void SweepIfDue(DateTime now)
    {
        if (now - _lastSweep < TimeSpan.FromMinutes(10)) return;
        _lastSweep = now;

        foreach (var pair in _hits)
        {
            var queue = pair.Value;
            lock (queue)
            {
                while (queue.Count > 0 && now - queue.Peek() > Window) queue.Dequeue();
                if (queue.Count == 0) _hits.TryRemove(pair.Key, out _);
            }
        }
    }
}
