using ELORA.Web.Data.Repositories;
using ELORA.Web.Models;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ELORA.Web.Pages;

public class ServicesModel : PageModel
{
    private readonly CatalogRepository _catalog;

    public ServicesModel(CatalogRepository catalog) => _catalog = catalog;

    public List<ServiceCategory> Categories { get; private set; } = new();
    public List<ServiceGroup> Groups { get; private set; } = new();

    public void OnGet()
    {
        Categories = _catalog.GetCategories();
        var services = _catalog.GetServices();

        Groups = Categories
            .Select(category => new ServiceGroup
            {
                Category = category,
                Slug = category.Slug,
                Services = services.Where(s => s.CategoryId == category.Id).ToList()
            })
            .Where(g => g.Services.Count > 0)
            .ToList();
    }
}

public class ServiceGroup
{
    public ServiceCategory Category { get; set; } = new();
    public string Slug { get; set; } = "";
    public List<Service> Services { get; set; } = new();
}
