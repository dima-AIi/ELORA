using ELORA.Web.Data.Repositories;
using ELORA.Web.Helpers;
using ELORA.Web.Models;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ELORA.Web.Pages;

public class ManageModel : PageModel
{
    private readonly BookingRepository _bookings;
    private readonly MasterRepository _masters;
    private readonly CatalogRepository _catalog;
    private readonly SettingsRepository _settings;

    public ManageModel(
        BookingRepository bookings,
        MasterRepository masters,
        CatalogRepository catalog,
        SettingsRepository settings)
    {
        _bookings = bookings;
        _masters = masters;
        _catalog = catalog;
        _settings = settings;
    }

    public Booking? Booking { get; private set; }
    public List<Master> AvailableMasters { get; private set; } = new();
    public string Phone { get; private set; } = "";

    /// <summary>
    /// Ссылка привязки Telegram, пока чат не подключён. Из чата записью управлять удобнее,
    /// чем по ссылке, и без привязки клиент не получит ни подтверждения, ни напоминания.
    /// </summary>
    public string TelegramLink { get; private set; } = "";

    public bool CanManage => Booking is not null && Booking.IsActiveStatus;

    public void OnGet(string token)
    {
        Phone = _settings.Get("Site.Phone", "+7 (999) 123-45-67");
        Booking = _bookings.GetByToken(token);

        if (Booking is null) return;

        // Ссылку из Telegram тоже запоминаем: клиент открыл запись по ней — значит, это его
        // браузер, и дальше он найдёт запись на сайте сам.
        MyBookings.Remember(Request, Response, token);

        // Перенести можно только к мастеру, который оказывает ту же услугу.
        AvailableMasters = _masters.GetForService(Booking.ServiceId);

        // Ссылку привязки показываем, пока чат не подключён: иначе клиент, закрывший страницу
        // успешной записи, остаётся без подтверждений и напоминаний — бот не может писать первым.
        if (!string.IsNullOrWhiteSpace(Booking.ClientTelegramChatId)) return;

        var bot = _settings.Get("Telegram.BotUsername", "");
        if (string.IsNullOrWhiteSpace(bot)) return;

        var name = bot.StartsWith('@') ? bot[1..] : bot;
        var code = _bookings.GetOrCreateLinkCode(Booking.ClientId, Booking.Id);
        TelegramLink = $"https://t.me/{name}?start={code}";
    }
}
