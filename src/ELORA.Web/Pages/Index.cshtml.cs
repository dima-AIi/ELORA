using ELORA.Web.Data.Repositories;
using ELORA.Web.Models;
using ELORA.Web.Services;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ELORA.Web.Pages;

public class IndexModel : PageModel
{
    private readonly CatalogRepository _catalog;
    private readonly WorksService _works;
    private readonly ContentRepository _content;
    private readonly SettingsRepository _settings;

    public IndexModel(
        CatalogRepository catalog,
        WorksService works,
        ContentRepository content,
        SettingsRepository settings)
    {
        _catalog = catalog;
        _works = works;
        _content = content;
        _settings = settings;
    }

    public List<ServiceCategory> Categories { get; private set; } = new();
    public List<Work> FeaturedWorks { get; private set; } = new();
    public List<PriceGroup> PriceGroups { get; private set; } = new();
    public List<Review> Reviews { get; private set; } = new();
    public List<FaqItem> Faq { get; private set; } = new();
    public int WorksTotal { get; private set; }

    public void OnGet()
    {
        Categories = _catalog.GetCategories();
        FeaturedWorks = _works.GetFeatured(6);
        WorksTotal = _content.GetWorks(null, true).Count;
        // По референсу: три колонки по четыре строки в каждой.
        PriceGroups = PriceListBuilder.Build(_catalog.GetServices(), take: 3, rowsPerGroup: 4);
        Reviews = _content.GetReviews(limit: 4);
        Faq = _content.GetFaq();
    }
}
