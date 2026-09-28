using ELORA.Web.Data.Repositories;
using ELORA.Web.Models;

namespace ELORA.Web.Services;

/// <summary>Портфолио: выдача галереи и счётчики по категориям.</summary>
public sealed class WorksService
{
    private readonly ContentRepository _content;

    public WorksService(ContentRepository content) => _content = content;

    public List<Work> GetGallery(string? category = null) =>
        _content.GetWorks(string.IsNullOrWhiteSpace(category) ? null : category);

    public Dictionary<string, int> GetCounts() => _content.CountWorksByCategory();

    public List<(string Key, string Title, int Count)> GetFilters()
    {
        var counts = GetCounts();
        var total = counts.Values.Sum();
        var list = new List<(string, string, int)> { ("all", "Все", total) };
        foreach (var (key, title) in WorkCategories.All)
            list.Add((key, title, counts.TryGetValue(key, out var c) ? c : 0));
        return list;
    }

    /// <summary>
    /// Порядок карточек в витрине на главной. Отличается от порядка фильтров:
    /// соседние карточки — из разных услуг (как в референсе), похожие крупные кадры
    /// (ресницы и брови — оба «глаз») не стоят рядом.
    /// </summary>
    private static readonly string[] FeaturedOrder =
    {
        WorkCategories.Manicure,
        WorkCategories.Lashes,
        WorkCategories.Hair,
        WorkCategories.Brows,
        WorkCategories.Tattoo,
        WorkCategories.Pedicure
    };

    /// <summary>
    /// Работы для блока «Наши работы» на главной.
    /// Берём по одной работе из каждой категории — в референсе ряд собран из разных услуг,
    /// а не из шести маникюров подряд.
    /// </summary>
    public List<Work> GetFeatured(int count = 6)
    {
        var all = _content.GetWorks(null, true);
        var picked = new List<Work>();

        foreach (var key in FeaturedOrder)
        {
            var first = all.FirstOrDefault(w => w.Category == key);
            if (first is not null) picked.Add(first);
        }

        // Если категорий меньше, чем нужно слотов — добираем остальными по порядку.
        foreach (var work in all)
        {
            if (picked.Count >= count) break;
            if (!picked.Contains(work)) picked.Add(work);
        }

        return picked.Take(count).ToList();
    }
}
