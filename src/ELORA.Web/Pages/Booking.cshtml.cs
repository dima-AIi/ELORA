using ELORA.Web.Data.Repositories;
using ELORA.Web.Helpers;
using ELORA.Web.Models;
using ELORA.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ELORA.Web.Pages;

public class BookingModel : PageModel
{
    private readonly CatalogRepository _catalog;
    private readonly MasterRepository _masters;
    private readonly SettingsRepository _settings;

    public BookingModel(CatalogRepository catalog, MasterRepository masters, SettingsRepository settings)
    {
        _catalog = catalog;
        _masters = masters;
        _settings = settings;
    }

    public List<ServiceCategory> Categories { get; private set; } = new();
    public List<Service> Services { get; private set; } = new();
    public List<Master> Masters { get; private set; } = new();

    /// <summary>Услуга, выбранная по ссылке (?serviceId=12) — шаг 1 сразу пройден.</summary>
    public int PreselectedServiceId { get; private set; }

    public string Phone { get; private set; } = "";
    public string Address { get; private set; } = "";
    public string TelegramBotUsername { get; private set; } = "";

    public void OnGet(int? serviceId)
    {
        Categories = _catalog.GetCategories();
        Services = _catalog.GetServices().Where(s => s.IsActive).ToList();
        Masters = _masters.GetAll();

        Phone = _settings.Get("Site.Phone", "+7 (999) 123-45-67");
        Address = _settings.Get("Site.Address", "Москва, ул. Примерная, 12");
        TelegramBotUsername = _settings.Get("Telegram.BotUsername", "");

        if (serviceId is > 0 && Services.Any(s => s.Id == serviceId))
            PreselectedServiceId = serviceId.Value;
    }

    /// <summary>JSON-описание каталога для клиентского сценария записи.</summary>
    public string CatalogJson()
    {
        var payload = Services.Select(s => new
        {
            id = s.Id,
            name = s.Name,
            categoryId = s.CategoryId,
            categorySlug = s.CategorySlug,
            categoryName = s.CategoryName,
            duration = s.DurationMinutes,
            durationLabel = Money.Duration(s.DurationMinutes),
            price = s.Price,
            priceLabel = Money.Format(s.Price),
            description = s.Description
        });

        return System.Text.Json.JsonSerializer.Serialize(payload);
    }
}
