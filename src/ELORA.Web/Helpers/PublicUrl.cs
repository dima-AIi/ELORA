namespace ELORA.Web.Helpers;

/// <summary>
/// Единый внешний адрес сайта для ссылок, которые уходят наружу: canonical, Open Graph,
/// карта сайта, robots.txt.
/// </summary>
/// <remarks>
/// Источник правды — настройка <c>Site:PublicUrl</c>: её задаёт владелец, и она же нужна
/// ботам для ссылок. Если настройка пустая или оставлена локальной (а так бывает и на
/// хостинге, если её забыли поменять), берём адрес из самого запроса. За прокси хостинга
/// он верный: <c>UseForwardedHeaders</c> уже подменил схему и хост на настоящие, поэтому
/// HTTPS-ссылка получается https, а не http.
/// </remarks>
public static class PublicUrl
{
    public static string Resolve(HttpContext context, IConfiguration configuration)
    {
        var configured = configuration["Site:PublicUrl"]?.Trim().TrimEnd('/');

        if (!string.IsNullOrWhiteSpace(configured) && !IsLocal(configured))
            return configured;

        var request = context.Request;
        return $"{request.Scheme}://{request.Host}".TrimEnd('/');
    }

    /// <summary>Локальный адрес: снаружи по нему сайт недоступен, для ссылок он бесполезен.</summary>
    private static bool IsLocal(string url) =>
        url.Contains("localhost", StringComparison.OrdinalIgnoreCase) ||
        url.Contains("127.0.0.1", StringComparison.OrdinalIgnoreCase);
}
