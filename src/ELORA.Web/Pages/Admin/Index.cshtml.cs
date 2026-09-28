using ELORA.Web.Models;
using ELORA.Web.Services;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ELORA.Web.Pages.Admin;

public class IndexModel : PageModel
{
    private readonly AdminService _admin;
    private readonly TelegramService _telegram;
    private readonly ClientReminderService _reminders;

    public IndexModel(AdminService admin, TelegramService telegram, ClientReminderService reminders)
    {
        _admin = admin;
        _telegram = telegram;
        _reminders = reminders;
    }

    public DashboardStats Stats { get; private set; } = new();
    public bool ReminderEnabled { get; private set; }
    public int ReminderHours { get; private set; }

    /// <summary>Боты: у каждого свой токен и своё имя. Токены живут вне исходников.</summary>
    public bool ClientBotConfigured { get; private set; }
    public bool AdminBotConfigured { get; private set; }
    public string ClientBotUsername { get; private set; } = "";
    public string AdminBotUsername { get; private set; } = "";

    /// <summary>Сколько клиентов давно не были — выносим на дашборд, чтобы не забывалось.</summary>
    public int ClientsDueForVisit { get; private set; }
    public int WinBackWeeks { get; private set; }

    /// <summary>
    /// Причина, по которой уведомление о новой записи не уйдёт. Показывается на дашборде:
    /// владелец узнаёт о молчании бота при входе в админку, а не из жалобы клиента.
    /// </summary>
    public string? TelegramBlockReason { get; private set; }

    public void OnGet()
    {
        Stats = _admin.GetDashboard();

        ClientBotConfigured = _telegram.IsClientBotConfigured;
        AdminBotConfigured = _telegram.IsAdminBotConfigured;
        ClientBotUsername = _admin.Get("Telegram.BotUsername");
        AdminBotUsername = _admin.Get("Telegram.AdminBotUsername");

        ReminderEnabled = _admin.Get("Telegram.Enabled") == "true";
        ReminderHours = int.TryParse(_admin.Get("Telegram.ReminderHoursBefore"), out var h) ? h : 24;

        TelegramBlockReason = _telegram.BlockReason();

        WinBackWeeks = _reminders.ThresholdWeeks;
        ClientsDueForVisit = _reminders.DueCount();
    }

    public string StatusTitle(string status) => BookingStatuses.Title(status);
}
