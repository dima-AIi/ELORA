using ELORA.Web.Data.Repositories;
using ELORA.Web.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ELORA.Web.Pages.Admin;

public class CatalogModel : PageModel
{
    private readonly CatalogRepository _catalog;

    public CatalogModel(CatalogRepository catalog) => _catalog = catalog;

    public List<ServiceCategory> Categories { get; private set; } = new();
    public List<Service> Services { get; private set; } = new();

    /// <summary>
    /// Сколько записей сделано на каждую услугу. Кнопка «Удалить» показывается только там,
    /// где удаление действительно возможно: с записями услуга не удаляется, а скрывается.
    /// </summary>
    public Dictionary<int, int> BookingCounts { get; private set; } = new();

    public void OnGet()
    {
        Categories = _catalog.GetCategories(false);
        Services = _catalog.GetServices(false);
        BookingCounts = _catalog.GetBookingCounts();
    }

    public IActionResult OnPostSaveService(
        int id, int categoryId, string name, string? description, int durationMinutes, decimal price, bool isActive)
    {
        if (string.IsNullOrWhiteSpace(name) || categoryId <= 0 || durationMinutes <= 0)
        {
            TempData["FlashError"] = "Проверьте название, категорию и длительность";
            return RedirectToPage();
        }

        var service = id > 0 ? _catalog.GetService(id) : new Service();
        if (service is null)
        {
            TempData["FlashError"] = "Услуга не найдена";
            return RedirectToPage();
        }

        service.CategoryId = categoryId;
        service.Name = name.Trim();
        service.Description = description;
        service.DurationMinutes = durationMinutes;
        service.Price = price;
        service.IsActive = isActive;

        var savedId = _catalog.SaveService(service);
        TempData["Flash"] = id > 0 ? "Услуга обновлена" : "Услуга добавлена";
        return RedirectToPage(new { serviceId = savedId });
    }

    public IActionResult OnPostToggleService(int id, bool active)
    {
        _catalog.SetServiceActive(id, active);
        TempData["Flash"] = active ? "Услуга включена" : "Услуга скрыта";
        return RedirectToPage();
    }

    /// <summary>
    /// Удалить услугу совсем. Если на неё есть записи — отказываем и предлагаем скрыть:
    /// в записях лежит история клиентов, и услуга из них пропадёт. Проверка дублирует
    /// скрытие кнопки, потому что POST можно собрать руками.
    /// </summary>
    public IActionResult OnPostDeleteService(int id)
    {
        var service = _catalog.GetService(id);
        if (service is null)
        {
            TempData["FlashError"] = "Услуга не найдена";
            return RedirectToPage();
        }

        if (!_catalog.DeleteService(id))
        {
            TempData["FlashError"] =
                $"На услугу «{service.Name}» есть записи — удалить её нельзя, иначе поедет история клиентов. " +
                "Вместо удаления она скрывается с сайта.";
            return RedirectToPage();
        }

        TempData["Flash"] = $"Услуга «{service.Name}» удалена";
        return RedirectToPage();
    }

    public IActionResult OnPostSaveCategory(int id, string name, string slug, string? description, int sortOrder, bool isActive)
    {
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(slug))
        {
            TempData["FlashError"] = "Заполните название и slug категории";
            return RedirectToPage();
        }

        var category = id > 0 ? _catalog.GetCategory(id) : new ServiceCategory();
        if (category is null)
        {
            TempData["FlashError"] = "Категория не найдена";
            return RedirectToPage();
        }

        category.Name = name.Trim();
        category.Slug = slug.Trim().ToLowerInvariant();
        category.Description = description;
        category.SortOrder = sortOrder;
        category.IsActive = isActive;

        _catalog.SaveCategory(category);
        TempData["Flash"] = "Категория сохранена";
        return RedirectToPage();
    }
}
