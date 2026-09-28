using ELORA.Web.Data.Repositories;
using ELORA.Web.Models;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ELORA.Web.Pages;

public class AboutModel : PageModel
{
    private readonly MasterRepository _masters;
    private readonly CatalogRepository _catalog;
    private readonly ContentRepository _content;
    private readonly SettingsRepository _settings;

    public AboutModel(
        MasterRepository masters,
        CatalogRepository catalog,
        ContentRepository content,
        SettingsRepository settings)
    {
        _masters = masters;
        _catalog = catalog;
        _content = content;
        _settings = settings;
    }

    public List<Master> Masters { get; private set; } = new();
    public List<ServiceCategory> Categories { get; private set; } = new();
    public List<Review> Reviews { get; private set; } = new();

    public int ServiceCount { get; private set; }
    public int WorksCount { get; private set; }
    public string Phone { get; private set; } = "";
    public string Address { get; private set; } = "";
    public string WorkHours { get; private set; } = "";

    public void OnGet()
    {
        Masters = _masters.GetAll();
        Categories = _catalog.GetCategories();
        Reviews = _content.GetReviews().Take(3).ToList();
        ServiceCount = _catalog.GetServices().Count;
        WorksCount = _content.GetWorks().Count;

        Phone = _settings.Get("Site.Phone", "+7 (999) 123-45-67");
        Address = _settings.Get("Site.Address", "Москва, ул. Примерная, 12");
        WorkHours = _settings.Get("Site.WorkHours", "Пн–Сб 09:00 – 19:00, Вс — выходной");
    }
}
