using System.Text;
using System.Text.Json;

namespace ELORA.Web.Services.Telegram;

/// <summary>
/// Транспорт одного бота: токен, HTTP и вызовы Bot API. Ничего не знает про записи и настройки —
/// только отправляет и принимает. Сценарии живут в <see cref="TelegramService"/>.
/// </summary>
/// <remarks>
/// Два правила, добытых на живом боте, нарушать нельзя:
/// <list type="number">
/// <item>Адрес запроса — только абсолютный. Токен содержит двоеточие, и относительный путь
/// вида <c>bot123:AA/getMe</c> .NET разбирает как URI со схемой <c>bot123</c>, после чего
/// выбрасывает <c>NotSupportedException</c>.</item>
/// <item>У HttpClient не должно быть общего таймаута: длинный опрос держит соединение столько,
/// сколько просит Telegram, и общий лимит обрывает запрос раньше ответа — бот перестаёт
/// получать команды. Таймаут задаётся точечно на вызов.</item>
/// </list>
/// </remarks>
public sealed class TelegramBot
{
    private const string HttpClientName = "telegram";
    private static readonly TimeSpan DefaultRequestTimeout = TimeSpan.FromSeconds(20);

    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger _logger;
    private string? _token;

    public TelegramBot(TelegramRole role, string? token, IHttpClientFactory httpFactory, ILogger logger)
    {
        Role = role;
        _token = Clean(token);
        TokenFromEnvironment = _token is not null;
        _httpFactory = httpFactory;
        _logger = logger;
    }

    public TelegramRole Role { get; }

    public bool IsConfigured => _token is not null;

    /// <summary>Токен пришёл из переменных окружения (или User Secrets), а не из настроек сайта.</summary>
    public bool TokenFromEnvironment { get; private set; }

    /// <summary>
    /// Подставляет токен на ходу. Нужен владельцу: токен, вписанный в админке, работает
    /// без перезапуска сайта и без доступа к панели хостинга — а именно отсутствие токена
    /// на хостинге и было причиной молчащих уведомлений.
    /// </summary>
    public bool SetToken(string? token)
    {
        var clean = Clean(token);
        if (string.Equals(clean, _token, StringComparison.Ordinal)) return false;

        _token = clean;
        // Имя бота принадлежит токену: у другого токена другой бот.
        Username = null;
        TokenFromEnvironment = false;
        _logger.LogInformation("{Bot}: токен обновлён на ходу", Describe());
        return true;
    }

    private static string? Clean(string? token) =>
        string.IsNullOrWhiteSpace(token) ? null : token.Trim();

    /// <summary>Имя бота без собаки. Заполняется после первого <c>getMe</c>.</summary>
    public string? Username { get; private set; }

    public string Describe() => Role == TelegramRole.Admin ? "админский бот" : "бот студии";

    /// <summary>Ссылка для привязки чата. Без имени бота код привязки бесполезен, поэтому даём заглушку.</summary>
    public string BuildStartLink(string code) =>
        string.IsNullOrWhiteSpace(Username)
            ? $"https://t.me/?start={code}"
            : $"https://t.me/{Username}?start={code}";

    public string? BuildChatLink() =>
        string.IsNullOrWhiteSpace(Username) ? null : $"https://t.me/{Username}";

    public async Task<string?> GetBotUsernameAsync(CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(Username)) return Username;
        if (!IsConfigured) return null;

        var me = await CallAsync("getMe", null, cancellationToken);
        if (me is null) return null;

        if (me.Value.TryGetProperty("result", out var result) &&
            result.TryGetProperty("username", out var username))
        {
            Username = username.GetString();
        }

