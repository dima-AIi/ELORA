using System.Globalization;

namespace ELORA.Web.Helpers;

public static class Money
{
    private static readonly CultureInfo Ru = CultureInfo.GetCultureInfo("ru-RU");

    /// <summary>Форматирует цену как «1 500 ₽».</summary>
    public static string Format(decimal price) =>
        price.ToString("N0", Ru).Replace('\u00A0', ' ') + " ₽";

    public static string FormatFrom(decimal price) => "от " + Format(price);

    public static string FormatShort(decimal price) =>
        price.ToString("N0", Ru).Replace('\u00A0', ' ');

    private static readonly string[] Months =
    {
        "января", "февраля", "марта", "апреля", "мая", "июня",
        "июля", "августа", "сентября", "октября", "ноября", "декабря"
    };

    private static readonly string[] Weekdays =
    {
        "воскресенье", "понедельник", "вторник", "среда", "четверг", "пятница", "суббота"
    };

    private static readonly string[] WeekdaysShort =
    {
        "вс", "пн", "вт", "ср", "чт", "пт", "сб"
    };

    public static string Month(int month) => Months[Math.Clamp(month - 1, 0, 11)];
    public static string Weekday(DayOfWeek day) => Weekdays[(int)day];
    public static string WeekdayShort(DayOfWeek day) => WeekdaysShort[(int)day];

    public static string Duration(int minutes) =>
        minutes < 60 ? $"{minutes} мин" : minutes % 60 == 0 ? $"{minutes / 60} ч" : $"{minutes / 60} ч {minutes % 60} мин";

    public static string DateLong(DateTime date) =>
        $"{date.Day} {Months[date.Month - 1]}";

    public static string DateWithWeekday(DateTime date) =>
        $"{date:dd.MM.yyyy}, {Weekdays[(int)date.DayOfWeek]}";

    public static string TimeRange(DateTime start, DateTime end) =>
        $"{start:HH:mm} – {end:HH:mm}";

    /// <summary>
    /// Русская форма множественного числа: «1 неделю», «2 недели», «5 недель».
    /// Нужна там, где рядом с числом стоит существительное.
    /// </summary>
    public static string Plural(int count, string one, string few, string many)
    {
        var mod100 = Math.Abs(count) % 100;
        if (mod100 is >= 11 and <= 14) return many;

        return (mod100 % 10) switch
        {
            1 => one,
            2 or 3 or 4 => few,
            _ => many
        };
    }

    public static string Weeks(int count) => Plural(count, "неделю", "недели", "недель");
    public static string Days(int count) => Plural(count, "день", "дня", "дней");
    public static string Visits(int count) => Plural(count, "визит", "визита", "визитов");
}
