using ELORA.Web.Models;
using ELORA.Web.Services;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ELORA.Web.Pages;

public class WorksModel : PageModel
{
    private readonly WorksService _works;

    public WorksModel(WorksService works) => _works = works;

    public List<Work> Works { get; private set; } = new();
    public List<(string Key, string Title, int Count)> Filters { get; private set; } = new();
    public string ActiveCategory { get; private set; } = "all";

    public void OnGet(string? category)
    {
        ActiveCategory = !string.IsNullOrWhiteSpace(category) && WorkCategories.IsKnown(category)
            ? category
            : "all";

        Works = _works.GetGallery(ActiveCategory == "all" ? null : ActiveCategory);
        Filters = _works.GetFilters();
    }
}