        return Username;
    }

    // ---------------- Отправка ----------------

    public async Task<bool> SendMessageAsync(
        string chatId,
        string text,
        IReadOnlyList<IReadOnlyList<TelegramButton>>? keyboard = null,
        CancellationToken cancellationToken = default)
    {
        if (!IsConfigured || string.IsNullOrWhiteSpace(chatId)) return false;

        var response = await CallAsync("sendMessage",
            MessagePayload(chatId, text, keyboard, null), cancellationToken);

        var ok = response is not null && response.Value.TryGetProperty("ok", out var flag) && flag.GetBoolean();
        if (!ok) _logger.LogWarning("{Bot}: sendMessage не удался для chat {ChatId}", Describe(), chatId);
        return ok;
    }

    /// <summary>
    /// Отправляет сообщение и возвращает его <c>message_id</c>. Нужно пошаговым мастерам:
    /// они правят одно и то же сообщение на каждом шаге, а не плодят новые.
    /// </summary>
    public async Task<int?> SendMessageReturningIdAsync(
        string chatId,
        string text,
        IReadOnlyList<IReadOnlyList<TelegramButton>>? keyboard = null,
        CancellationToken cancellationToken = default)
    {
        if (!IsConfigured || string.IsNullOrWhiteSpace(chatId)) return null;

        var response = await CallAsync("sendMessage",
            MessagePayload(chatId, text, keyboard, null), cancellationToken);

        if (response is null) return null;
        if (!response.Value.TryGetProperty("ok", out var ok) || !ok.GetBoolean()) return null;

        return response.Value.TryGetProperty("result", out var result) &&
               result.TryGetProperty("message_id", out var messageId)
            ? messageId.GetInt32()
            : null;
    }

    /// <summary>
    /// Отправка с диагностикой: <c>null</c> при успехе, иначе причина отказа словами.
    /// Нужна кнопке «Отправить тестовое сообщение»: «не удалось» без причины не помогает,
    /// а самая частая ситуация — человек ещё не нажал Start (Telegram отвечает chat not found).
    /// </summary>
    public async Task<string?> SendMessageExAsync(
        string chatId,
        string text,
        IReadOnlyList<IReadOnlyList<TelegramButton>>? keyboard = null,
        CancellationToken cancellationToken = default)
    {
        if (!IsConfigured) return "токен бота не задан";
        if (string.IsNullOrWhiteSpace(chatId)) return "не указан Chat ID администратора";

        var response = await CallAsync("sendMessage", MessagePayload(chatId, text, keyboard, null), cancellationToken);

        if (response is null) return "Telegram не ответил — проверьте интернет и токен";
        if (response.Value.TryGetProperty("ok", out var ok) && ok.GetBoolean()) return null;

        var description = response.Value.TryGetProperty("description", out var d)
            ? d.GetString() ?? "неизвестная ошибка"
            : "неизвестная ошибка";

        if (description.Contains("chat not found", StringComparison.OrdinalIgnoreCase))
        {
            var link = BuildChatLink() ?? "бота";
            return $"Telegram не знает этот чат. Откройте {link} и нажмите Start, после этого повторите проверку";
        }

        if (description.Contains("Unauthorized", StringComparison.OrdinalIgnoreCase))
            return "токен неверный — скопируйте его заново из @BotFather";

        if (description.Contains("blocked", StringComparison.OrdinalIgnoreCase))
            return "бот заблокирован получателем — разблокируйте его в Telegram";

        return description;
    }

    /// <summary>
    /// Переписывает уже отправленное сообщение. Так работает вся работа с записью: карточка
    /// остаётся одна и меняется по шагам, а не плодит новые сообщения.
    /// </summary>
    public async Task<bool> EditMessageTextAsync(
        string chatId,
        int messageId,
        string text,
        IReadOnlyList<IReadOnlyList<TelegramButton>>? keyboard = null,
        bool clearKeyboard = false,
        CancellationToken cancellationToken = default)
    {
        if (!IsConfigured) return false;

        var payload = MessagePayload(chatId, text, keyboard, null);
        payload["message_id"] = messageId;
        if (clearKeyboard && keyboard is null) payload["reply_markup"] = new { inline_keyboard = Array.Empty<object>() };

        var response = await CallAsync("editMessageText", payload, cancellationToken);
        var ok = response is not null && response.Value.TryGetProperty("ok", out var flag) && flag.GetBoolean();
        if (!ok) _logger.LogWarning("{Bot}: editMessageText не удался для chat {ChatId}", Describe(), chatId);
        return ok;
    }

    public async Task<bool> AnswerCallbackAsync(
        string callbackId, string? text = null, CancellationToken cancellationToken = default)
    {
        if (!IsConfigured || string.IsNullOrWhiteSpace(callbackId)) return false;

        var payload = new Dictionary<string, object?> { ["callback_query_id"] = callbackId };
        if (!string.IsNullOrWhiteSpace(text)) payload["text"] = text;

        var response = await CallAsync("answerCallbackQuery", payload, cancellationToken);
        return response is not null;
    }

    /// <summary>Подсказка над клавиатурой («печатает…»), пока идёт обработка нажатия.</summary>
    public async Task<bool> SendChatActionAsync(string chatId, string action = "typing", CancellationToken cancellationToken = default)
    {
        if (!IsConfigured) return false;
        var response = await CallAsync("sendChatAction",
            new Dictionary<string, object?> { ["chat_id"] = chatId, ["action"] = action }, cancellationToken);
        return response is not null;
    }

    public async Task<JsonElement?> GetUpdatesAsync(
        long offset, int timeoutSeconds, CancellationToken cancellationToken = default)
    {
        if (!IsConfigured) return null;

        // Таймаут с запасом к параметру timeout: сервер держит соединение до timeoutSeconds
        // и отвечает пустым result, если сообщений не было. Наш лимит нужен только чтобы
        // не висеть вечно на мёртвом сокете.
        return await CallAsync("getUpdates", new Dictionary<string, object?>
        {
            ["offset"] = offset,
            ["timeout"] = timeoutSeconds,
            ["allowed_updates"] = new[] { "message", "callback_query" }
        }, cancellationToken, TimeSpan.FromSeconds(timeoutSeconds + 15));
    }

    /// <summary>Список команд в меню бота. В Telegram он один на бота, а не на пользователя —
    /// именно поэтому у клиентов и админа разные боты.</summary>
    public async Task<bool> SetMyCommandsAsync(
        IReadOnlyList<(string Command, string Description)> commands, CancellationToken cancellationToken = default)
    {
        if (!IsConfigured) return false;

        var payload = new Dictionary<string, object?>
        {
            ["commands"] = commands.Select(c => new { command = c.Command, description = c.Description }).ToArray()
        };

        var response = await CallAsync("setMyCommands", payload, cancellationToken);
        return response is not null && response.Value.TryGetProperty("ok", out var ok) && ok.GetBoolean();
    }

    /// <summary>
    /// Кнопка меню бота (слева от поля ввода) — открывает переданный адрес как Mini App.
    /// Нужна админскому боту: панель под рукой, одним нажатием. Telegram открывает
    /// мини-приложения только по HTTPS, поэтому вызывать её на localhost бессмысленно.
    /// </summary>
    public async Task<bool> SetChatMenuButtonAsync(
        string url, string text = "Панель", CancellationToken cancellationToken = default)
    {
        if (!IsConfigured || string.IsNullOrWhiteSpace(url)) return false;

        var payload = new Dictionary<string, object?>
        {
            ["menu_button"] = new { type = "web_app", text, web_app = new { url } }
        };

        var response = await CallAsync("setChatMenuButton", payload, cancellationToken);
        return response is not null && response.Value.TryGetProperty("ok", out var ok) && ok.GetBoolean();
    }

    /// <summary>
    /// Возвращает боту обычную кнопку меню — список команд вместо адреса сайта.
    /// </summary>
    /// <remarks>
    /// Кнопка-сайт в клиентском боте уводит человека из чата, так и не нажав Start,
    /// а без Start Telegram не даёт боту написать первым: ни подтверждения записи,
    /// ни напоминания клиент не получит. Снимается тем же вызовом, что и ставится,
    /// только с типом <c>default</c>.
    /// </remarks>
    public async Task<bool> ResetChatMenuButtonAsync(CancellationToken cancellationToken = default)
    {
        if (!IsConfigured) return false;

        var payload = new Dictionary<string, object?>
        {
            ["menu_button"] = new { type = "default" }
        };

        var response = await CallAsync("setChatMenuButton", payload, cancellationToken);
        return response is not null && response.Value.TryGetProperty("ok", out var ok) && ok.GetBoolean();
    }

    /// <summary>
    /// Проверяет <c>initData</c>, присланный страницей Mini App. Подпись сверяется токеном
    /// именно этого бота — поэтому метод живёт здесь, где токен, и наружу его не отдаёт.
    /// </summary>
    public bool TryValidateWebAppData(
        string? initData, TimeSpan maxAge, out TelegramWebAppUser? user, out string? error) =>
        TelegramWebApp.TryValidate(initData, _token, maxAge, out user, out error);

    private static Dictionary<string, object?> MessagePayload(
        string chatId, string text, IReadOnlyList<IReadOnlyList<TelegramButton>>? keyboard, int? messageId)
    {
        var payload = new Dictionary<string, object?>
        {
            ["chat_id"] = chatId,
            ["text"] = text,
            ["parse_mode"] = "HTML",
            ["disable_web_page_preview"] = true
        };

        if (messageId is not null) payload["message_id"] = messageId;

        if (keyboard is { Count: > 0 })
        {
            // Кнопка «отправить номер» живёт в обычной клавиатуре, а не в inline: только она
            // позволяет Telegram подставить номер телефона владельца аккаунта. Inline-кнопки
            // так не умеют, поэтому вид клавиатуры выбирается по типу первой кнопки.
            payload["reply_markup"] = keyboard.Any(row => row.Any(button => button.RequestContact))
                ? ReplyKeyboardJson(keyboard)
                : new
                {
                    inline_keyboard = keyboard.Select(row => row.Select(ButtonJson).ToArray()).ToArray()
                };
        }

        return payload;
    }

    /// <summary>
    /// Форма обычной (не inline) клавиатуры с кнопкой «отправить мой номер».
    /// </summary>
    /// <remarks>
    /// <c>internal</c> по той же причине, что и <see cref="ButtonJson"/>: в живом чате такую
    /// клавиатуру видно только в Telegram, а форму, которая уйдёт в Bot API, надо проверить
    /// до отправки — Telegram молча игнорирует неверно названное поле.
    /// </remarks>
    internal static object ReplyKeyboardJson(IReadOnlyList<IReadOnlyList<TelegramButton>> keyboard) => new
    {
        keyboard = keyboard
            .Select(row => row
                .Select(button => (object)new
                {
                    text = button.Text,
                    request_contact = button.RequestContact
                })
                .ToArray())
            .ToArray(),
        resize_keyboard = true,
        // Клавиатура убирается сразу после нажатия — иначе она перекрывает список записей
        // и остаётся висеть до следующего сообщения.
        one_time_keyboard = true
    };

    /// <summary>
    /// JSON одной кнопки. <c>internal</c>, а не <c>private</c>, ровно ради стенда разработки:
    /// форму кнопки Mini App иначе не проверить — Telegram открывает её только по HTTPS.
    /// </summary>
    internal static object ButtonJson(TelegramButton button)
    {
        if (!string.IsNullOrWhiteSpace(button.WebAppUrl))
            return new { text = button.Text, web_app = new { url = button.WebAppUrl } };

        return string.IsNullOrWhiteSpace(button.Url)
            ? new { text = button.Text, callback_data = button.CallbackData ?? "" }
            : new { text = button.Text, url = button.Url };
    }

    private async Task<JsonElement?> CallAsync(
        string method, Dictionary<string, object?>? payload, CancellationToken cancellationToken,
        TimeSpan? requestTimeout = null)
    {
        if (!IsConfigured) return null;

        // Короткие вызовы ограничиваем сами, длинный опрос — нет: он живёт ровно столько,
        // сколько держит соединение Telegram. Верхняя граница опроса передана вызывающим.
        var effectiveTimeout = requestTimeout ?? DefaultRequestTimeout;
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(effectiveTimeout);

        try
        {
            var http = _httpFactory.CreateClient(HttpClientName);
            var url = $"https://api.telegram.org/bot{_token}/{method}";
            HttpResponseMessage response;

            if (payload is null)
            {
                response = await http.GetAsync(url, timeoutCts.Token);
            }
            else
            {
                var json = JsonSerializer.Serialize(payload);
                using var content = new StringContent(json, Encoding.UTF8, "application/json");
                response = await http.PostAsync(url, content, timeoutCts.Token);
            }

            var body = await response.Content.ReadAsStringAsync(timeoutCts.Token);
            if (string.IsNullOrWhiteSpace(body)) return null;

            using var document = JsonDocument.Parse(body);
            return document.RootElement.Clone();
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Наш собственный таймаут, а не остановка приложения. Для опроса это норма:
            // следующая итерация просто начнёт заново.
            _logger.LogDebug("{Bot}: {Method} превысил таймаут {Seconds} с",
                Describe(), method, effectiveTimeout.TotalSeconds);
            return null;
        }
        catch (Exception ex)
        {
            // Ловим всё: уведомление — вспомогательный канал и не должно ронять ни фоновую
            // службу, ни сайт. Следующий цикл попробует снова.
            _logger.LogWarning(ex, "{Bot}: {Method} недоступен", Describe(), method);
            return null;
        }
    }
}
