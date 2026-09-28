using ELORA.Web.Models;

namespace ELORA.Web.Services;

/// <summary>
/// Группировка услуг для блока «Прайс-лист».
/// На главной по референсу три колонки: Маникюр, Педикюр, Ресницы и брови.
/// </summary>
public static class PriceListBuilder
{
    public static readonly (string Slug, string Title, string[] Slugs)[] Groups =
    {
        ("manicure", "Маникюр", new[] { "manicure" }),
        ("pedicure", "Педикюр", new[] { "pedicure" }),
        ("lashes-brows", "Ресницы и брови", new[] { "lashes", "brows" }),
        ("hair", "Волосы", new[] { "hair" }),
        ("tattoo", "Тату", new[] { "tattoo" })
    };

    /// <param name="take">Сколько групп (колонок) показать. null — все.</param>
    /// <param name="rowsPerGroup">
    /// Сколько строк услуг оставить в колонке. По референсу на главной — ровно 4.
    /// null — без ограничения (страница /price).
    /// </param>
    public static List<PriceGroup> Build(IEnumerable<Service> services, int? take = null, int? rowsPerGroup = null)
    {
        var all = services.ToList();
        var groups = new List<PriceGroup>();

        var source = take.HasValue ? Groups.Take(take.Value) : Groups;

        foreach (var (slug, title, slugs) in source)
        {
            var items = all
                .Where(s => slugs.Contains(s.CategorySlug))
                .Select(s => new PriceItem { Name = Shorten(s.Name), FullName = s.Name, Price = s.Price })
                .ToList();

            if (rowsPerGroup.HasValue && items.Count > rowsPerGroup.Value)
                items = items.Take(rowsPerGroup.Value).ToList();

            if (items.Count == 0) continue;

            groups.Add(new PriceGroup { Slug = slug, Title = title, Items = items });
        }

        return groups;
    }

    /// <summary>
    /// В референсе в колонках короткие названия: «Классический», «Гель-лак»,
    /// а не «Классический маникюр». Название категории уже стоит в заголовке колонки.
    /// </summary>
    private static readonly HashSet<string> CategoryWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "маникюр", "педикюр", "ногтей", "ресниц", "ресницы", "бровей", "брови", "стоп", "волос", "+"
    };

    internal static string Shorten(string name)
    {
        var tokens = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var kept = tokens.Where(t => !CategoryWords.Contains(t)).ToArray();

        // «Комплекс брови + ресницы» после чистки схлопнулось бы в «Комплекс» —
        // такое название теряет смысл, оставляем исходное.
        if (kept.Length == 0) return name;
        if (kept.Length == 1 && tokens.Length >= 4) return name;

        var result = string.Join(' ', kept).Trim();
        if (result.Length == 0) return name;

        return char.ToUpper(result[0], System.Globalization.CultureInfo.GetCultureInfo("ru-RU")) + result[1..];
    }
}

public sealed class PriceGroup
{
    public string Slug { get; set; } = "";
    public string Title { get; set; } = "";
    public List<PriceItem> Items { get; set; } = new();
}

public sealed class PriceItem
{
    public string Name { get; set; } = "";
    public string FullName { get; set; } = "";
    public decimal Price { get; set; }

    public string PriceLabel =>
        Price.ToString("N0", System.Globalization.CultureInfo.GetCultureInfo("ru-RU"))
             .Replace('\u00A0', ' ') + " ₽";
}
