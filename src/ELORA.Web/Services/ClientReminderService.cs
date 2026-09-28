using ELORA.Web.Data.Repositories;
using ELORA.Web.Helpers;

namespace ELORA.Web.Services;

/// <summary>
/// Приглашение вернуться: клиент давно не был, и студия напоминает о себе.
/// </summary>
/// <remarks>
/// Отличается от <see cref="ReminderService"/>: тот напоминает о <em>состоявшейся</em> записи
/// (у неё есть <c>BookingId</c>, и повтор отсекается журналом <c>ReminderLogs</c>). Здесь записи
/// нет вовсе — человек просто пропал, поэтому повтор отсекается датой <c>Clients.LastRemindedAt</c>.
/// Пишем клиенту только через клиентский бот: сообщение должно прийти от того бота,
/// которого человек видел на сайте.
/// </remarks>
public sealed class ClientReminderService
{
    private readonly ClientRepository _clients;
    private readonly TelegramService _telegram;
    private readonly SettingsRepository _settings;
    private readonly ILogger<ClientReminderService> _logger;

    public ClientReminderService(
        ClientRepository clients,
        TelegramService telegram,
        SettingsRepository settings,
        ILogger<ClientReminderService> logger)
    {
        _clients = clients;
        _telegram = telegram;
        _settings = settings;
        _logger = logger;
    }

    /// <summary>Сколько недель без визита считаем поводом напомнить.</summary>
    public int ThresholdWeeks => Math.Max(1, _settings.GetInt("Clients.WinBackWeeks", 3));

    public List<ClientSummary> DueForVisit() => _clients.GetDueForVisit(ThresholdWeeks);

    /// <summary>
    /// Отправляет приглашение. Возвращает текст ошибки либо <c>null</c> при успехе.
    /// Отметка о приглашении ставится только после успешной отправки — иначе клиент,
    /// которому не дошло сообщение, выпал бы из списка на две недели.
    /// </summary>
    public async Task<string?> InviteBackAsync(ClientSummary client, string? customText = null,
        CancellationToken cancellationToken = default)
    {
        if (!client.TelegramConnected)
            return "Клиент не подключил Telegram — напишите или позвоните вручную";

        var bot = _telegram.Bots.Client;
        if (!bot.IsConfigured)
            return "Токен клиентского бота не задан";

        var text = string.IsNullOrWhiteSpace(customText) ? ComposeText(client) : customText!;
        var sent = await bot.SendMessageAsync(client.TelegramChatId!, text, null, cancellationToken);

        if (!sent)
        {
            _logger.LogWarning("Приглашение клиенту {ClientId} не ушло", client.Id);
            return "Telegram не принял сообщение — возможно, клиент заблокировал бота";
        }

        _clients.SetLastReminded(client.Id);
        return null;
    }

    /// <summary>Текст приглашения. Имя и адрес — из настроек, чтобы админка оставалась источником правды.</summary>
    private string ComposeText(ClientSummary client)
    {
        var name = client.Name.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        var address = _settings.Get("Site.Address", "");
        var phone = _settings.Get("Site.Phone", "");
        var site = _settings.Get("Site.Telegram", "");

        var text = $"💫 {TelegramService.Escape(name ?? client.Name)}, здравствуйте!\n\n" +
                   "Давно вас не было в ELORA — будем рады видеть снова.\n" +
                   "Записаться можно прямо здесь, командой /book.";

        if (!string.IsNullOrWhiteSpace(address))
            text += $"\n\nМы находимся: {TelegramService.Escape(address)}";
        if (!string.IsNullOrWhiteSpace(phone))
            text += $"\nТелефон: {TelegramService.PhoneLink(phone)}";

        return text;
    }

    /// <summary>Сколько клиентов сейчас в списке «пора напомнить» — для дашборда.</summary>
    public int DueCount() => DueForVisit().Count;

    public static string ThresholdLabel(int weeks) => $"{weeks} {Money.Weeks(weeks)}";
}
