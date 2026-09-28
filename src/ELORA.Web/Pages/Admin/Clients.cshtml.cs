using ELORA.Web.Data.Repositories;
using ELORA.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ELORA.Web.Pages.Admin;

/// <summary>
/// Список клиентов студии и блок «пора напомнить».
/// </summary>
/// <remarks>
/// До этого раздела клиентов в админке не было видно как людей: только через записи, по одной.
/// Здесь они собраны с агрегатами (сколько визитов, когда последний, сколько оставил) — именно
/// по этим числам администратор решает, кому звонить.
/// </remarks>
public class ClientsModel : PageModel
{
    private readonly ClientRepository _clients;
    private readonly ClientReminderService _reminders;

    public ClientsModel(ClientRepository clients, ClientReminderService reminders)
    {
        _clients = clients;
        _reminders = reminders;
    }

    public List<ClientSummary> Items { get; private set; } = new();
    public List<ClientSummary> DueForVisit { get; private set; } = new();

    public int TotalCount { get; private set; }
    public int TelegramCount { get; private set; }
    public decimal TotalRevenue { get; private set; }
    public int ThresholdWeeks { get; private set; }

    /// <summary>Клиенты, которых нельзя пригласить в боте, — их придётся звонить.</summary>
    public int DueWithoutTelegram { get; private set; }

    /// <summary>Сколько клиентов были в студии за последние 30 дней.</summary>
    public int ActiveThisMonth { get; private set; }

    [BindProperty(SupportsGet = true)] public string? Search { get; set; }

    public void OnGet()
    {
        ThresholdWeeks = _reminders.ThresholdWeeks;

        var all = _clients.GetAll();
        TotalCount = all.Count;
        TelegramCount = all.Count(c => c.TelegramConnected);
        TotalRevenue = all.Sum(c => c.TotalSpent);

        var monthAgo = DateTime.Now.AddDays(-30);
        ActiveThisMonth = all.Count(c => c.LastVisit is not null && c.LastVisit >= monthAgo);

        DueForVisit = _reminders.DueForVisit();
        DueWithoutTelegram = DueForVisit.Count(c => !c.TelegramConnected);

        Items = string.IsNullOrWhiteSpace(Search) ? all : _clients.GetAll(Search);
    }

    /// <summary>Приглашает одного клиента вернуться. Текст по умолчанию собирает сервис.</summary>
    public async Task<IActionResult> OnPostInviteAsync(int id, string? text, CancellationToken cancellationToken)
    {
        var client = _clients.GetById(id);
        if (client is null)
        {
            TempData["FlashError"] = "Клиент не найден";
            return RedirectToPage(new { Search });
        }

        var error = await _reminders.InviteBackAsync(client, text, cancellationToken);
        TempData[error is null ? "Flash" : "FlashError"] = error is null
            ? $"Приглашение отправлено: {client.Name}"
            : $"{client.Name}: {error}";

        return RedirectToPage(new { Search });
    }

    /// <summary>
    /// Приглашает всех, кто в списке и подключён к боту. Одним нажатием, но с отчётом:
    /// молчаливая рассылка на десяток человек без обратной связи — плохая идея.
    /// </summary>
    public async Task<IActionResult> OnPostInviteAllAsync(CancellationToken cancellationToken)
    {
        var targets = _reminders.DueForVisit().Where(c => c.TelegramConnected).ToList();
        if (targets.Count == 0)
        {
            TempData["FlashError"] = "Некого приглашать: у всех из списка нет подключённого Telegram";
            return RedirectToPage(new { Search });
        }

        var sent = 0;
        var failed = 0;

        foreach (var client in targets)
        {
            var error = await _reminders.InviteBackAsync(client, null, cancellationToken);
            if (error is null) sent++;
            else failed++;
        }

        TempData[failed == 0 ? "Flash" : "FlashError"] = failed == 0
            ? $"Приглашение отправлено: {sent}"
            : $"Отправлено: {sent}, не дошло: {failed}";

        return RedirectToPage(new { Search });
    }

    public IActionResult OnPostSaveNotes(int id, string? notes)
    {
        var client = _clients.GetById(id);
        if (client is null)
        {
            TempData["FlashError"] = "Клиент не найден";
            return RedirectToPage(new { Search });
        }

        _clients.SetNotes(id, notes);
        TempData["Flash"] = $"Заметка сохранена: {client.Name}";
        return RedirectToPage(new { Search });
    }
}
