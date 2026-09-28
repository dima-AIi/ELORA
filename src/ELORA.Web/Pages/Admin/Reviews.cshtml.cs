using ELORA.Web.Data;
using ELORA.Web.Data.Repositories;
using ELORA.Web.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ELORA.Web.Pages.Admin;

public class ReviewsModel : PageModel
{
    private readonly ContentRepository _content;

    public ReviewsModel(ContentRepository content) => _content = content;

    public List<Review> Items { get; private set; } = new();

    /// <summary>
    /// Сколько отзывов осталось демонстрационными. Выдуманные отзывы — недостоверная реклама
    /// (ч. 3 ст. 5 ФЗ «О рекламе»), поэтому о них надо сказать прямо в админке, а не надеяться,
    /// что владелец сам догадается их заменить.
    /// </summary>
    public int DemoLeft { get; private set; }

    public void OnGet()
    {
        Items = _content.GetReviews(onlyActive: false);
        DemoLeft = Items.Count(review =>
            Seed.DemoReviews.Any(demo => demo.Text == review.Text));
    }

    public IActionResult OnPostSave(int id, string clientName, string? avatarPath, int rating, string text, int sortOrder, bool isActive)
    {
        if (string.IsNullOrWhiteSpace(clientName) || string.IsNullOrWhiteSpace(text))
        {
            TempData["FlashError"] = "Заполните имя и текст отзыва";
            return RedirectToPage();
        }

        var review = id > 0 ? _content.GetReview(id) : new Review();
        if (review is null)
        {
            TempData["FlashError"] = "Отзыв не найден";
            return RedirectToPage();
        }

        review.ClientName = clientName.Trim();
        review.AvatarPath = string.IsNullOrWhiteSpace(avatarPath) ? null : avatarPath.Trim();
        review.Rating = Math.Clamp(rating, 1, 5);
        review.Text = text.Trim();
        review.SortOrder = sortOrder;
        review.IsActive = isActive;

        _content.SaveReview(review);
        TempData["Flash"] = id > 0 ? "Отзыв обновлён" : "Отзыв добавлен";
        return RedirectToPage();
    }

    public IActionResult OnPostDelete(int id)
    {
        _content.DeleteReview(id);
        TempData["Flash"] = "Отзыв удалён";
        return RedirectToPage();
    }
}
