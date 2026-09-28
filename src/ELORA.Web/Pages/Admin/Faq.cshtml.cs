using ELORA.Web.Data.Repositories;
using ELORA.Web.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ELORA.Web.Pages.Admin;

public class FaqModel : PageModel
{
    private readonly ContentRepository _content;

    public FaqModel(ContentRepository content) => _content = content;

    public List<FaqItem> Items { get; private set; } = new();

    public void OnGet() => Items = _content.GetFaq(onlyActive: false);

    public IActionResult OnPostSave(int id, string question, string answer, int sortOrder, bool isActive)
    {
        if (string.IsNullOrWhiteSpace(question) || string.IsNullOrWhiteSpace(answer))
        {
            TempData["FlashError"] = "Заполните вопрос и ответ";
            return RedirectToPage();
        }

        var item = id > 0 ? _content.GetFaqItem(id) : new FaqItem();
        if (item is null)
        {
            TempData["FlashError"] = "Вопрос не найден";
            return RedirectToPage();
        }

        item.Question = question.Trim();
        item.Answer = answer.Trim();
        item.SortOrder = sortOrder;
        item.IsActive = isActive;

        _content.SaveFaq(item);
        TempData["Flash"] = id > 0 ? "Вопрос обновлён" : "Вопрос добавлен";
        return RedirectToPage();
    }

    public IActionResult OnPostDelete(int id)
    {
        _content.DeleteFaq(id);
        TempData["Flash"] = "Вопрос удалён";
        return RedirectToPage();
    }
}
