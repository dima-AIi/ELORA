using ELORA.Web.Data.Repositories;
using ELORA.Web.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ELORA.Web.Pages.Admin;

public class MastersModel : PageModel
{
    private readonly MasterRepository _masters;
    private readonly CatalogRepository _catalog;

    public MastersModel(MasterRepository masters, CatalogRepository catalog)
    {
        _masters = masters;
        _catalog = catalog;
    }

    public List<Master> Items { get; private set; } = new();
    public List<Service> Services { get; private set; } = new();

    /// <summary>
    /// Сколько записей сделано к каждому мастеру. Нужно, чтобы кнопка «Удалить» не висела
    /// там, где сервер всё равно откажет: у мастера с записями удаление запрещено.
    /// </summary>
    public Dictionary<int, int> BookingCounts { get; private set; } = new();

    public void OnGet()
    {
        Items = _masters.GetAll(false);
        Services = _catalog.GetServices(false);
        BookingCounts = _masters.GetBookingCounts();
    }

    public IActionResult OnPostSave(
        int id, string name, string? specialization, string? description,
        string? phone, string? photoPath, int sortOrder, bool isActive, int[]? serviceIds)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            TempData["FlashError"] = "Укажите имя мастера";
            return RedirectToPage();
        }

        var master = id > 0 ? _masters.GetById(id) : new Master();
        if (master is null)
        {
            TempData["FlashError"] = "Мастер не найден";
            return RedirectToPage();
        }

        master.Name = name.Trim();
        master.Specialization = specialization;
        master.Description = description;
        master.Phone = phone;
        master.PhotoPath = photoPath;
        master.SortOrder = sortOrder;
        master.IsActive = isActive;
        master.ServiceIds = (serviceIds ?? Array.Empty<int>()).ToList();

        _masters.Save(master);
        TempData["Flash"] = id > 0 ? "Мастер обновлён" : "Мастер добавлен";
        return RedirectToPage();
    }

    public IActionResult OnPostToggle(int id, bool active)
    {
        _masters.SetActive(id, active);
        TempData["Flash"] = active ? "Мастер включён" : "Мастер скрыт";
        return RedirectToPage();
    }

    /// <summary>
    /// Удалить мастера. Записи ссылаются на мастера жёстко, поэтому мастера с записями
    /// удалить нельзя — иначе поедет история клиентов. Проверка повторяется здесь, а не
    /// только скрытием кнопки: POST можно собрать руками, минуя разметку.
    /// </summary>
    public IActionResult OnPostDelete(int id)
    {
        var master = _masters.GetById(id);
        if (master is null)
        {
            TempData["FlashError"] = "Мастер не найден";
            return RedirectToPage();
        }

        if (!_masters.Delete(id))
        {
            TempData["FlashError"] =
                $"У мастера «{master.Name}» есть записи — удалить его нельзя, иначе поедет история клиентов. " +
                "Снимите галочку «Мастер работает», и он уйдёт с сайта.";
            return RedirectToPage();
        }

        TempData["Flash"] = $"Мастер «{master.Name}» удалён";
        return RedirectToPage();
    }
}
