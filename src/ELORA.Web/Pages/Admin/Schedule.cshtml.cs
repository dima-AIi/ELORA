using ELORA.Web.Data.Repositories;
using ELORA.Web.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ELORA.Web.Pages.Admin;

public class ScheduleModel : PageModel
{
    private readonly ScheduleRepository _schedule;
    private readonly MasterRepository _masters;
    private readonly BookingRepository _bookings;

    public ScheduleModel(ScheduleRepository schedule, MasterRepository masters, BookingRepository bookings)
    {
        _schedule = schedule;
        _masters = masters;
        _bookings = bookings;
    }

    public List<Master> Masters { get; private set; } = new();
    public List<BlockedDate> BlockedDates { get; private set; } = new();
    public Dictionary<int, List<WorkingHours>> HoursByMaster { get; private set; } = new();

    [BindProperty(SupportsGet = true)] public int? MasterId { get; set; }

    public string[] DayNames { get; } =
        { "Воскресенье", "Понедельник", "Вторник", "Среда", "Четверг", "Пятница", "Суббота" };

    public void OnGet()
    {
        Masters = _masters.GetAll(false);
        BlockedDates = _schedule.GetBlockedDates();

        foreach (var master in Masters)
            HoursByMaster[master.Id] = _schedule.GetWorkingHours(master.Id)
                .OrderBy(h => h.DayOfWeek == 0 ? 7 : h.DayOfWeek)
                .ToList();
    }

    public IActionResult OnPostSaveHours(int masterId, int[]? days, IFormCollection form)
    {
        if (masterId <= 0)
        {
            TempData["FlashError"] = "Не выбран мастер";
            return RedirectToPage();
        }

        var workingDays = (days ?? Array.Empty<int>()).ToHashSet();
        var hours = new List<WorkingHours>();

        for (var day = 0; day < 7; day++)
        {
            var from = form[$"start{day}"].ToString();
            var to = form[$"end{day}"].ToString();
            if (string.IsNullOrWhiteSpace(from)) from = "09:00";
            if (string.IsNullOrWhiteSpace(to)) to = "19:00";

            if (!TimeOnly.TryParse(from, out var parsedFrom) || !TimeOnly.TryParse(to, out var parsedTo))
            {
                TempData["FlashError"] = $"Некорректное время для дня «{DayNames[day]}»";
                return RedirectToPage(new { MasterId = masterId });
            }

            if (workingDays.Contains(day) && parsedFrom >= parsedTo)
            {
                TempData["FlashError"] = $"В дне «{DayNames[day]}» начало позже окончания";
                return RedirectToPage(new { MasterId = masterId });
            }

            hours.Add(new WorkingHours
            {
                MasterId = masterId,
                DayOfWeek = day,
                StartTime = parsedFrom.ToString("HH:mm"),
                EndTime = parsedTo.ToString("HH:mm"),
                IsActive = workingDays.Contains(day)
            });
        }

        _schedule.SaveWorkingHours(masterId, hours);
        TempData["Flash"] = "График сохранён";
        return RedirectToPage(new { MasterId = masterId });
    }

    public IActionResult OnPostAddBlocked(string? date, int? masterId, string? reason)
    {
        if (!DateOnly.TryParse(date, out var parsed))
        {
            TempData["FlashError"] = "Укажите дату";
            return RedirectToPage();
        }

        _schedule.AddBlockedDate(masterId is > 0 ? masterId : null, parsed, reason);

        // Закрытая дата не отменяет уже существующие записи: клиенты о ней не узнают и придут.
        // Поэтому считаем их и говорим об этом вслух — иначе «дата закрыта» выглядит как
        // полностью решённый вопрос, а в списке остаются живые записи.
        var affected = _bookings.GetBookings(parsed, parsed, null, masterId is > 0 ? masterId : null)
            .Where(b => b.IsActiveStatus)
            .ToList();

        if (affected.Count == 0)
        {
            TempData["Flash"] = "Дата закрыта для записи. Активных записей на этот день нет.";
            return RedirectToPage();
        }

        var names = string.Join(", ", affected.Take(3).Select(b => b.ClientName));
        var tail = affected.Count > 3 ? " и другие" : "";
        TempData["FlashWarning"] =
            $"Дата закрыта, но на {parsed:dd.MM.yyyy} осталось активных записей: {affected.Count} ({names}{tail}). " +
            "Закрытие даты их не отменяет — отмените или перенесите их в разделе «Записи», " +
            "отфильтровав по этой дате, иначе клиенты придут к закрытой студии.";
        return RedirectToPage();
    }

    public IActionResult OnPostRemoveBlocked(int id)
    {
        _schedule.RemoveBlockedDate(id);
        TempData["Flash"] = "Дата снова доступна для записи";
        return RedirectToPage();
    }
}
