using System.Globalization;
using System.Reflection;
using System.Security;
using System.Text;
using ELORA.Web.Helpers;

namespace ELORA.Web.Endpoints;

/// <summary>
/// Служебные адреса для поисковых систем: <c>/robots.txt</c> и <c>/sitemap.xml</c>.
/// </summary>
/// <remarks>
/// Отдаём их из приложения, а не файлами в wwwroot. Файл пришлось бы править руками
/// при каждом переезде: внутри карты сайта лежат абсолютные адреса, и после смены
/// домена они молча указывали бы на старый. Здесь адрес берётся из той же настройки,
/// что и ссылки для Telegram-бота, — одна точка правды на весь проект.
/// </remarks>
public static class SeoEndpoints
{
    /// <summary>Страницы, открытые посетителю и разрешённые к индексации.</summary>
    private static readonly string[] PublicPages =
    {
        "/", "/services", "/works", "/price", "/about",
        "/contacts", "/faq", "/rules", "/privacy", "/booking"
    };

    public static void MapSeo(this WebApplication app)
    {
        app.MapGet("/robots.txt", (HttpContext context, IConfiguration configuration) =>
        {
            var site = PublicUrl.Resolve(context, configuration);

            var text = string.Join('\n', new[]
            {
                "User-agent: *",
                "Allow: /",
                // Служебное и личное закрываем: панель управления и страница чужих
                // записей в поиске не нужны, а обход создаёт лишнюю нагрузку.
                "Disallow: /admin",
                "Disallow: /api/",
                "Disallow: /my",
                "Disallow: /error",
                "Disallow: /healthz",
                "",
                $"Sitemap: {site}/sitemap.xml",
                ""
            });

            return Results.Text(text, "text/plain; charset=utf-8");
        });

        app.MapGet("/sitemap.xml", (HttpContext context, IConfiguration configuration) =>
        {
            var site = PublicUrl.Resolve(context, configuration);
            var lastMod = LastModified();

            var xml = new StringBuilder();
            xml.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n");
            xml.Append("<urlset xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\">\n");

            foreach (var page in PublicPages)
            {
                xml.Append("  <url>\n");
                xml.Append("    <loc>").Append(SecurityElement.Escape(site + page)).Append("</loc>\n");
                xml.Append("    <lastmod>").Append(lastMod).Append("</lastmod>\n");
                xml.Append("  </url>\n");
            }

            xml.Append("</urlset>\n");

            return Results.Text(xml.ToString(), "application/xml; charset=utf-8");
        });
    }

    /// <summary>
    /// Дата последнего изменения страниц в формате W3C (<c>yyyy-MM-dd</c>).
    /// </summary>
    /// <remarks>
    /// Берём время сборки, а не текущую дату: «сейчас» на каждом запросе означало бы
    /// «всё меняется постоянно», и поисковик перестал бы верить этой дате совсем.
    /// Время сборки честно отвечает на вопрос «когда сайт обновляли в последний раз».
    /// </remarks>
    private static string LastModified()
    {
        try
        {
            var location = Assembly.GetEntryAssembly()?.Location;
            if (!string.IsNullOrEmpty(location) && File.Exists(location))
                return File.GetLastWriteTimeUtc(location).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }
        catch (IOException)
        {
            // Файл занят или недоступен — дата не тот повод, чтобы ронять карту сайта.
        }

        return DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }
}
