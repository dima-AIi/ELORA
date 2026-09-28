using ELORA.Web.Data.Repositories;
using ELORA.Web.DTOs;
using ELORA.Web.Models;

namespace ELORA.Web.Services;

/// <summary>
/// Расчёт свободных слотов. Полностью повторяет BOOKING_ALGORITHM.md:
/// проверки услуги/мастера/связи/блокировок/рабочих часов/прошлого времени,
/// генерация кандидатов шагом 30 минут и отсечение пересечений.
/// </summary>
public sealed class ScheduleService
{
    public const int StepMinutes = 30;
    private static readonly TimeSpan MinimumLeadTime = TimeSpan.FromMinutes(30);

    private readonly CatalogRepository _catalog;
    private readonly MasterRepository _masters;
    private readonly ScheduleRepository _schedule;
    private readonly BookingRepository _bookings;

    public ScheduleService(
        CatalogRepository catalog,
        MasterRepository masters,
        ScheduleRepository schedule,
        BookingRepository bookings)
    {
        _catalog = catalog;
        _masters = masters;
        _schedule = schedule;
        _bookings = bookings;
    }

    public SlotResult GetSlots(int serviceId, int masterId, DateOnly date)
    {
        var service = _catalog.GetService(serviceId);
        if (service is null || !service.IsActive)
            return SlotResult.Fail("Услуга недоступна");

        var master = _masters.GetById(masterId);
        if (master is null || !master.IsActive)
            return SlotResult.Fail("Мастер недоступен");

        if (!master.ServiceIds.Contains(serviceId))
            return SlotResult.Fail("Мастер не оказывает эту услугу");

        if (_schedule.IsDateBlocked(masterId, date))
            return SlotResult.Fail("Дата недоступна для записи");

        var hours = _schedule.GetWorkingHours(masterId, date.DayOfWeek);
        if (hours is null || !hours.IsActive)
            return SlotResult.Fail("В этот день мастер не работает");

        var today = DateOnly.FromDateTime(DateTime.Now);
        if (date < today)
            return SlotResult.Fail("Дата уже прошла");

        if (!TimeOnly.TryParse(hours.StartTime, out var workStart) ||
            !TimeOnly.TryParse(hours.EndTime, out var workEnd))
            return SlotResult.Fail("Некорректные рабочие часы мастера");

        var busy = _bookings.GetActiveBookingsForMaster(masterId, date)
            .Select(b => (Start: Parse(b.StartAt), End: Parse(b.EndAt)))
            .Where(x => x.Start.HasValue && x.End.HasValue)
            .Select(x => (Start: x.Start!.Value, End: x.End!.Value))
            .ToList();

        var slots = new List<SlotDto>();
        var earliest = DateTime.Now.Add(MinimumLeadTime);

        for (var cursor = workStart; cursor.AddMinutes(service.DurationMinutes) <= workEnd; cursor = cursor.AddMinutes(StepMinutes))
        {
            var start = date.ToDateTime(cursor);
            var end = start.AddMinutes(service.DurationMinutes);

            if (start < earliest) continue;
            if (busy.Any(b => start < b.End && end > b.Start)) continue;

            slots.Add(new SlotDto
            {
                Time = start.ToString("HH:mm"),
                Start = start.ToString("s"),
                End = end.ToString("s")
            });
        }

        return new SlotResult { Ok = true, Slots = slots };
    }

    /// <summary>Проверяет, свободен ли конкретный интервал (используется при переносе).</summary>
    public bool IsRangeFree(int masterId, DateTime start, DateTime end, int? excludeBookingId = null)
    {
        var busy = _bookings.GetActiveBookingsForMaster(masterId, DateOnly.FromDateTime(start));
        return busy
            .Where(b => b.Id != excludeBookingId)
            .All(b => !(start < b.EndLocal && end > b.StartLocal));
    }

    public List<DateOnly> GetAvailableDates(int masterId, int serviceId, int daysAhead = 60)
    {
        var result = new List<DateOnly>();
        var today = DateOnly.FromDateTime(DateTime.Now);
        var service = _catalog.GetService(serviceId);
        if (service is null) return result;

        for (var i = 0; i < daysAhead; i++)
        {
            var date = today.AddDays(i);
            if (_schedule.IsDateBlocked(masterId, date)) continue;

            var hours = _schedule.GetWorkingHours(masterId, date.DayOfWeek);
            if (hours is null || !hours.IsActive) continue;

            if (GetSlots(serviceId, masterId, date).Slots.Count > 0)
                result.Add(date);
        }
        return result;
    }

    private static DateTime? Parse(string value) =>
        DateTime.TryParse(value, out var parsed) ? parsed : null;
}
