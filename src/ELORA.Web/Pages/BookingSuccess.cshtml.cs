using ELORA.Web.Data.Repositories;
using ELORA.Web.Helpers;
using ELORA.Web.Models;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ELORA.Web.Pages;

public class BookingSuccessModel : PageModel
{
    private readonly BookingRepository _bookings;
    private readonly SettingsRepository _settings;

    public BookingSuccessModel(BookingRepository bookings, SettingsRepository settings)
    {
        _bookings = bookings;
        _settings = settings;
    }

    public Booking? Booking { get; private set; }
    public string TelegramLink { get; private set; } = "";
    public bool TelegramConnected { get; private set; }
    public string Phone { get; private set; } = "";

    public void OnGet(string? token)
    {
        Phone = _settings.Get("Site.Phone", "+7 (999) 123-45-67");

        if (string.IsNullOrWhiteSpace(token)) return;

        Booking = _bookings.GetByToken(token);
        if (Booking is null) return;

        TelegramConnected = !string.IsNullOrWhiteSpace(Booking.ClientTelegramChatId);
        if (TelegramConnected) return;

        // Ссылка обязана нести параметр start=<код>: без него бот получает обычный
        // /start, не знает, к какому клиенту привязать чат, и отвечает общим текстом
        // «запишитесь на сайте». Код берём из БД — страница открывается отдельным
        // GET, и код, созданный при POST записи, до неё не доходит.
        var bot = _settings.Get("Telegram.BotUsername", "");
        if (string.IsNullOrWhiteSpace(bot)) return;

        var name = bot.StartsWith('@') ? bot[1..] : bot;
        var code = _bookings.GetOrCreateLinkCode(Booking.ClientId, Booking.Id);
        TelegramLink = $"https://t.me/{name}?start={code}";
    }

    public string ManageUrl => Booking is null ? "" : $"/manage/{Booking.ManageToken}";

    public string AbsoluteManageUrl =>
        Booking is null ? "" : $"{Request.Scheme}://{Request.Host}{ManageUrl}";

    public string StatusTitle => Booking is null ? "" : BookingStatuses.Title(Booking.Status);
}
