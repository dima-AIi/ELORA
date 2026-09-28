using ELORA.Web.Data.Repositories;
using ELORA.Web.Helpers;
using ELORA.Web.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ELORA.Web.Pages;

/// <summary>
/// «Мои записи» — что браузер помнит о клиенте. Регистрация не нужна: сервер при записи
/// выдаёт токен управления, мы держим его в cookie и по нему достаём записи из базы.
/// </summary>
public class MyModel : PageModel
{
    private readonly BookingRepository _bookings;
    private readonly SettingsRepository _settings;

    public MyModel(BookingRepository bookings, SettingsRepository settings)
    {
        _bookings = bookings;
        _settings = settings;
    }

    /// <summary>Активные записи, которые ещё не прошли, — их клиент и пришёл смотреть.</summary>
    public List<Booking> Upcoming { get; private set; } = new();

    /// <summary>Всё остальное: отменённые, завершённые и прошедшие — как история.</summary>
    public List<Booking> History { get; private set; } = new();

    public string Phone { get; private set; } = "";
    public string TelegramBotUsername { get; private set; } = "";

    /// <summary>
    /// Ссылка привязки Telegram для клиента, у которого чат ещё не подключён.
    /// </summary>
    /// <remarks>
    /// Страница успешной записи показывает эту ссылку один раз и закрывается. Клиент,
    /// который её закрыл, привязать чат больше не мог нигде — и не получал ни подтверждений,
    /// ни напоминаний. Показываем ссылку здесь, пока чат не подключён.
    /// </remarks>
    public string TelegramLink { get; private set; } = "";

    public bool AnythingRemembered => Upcoming.Count > 0 || History.Count > 0;

    public void OnGet()
    {
        Phone = _settings.Get("Site.Phone", "+7 (999) 123-45-67");
        TelegramBotUsername = _settings.Get("Telegram.BotUsername", "");

        var tokens = MyBookings.Read(Request);
        if (tokens.Count == 0) return;

        var now = DateTime.Now;
        var bookings = _bookings.GetByTokens(tokens);

        Upcoming = bookings
            .Where(b => b.IsActiveStatus && b.StartLocal >= now)
            .OrderBy(b => b.StartLocal)
            .ToList();

        History = bookings
            .Where(b => !(b.IsActiveStatus && b.StartLocal >= now))
            .OrderByDescending(b => b.StartLocal)
            .ToList();

        BuildTelegramLink();
    }

    /// <summary>
    /// Готовит ссылку привязки, если хотя бы у одной записи чат не подключён. Код берём
    /// у самого свежего такого клиента: он и есть тот, кто читает страницу сейчас.
    /// </summary>
    private void BuildTelegramLink()
    {
        if (string.IsNullOrWhiteSpace(TelegramBotUsername)) return;

        var unbound = Upcoming.Concat(History)
            .FirstOrDefault(b => string.IsNullOrWhiteSpace(b.ClientTelegramChatId));

        if (unbound is null) return;

        var name = TelegramBotUsername.StartsWith('@') ? TelegramBotUsername[1..] : TelegramBotUsername;
        var code = _bookings.GetOrCreateLinkCode(unbound.ClientId, unbound.Id);
        TelegramLink = $"https://t.me/{name}?start={code}";
    }

    /// <summary>«Забыть этот браузер» — для чужого или общего компьютера.</summary>
    public IActionResult OnPostForget()
    {
        MyBookings.Forget(Request, Response);
        TempData["Flash"] = "Готово: на этом устройстве записи больше не показываются. " +
                            "Ссылки из Telegram продолжают работать.";
        return RedirectToPage();
    }
}
