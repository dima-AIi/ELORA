namespace ELORA.Web.Models;

public static class BookingStatuses
{
    public const string Pending = "Pending";
    public const string Confirmed = "Confirmed";
    public const string Cancelled = "Cancelled";
    public const string Completed = "Completed";

    public static readonly string[] All = { Pending, Confirmed, Cancelled, Completed };

    public static string Title(string status) => status switch
    {
        Pending => "Ожидает подтверждения",
        Confirmed => "Подтверждена",
        Cancelled => "Отменена",
        Completed => "Завершена",
        _ => status
    };

    public static string CssClass(string status) => status switch
    {
        Pending => "is-pending",
        Confirmed => "is-confirmed",
        Cancelled => "is-cancelled",
        Completed => "is-completed",
        _ => ""
    };
}

/// <summary>Категории портфолио. Ключи хранятся в БД, заголовки — в интерфейсе.</summary>
public static class WorkCategories
{
    public const string Manicure = "Manicure";
    public const string Pedicure = "Pedicure";
    public const string Lashes = "Lashes";
    public const string Brows = "Brows";
    public const string Hair = "Hair";
    public const string Tattoo = "Tattoo";

    public static readonly (string Key, string Title)[] All =
    {
        (Manicure, "Маникюр"),
        (Pedicure, "Педикюр"),
        (Lashes, "Ресницы"),
        (Brows, "Брови"),
        (Hair, "Волосы"),
        (Tattoo, "Тату")
    };

    public static string Title(string key) =>
        All.FirstOrDefault(c => c.Key == key).Title ?? key;

    public static bool IsKnown(string key) => All.Any(c => c.Key == key);
}

public static class ReminderTypes
{
    public const string DayBefore = "DayBefore";
}
