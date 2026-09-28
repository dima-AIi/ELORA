using ELORA.Web.Data.Repositories;
using ELORA.Web.Models;

namespace ELORA.Web.Services;

/// <summary>
/// Напоминания примерно за 24 часа до визита.
/// Отправка считается успешной только при успешном ответе Telegram — тогда пишется ReminderLog.
/// Если Telegram недоступен, следующий цикл попробует снова.
/// </summary>
public sealed class ReminderService
{
    private readonly BookingRepository _bookings;
    private readonly TelegramService _telegram;
    private readonly ILogger<ReminderService> _logger;

    public ReminderService(
        BookingRepository bookings,
        TelegramService telegram,
        ILogger<ReminderService> logger)
    {
        _bookings = bookings;
        _telegram = telegram;
        _logger = logger;
    }

    public async Task<int> RunOnceAsync(CancellationToken cancellationToken = default)
    {
        if (!_telegram.IsEnabled)
        {
            _logger.LogDebug("Напоминания пропущены: Telegram выключен или не настроен");
            return 0;
        }

        var now = DateTime.Now;
        var horizon = now.AddHours(_telegram.ReminderHoursBefore);

        var candidates = _bookings.GetUpcomingForReminder(now, horizon);
        var sent = 0;

        foreach (var booking in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (_bookings.HasReminder(booking.Id, ReminderTypes.DayBefore)) continue;

            var ok = await _telegram.SendReminderAsync(booking, cancellationToken);
            if (!ok) continue;

            _bookings.AddReminder(booking.Id, ReminderTypes.DayBefore);
            sent++;
            _logger.LogInformation("Напоминание отправлено для записи {BookingId}", booking.Id);
        }

        return sent;
    }
}
