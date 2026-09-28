using ELORA.Web.Data.Repositories;
using ELORA.Web.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ELORA.Web.Pages.Admin;

public class WorksModel : PageModel
{
    private readonly ContentRepository _content;
    private readonly MasterRepository _masters;

    public WorksModel(ContentRepository content, MasterRepository masters)
    {
        _content = content;
        _masters = masters;
    }

    public List<Work> Items { get; private set; } = new();
    public List<Master> Masters { get; private set; } = new();
    public Dictionary<string, int> Counts { get; private set; } = new();

    [BindProperty(SupportsGet = true)] public string? Category { get; set; }

    public void OnGet()
    {
        Masters = _masters.GetAll(false);
        Counts = _content.CountWorksByCategory(false);
        Items = _content.GetWorks(string.IsNullOrWhiteSpace(Category) ? null : Category, onlyActive: false);
    }

    public IActionResult OnPostSave(int id, string title, string category, string imagePath, string? description, int? masterId, int sortOrder, bool isActive)
    {
        if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(imagePath))
        {
            TempData["FlashError"] = "Заполните название и путь к изображению";
            return RedirectToPage();
        }

        if (!WorkCategories.IsKnown(category))
        {
            TempData["FlashError"] = "Неизвестная категория";
            return RedirectToPage();
        }

        var work = id > 0 ? _content.GetWork(id) : new Work();
        if (work is null)
        {
            TempData["FlashError"] = "Работа не найдена";
            return RedirectToPage();
        }

        work.Title = title.Trim();
        work.Category = category;
        work.ImagePath = imagePath.Trim();
        work.Description = description;
        work.MasterId = masterId is > 0 ? masterId : null;
        work.SortOrder = sortOrder;
        work.IsActive = isActive;

        _content.SaveWork(work);
        TempData["Flash"] = id > 0 ? "Работа обновлена" : "Работа добавлена";
        return RedirectToPage(new { Category });
    }

    public IActionResult OnPostDelete(int id)
    {
        _content.DeleteWork(id);
        TempData["Flash"] = "Работа удалена";
        return RedirectToPage(new { Category });
    }
}
