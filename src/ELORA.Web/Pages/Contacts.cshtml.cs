using ELORA.Web.Data.Repositories;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ELORA.Web.Pages;

public class ContactsModel : PageModel
{
    private readonly SettingsRepository _settings;
    private readonly MasterRepository _masters;

    public ContactsModel(SettingsRepository settings, MasterRepository masters)
    {
        _settings = settings;
        _masters = masters;
    }

    public string Phone { get; private set; } = "";
    public string Address { get; private set; } = "";
    public string Email { get; private set; } = "";
    public string WorkHours { get; private set; } = "";
    public string Telegram { get; private set; } = "";
    public string Vk { get; private set; } = "";
    public string Instagram { get; private set; } = "";

    public int MasterCount { get; private set; }

    public string PhoneHref => Phone
        .Replace(" ", "").Replace("(", "").Replace(")", "").Replace("-", "");

    public void OnGet()
    {
        Phone = _settings.Get("Site.Phone", "+7 (999) 123-45-67");
        Address = _settings.Get("Site.Address", "Москва, ул. Примерная, 12");
        Email = _settings.Get("Site.Email", "hello@elora.ru");
        WorkHours = _settings.Get("Site.WorkHours", "Пн–Сб 09:00 – 19:00, Вс — выходной");
        Telegram = _settings.Get("Site.Telegram", "");
        Vk = _settings.Get("Site.Vk", "");
        Instagram = _settings.Get("Site.Instagram", "");

        MasterCount = _masters.GetAll().Count;
    }
}
