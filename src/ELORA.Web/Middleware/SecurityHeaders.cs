namespace ELORA.Web.Middleware;

/// <summary>
/// Заголовки безопасности для всех ответов сайта.
/// </summary>
/// <remarks>
/// <para>
/// Ставится до отдачи статики, чтобы заголовки получали и картинки со стилями, а не
/// только страницы: подмена типа файла опасна ровно так же.
/// </para>
/// <para>
/// <b>Почему на /admin нет X-Frame-Options.</b> У этого заголовка нет списка исключений:
/// значение либо <c>DENY</c>, либо <c>SAMEORIGIN</c>, и оба запрещают показ страницы
/// внутри чужого сайта. Админ-панель открывается как мини-приложение Telegram, а
/// Telegram Web (браузерная версия) показывает мини-приложение внутри iframe — с этим
/// заголовком панель в нём просто не откроется. Рамку для панели разрешает
/// <c>frame-ancestors</c>: он умеет перечислять домены, а современные браузеры при
/// наличии <c>frame-ancestors</c> игнорируют <c>X-Frame-Options</c>.
/// </para>
/// <para>
/// Отдельная тонкость: <c>SAMEORIGIN</c> на страницу входа ставит не только этот код —
/// его добавляет сам механизм защиты форм (Antiforgery), когда страница отдаёт форму
/// с токеном. Происходит это уже во время отрисовки, поэтому снять заголовок можно
/// только в <c>OnStarting</c>, перед отправкой заголовков клиенту.
/// </para>
/// </remarks>
public static class SecurityHeaders
{
    /// <summary>
    /// Кто имеет право показывать наши страницы в рамке. Мини-приложение Telegram
    /// открывается с доменов web.telegram.org (у него несколько зеркал), поэтому
    /// перечислены и они, и весь домен telegram.org.
    /// </summary>
    private const string TelegramFrameAncestors =
        "https://web.telegram.org https://webk.telegram.org https://webz.telegram.org https://*.telegram.org";

    /// <summary>
    /// Политика содержимого. Собрана из того, что сайт реально загружает.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>'unsafe-inline'</c> в <c>script-src</c> и <c>style-src</c> — сознательная
    /// уступка, а не недосмотр. На страницах есть небольшие встроенные скрипты
    /// (полноэкранный режим Telegram, выбор слота при записи) и inline-стили в разметке.
    /// Чтобы убрать <c>'unsafe-inline'</c> из <c>script-src</c>, каждому встроенному
    /// скрипту нужен одноразовый <c>nonce</c>, который Razor должен проставить в тег, —
    /// это отдельная задача. Пока <c>'unsafe-inline'</c> всё равно полезен: он
    /// ограничивает источники внешних скриптов, а <c>object-src</c> и <c>base-uri</c>
    /// закрыты полностью, то есть вставка чужого плагина или подмена базового адреса
    /// уже невозможна.
    /// </para>
    /// <para>
    /// <c>telegram.org</c> в <c>script-src</c> нужен странице входа в панель:
    /// <c>telegram-app.js</c> подгружает оттуда официальный скрипт мини-приложения.
    /// <c>api.telegram.org</c> в <c>connect-src</c> — на случай обращений самого скрипта.
    /// </para>
    /// </remarks>
    private static readonly string ContentSecurityPolicy = string.Join("; ", new[]
    {
        "default-src 'self'",
        "base-uri 'self'",
        "object-src 'none'",
        "form-action 'self'",
        "script-src 'self' 'unsafe-inline' https://telegram.org",
        "style-src 'self' 'unsafe-inline'",
        "img-src 'self' data:",
        "font-src 'self'",
        "connect-src 'self' https://api.telegram.org",
        $"frame-ancestors 'self' {TelegramFrameAncestors}"
    });

    /// <summary>
    /// Что странице запрещено спрашивать у устройства. Ни карта, ни запись звука,
    /// ни оплата через браузерный API на сайте не используются — значит, и разрешений
    /// на них быть не должно: если в разметку когда-нибудь попадёт чужой скрипт,
    /// он не сможет попросить доступ к микрофону или камере.
    /// </summary>
    private static readonly string PermissionsPolicy = string.Join(", ", new[]
    {
        "geolocation=()", "microphone=()", "camera=()", "payment=()", "usb=()",
        "magnetometer=()", "gyroscope=()", "accelerometer=()"
    });

    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app)
    {
        return app.Use(async (context, next) =>
        {
            var headers = context.Response.Headers;

            // Приложение не сообщает наружу, на чём оно работает: точная версия платформы
            // помогает подбирать уязвимости. Серверный заголовок IIS добавляет сам, и
            // убрать его можно только в web.config хостинга — там править не будем.
            headers.Remove("Server");
            headers.Remove("X-Powered-By");

            headers["X-Content-Type-Options"] = "nosniff";

            // Полный адрес страницы уходит чужому сайту только при переходе по внешней
            // ссылке, и то без пути: Referer с параметрами записи — это лишнее.
            headers["Referrer-Policy"] = "strict-origin-when-cross-origin";

            headers["Permissions-Policy"] = PermissionsPolicy;
            headers["Content-Security-Policy"] = ContentSecurityPolicy;

            if (context.Request.Path.StartsWithSegments("/admin"))
            {
                // Снимаем заголовок в последний момент: к этому времени его уже успел
                // поставить Antiforgery вместе с формой входа, а отправить клиенту он
                // ещё не успел. Рамку для панели ограничивает frame-ancestors выше.
                context.Response.OnStarting(() =>
                {
                    context.Response.Headers.Remove("X-Frame-Options");
                    return Task.CompletedTask;
                });
            }
            else
            {
                headers["X-Frame-Options"] = "SAMEORIGIN";
            }

            await next();
        });
    }
}
