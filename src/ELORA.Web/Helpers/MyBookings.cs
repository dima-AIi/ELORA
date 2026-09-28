namespace ELORA.Web.Helpers;

/// <summary>
/// «Мои записи» — память браузера о записях клиента. Регистрация для этого не нужна.
/// </summary>
/// <remarks>
/// При создании записи сервер и так выдаёт секретный токен управления — по нему открывается
/// страница <c>/manage/{token}</c>. Держим список этих токенов в cookie, и клиент, вернувшись
/// на сайт с того же браузера, сразу видит свои записи, а не идёт за ними в Telegram.
/// <para>
/// В cookie лежат <b>только токены</b>: ни имени, ни телефона там нет, и сами по себе, без базы,
/// они бесполезны. Кука помечена <c>HttpOnly</c> (скриптам со страницы недоступна) и
/// <c>SameSite=Lax</c> — она не уезжает на сторонние сайты, но работает при обычном переходе.
/// </para>
/// <para>
/// Обратная сторона: на чужом или общем компьютере клиент увидит свои записи. Поэтому на странице
/// «Мои записи» есть кнопка «Забыть этот браузер» — она стирает куку.
/// </para>
/// </remarks>
public static class MyBookings
{
    public const string CookieName = "elora_my_bookings";

    /// <summary>Сколько токенов помним. Двадцать записей — это больше, чем у нас бывает у человека.</summary>
    private const int MaxTokens = 20;

    private static readonly TimeSpan Lifetime = TimeSpan.FromDays(365);

    /// <summary>Токены управления из cookie — в порядке «последняя запись первой».</summary>
    public static IReadOnlyList<string> Read(HttpRequest request)
    {
        if (!request.Cookies.TryGetValue(CookieName, out var raw) || string.IsNullOrWhiteSpace(raw))
            return Array.Empty<string>();

        return raw
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(IsToken)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Добавляет токен в память браузера. Повторный вызов с тем же токеном куку не трогает —
    /// иначе срок жизни продлевался бы при каждом открытии страницы записи.
    /// </summary>
    public static void Remember(HttpRequest request, HttpResponse response, string? token)
    {
        if (token is null || !IsToken(token)) return;

        var tokens = Read(request).ToList();
        if (tokens.Contains(token, StringComparer.OrdinalIgnoreCase)) return;

        tokens.Insert(0, token);
        if (tokens.Count > MaxTokens)
            tokens.RemoveRange(MaxTokens, tokens.Count - MaxTokens);

        response.Cookies.Append(CookieName, string.Join(',', tokens), CookieOptionsFor(request));
    }

    /// <summary>Забывает все записи в этом браузере.</summary>
    public static void Forget(HttpRequest request, HttpResponse response)
    {
        // Тот же набор атрибутов, что при записи: иначе браузер посчитает это другой кукой
        // и старая останется на месте.
        response.Cookies.Delete(CookieName, CookieOptionsFor(request));
    }

    private static CookieOptions CookieOptionsFor(HttpRequest request) => new()
    {
        HttpOnly = true,
        // На локальном стенде сайт открывается по http — с флагом Secure браузер куку бы не сохранил.
        Secure = request.IsHttps,
        SameSite = SameSiteMode.Lax,
        Path = "/",
        MaxAge = Lifetime,
        IsEssential = true
    };

    /// <summary>
    /// Токен управления — 32 шестнадцатеричных символа (<c>BookingService.GenerateToken</c>).
    /// Проверяем строго: значение приходит из cookie, то есть его может подсунуть кто угодно,
    /// а оно подставляется в запрос к базе.
    /// </summary>
    private static bool IsToken(string value) =>
        value.Length is >= 16 and <= 64 && value.All(char.IsAsciiHexDigit);
}
