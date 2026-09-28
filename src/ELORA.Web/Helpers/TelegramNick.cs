namespace ELORA.Web.Helpers;

/// <summary>
/// Ник в Telegram — единственное, что клиент вводит руками в поле «Ник в Telegram».
/// </summary>
/// <remarks>
/// Поле свободного текста здесь опаснее пустого: владелец видит в админке «@» с чем угодно
/// после него и не может написать человеку. Поэтому принимаем только настоящий ник
/// (<c>@nick</c>, 3–32 символа из латиницы, цифр и подчёркивания) и приводим его к одному
/// виду. Всё остальное — пустая строка, то есть «ника нет».
///
/// Ту же проверку повторяет браузер (<c>wwwroot/js/booking.js</c>), но доверять ей нельзя:
/// адрес <c>POST /api/bookings</c> открыт, и запрос можно послать руками.
/// </remarks>
public static class TelegramNick
{
    /// <summary>Приводит ввод к виду <c>@nick</c>. Не ник — <c>null</c>.</summary>
    public static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;

        var text = value.Trim();

        // Ссылку t.me/anna или https://telegram.me/anna принимаем как ник: люди присылают
        // именно её, и это по-прежнему настоящий ник, а не «что угодно».
        foreach (var prefix in new[] { "https://t.me/", "http://t.me/", "https://telegram.me/", "http://telegram.me/", "t.me/", "telegram.me/" })
        {
            if (text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                text = text[prefix.Length..];
                break;
            }
        }

        // Хвост ссылки вида t.me/anna?start=abc — параметры к нику не относятся.
        var cut = text.IndexOfAny(new[] { '?', '/', ' ', '\t' });
        if (cut >= 0) text = text[..cut];

        text = text.TrimStart('@').Trim();

        if (text.Length is < 3 or > 32) return null;
        if (!text.All(c => char.IsAsciiLetterOrDigit(c) || c == '_')) return null;

        // Telegram не отдаёт ники, начинающиеся с цифры или подчёркивания, и запрещает
        // двойное подчёркивание. Проверяем, чтобы в админке не оказалось несуществующего ника.
        if (char.IsAsciiDigit(text[0]) || text[0] == '_') return null;
        if (text.Contains("__")) return null;
        if (text.EndsWith('_')) return null;

        return "@" + text;
    }

    /// <summary>Ник без собачки — для ссылок и поиска.</summary>
    public static string? WithoutAt(string? normalized) =>
        string.IsNullOrEmpty(normalized) ? null : normalized.TrimStart('@');
}
