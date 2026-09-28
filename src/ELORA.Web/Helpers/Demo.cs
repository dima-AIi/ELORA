using ELORA.Web.Data;

namespace ELORA.Web.Helpers;

/// <summary>
/// Признаки демонстрационных данных — тех, что пришли из сида и к реальной студии
/// отношения не имеют.
/// </summary>
/// <remarks>
/// Отзывы и мастера в базе не помечены флагом: столбца «демонстрационный» в схеме нет,
/// и добавлять его ради пометки на странице — лишняя миграция. Вместо этого текст отзыва
/// сверяется с теми, что положил сид: у реального отзыва он другой, поэтому пометка
/// исчезает сама, как только владелец добавит настоящие.
/// </remarks>
public static class Demo
{
    /// <summary>Отзыв из сида, а не написанный владельцем.</summary>
    public static bool IsReview(string? text) =>
        !string.IsNullOrWhiteSpace(text) && Seed.DemoReviews.Any(demo => demo.Text == text);

    /// <summary>Есть ли среди отзывов хотя бы один демонстрационный.</summary>
    public static bool HasDemoReviews(IEnumerable<string?> texts) => texts.Any(IsReview);
}
