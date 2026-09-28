using ELORA.Web.Data.Repositories;
using ELORA.Web.Models;
using ELORA.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ELORA.Web.Pages.Admin;

/// <summary>
/// Карточка клиента: история визитов, заметка администратора и приглашение вернуться.
/// </summary>
/// <remarks>
/// Заметка хранится в <c>Clients.Notes</c> и видна только в админке — клиент её не получает.
/// Так администратор помнит, что «Анна не любит резкий запах», не заводя отдельный файл.
/// </remarks>
public class ClientModel : PageModel
{
    private readonly ClientRepository _clients;
    private readonly ClientReminderService _reminders;

    public ClientModel(ClientRepository clients, ClientReminderService reminders)
    {
        _clients = clients;
        _reminders = reminders;
    }

    public ClientSummary Client { get; private set; } = new();
    public List<Booking> Bookings { get; private set; } = new();
    public int ThresholdWeeks { get; private set; }

    /// <summary>Клиент подходит под правило «пора напомнить» прямо сейчас.</summary>
    public bool IsDueForVisit { get; private set; }

    public IActionResult OnGet(int id)
    {
        var client = _clients.GetById(id);
        if (client is null) return NotFound();

        Client = client;
        Bookings = _clients.GetBookings(id);
        ThresholdWeeks = _reminders.ThresholdWeeks;

        var threshold = DateTime.Now.AddDays(-7 * ThresholdWeeks);
        IsDueForVisit = client.LastVisit is not null
                        && client.LastVisit < threshold
                        && client.NextVisit is null;

        return Page();
    }

    public async Task<IActionResult> OnPostInviteAsync(int id, string? text, CancellationToken cancellationToken)
    {
        var client = _clients.GetById(id);
        if (client is null) return NotFound();

        var error = await _reminders.InviteBackAsync(client, text, cancellationToken);
        TempData[error is null ? "Flash" : "FlashError"] = error ?? "Приглашение отправлено";
        return RedirectToPage(new { id });
    }

    public IActionResult OnPostNotes(int id, string? notes)
    {
        if (_clients.GetById(id) is null) return NotFound();

        _clients.SetNotes(id, notes);
        TempData["Flash"] = "Заметка сохранена";
        return RedirectToPage(new { id });
    }

    /// <summary>Сколько оставил клиент по завершённым визитам — то же число, что в списке.</summary>
    public decimal CompletedRevenue => Bookings.Where(b => b.Status == BookingStatuses.Completed).Sum(b => b.Price);

    public int CompletedVisits => Bookings.Count(b => b.Status == BookingStatuses.Completed);

    public int CancelledVisits => Bookings.Count(b => b.Status == BookingStatuses.Cancelled);
}
