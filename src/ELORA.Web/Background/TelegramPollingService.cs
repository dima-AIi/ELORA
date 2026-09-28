using System.Text.Json;
using ELORA.Web.Services;
using ELORA.Web.Services.Telegram;

namespace ELORA.Web.Background;

/// <summary>
/// Long polling одного бота. Служба регистрируется дважды — по экземпляру на клиентского
/// и админского бота.
/// </summary>
/// <remarks>
/// Long polling выбран специально: он не требует публичного webhook-адреса, поэтому одинаково
/// работает и на localhost, и на хостинге.
/// </remarks>
public sealed class TelegramPollingService : BackgroundService
{
    private const int PollTimeoutSeconds = 25;

    private readonly TelegramBot _bot;
    private readonly ITelegramUpdateHandler _handler;
    private readonly TelegramService _telegram;
    private readonly IConfiguration _configuration;
    private readonly ILogger _logger;

    public TelegramPollingService(
        TelegramBot bot,
        ITelegramUpdateHandler handler,
        TelegramService telegram,
        IConfiguration configuration,
        ILogger<TelegramPollingService> logger)
    {
        _bot = bot;
        _handler = handler;
        _telegram = telegram;
        _configuration = configuration;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Токен можно вписать в админке уже после запуска сайта, и тогда бот не должен
        // остаться выключенным до перезапуска: ждём токен, а не выходим навсегда.
        if (!_bot.IsConfigured)
        {
            _logger.LogInformation("{Bot} не настроен — жду токен из настроек сайта", _bot.Describe());
            while (!stoppingToken.IsCancellationRequested && !_bot.IsConfigured)
                await SafeDelay(TimeSpan.FromSeconds(20), stoppingToken);

            if (stoppingToken.IsCancellationRequested) return;
            _logger.LogInformation("{Bot}: токен появился, начинаю работу", _bot.Describe());
        }

        await SafeDelay(TimeSpan.FromSeconds(6), stoppingToken);

        // Имя бота и список команд — необязательное украшение: если Telegram недоступен,
        // опрос всё равно должен продолжить работу, а не уронить службу (необработанное
        // исключение в BackgroundService по умолчанию останавливает весь хост вместе с сайтом).
        await TryAsync(() => _telegram.EnsureUsernamesAsync(stoppingToken), "получить имя бота");
        await TryAsync(() => _bot.SetMyCommandsAsync(_handler.Commands, stoppingToken), "опубликовать команды");

        // Кнопка меню у админского бота открывает панель как Mini App. Ставим её один раз
        // при старте: дальше она живёт в настройках бота.
        if (_bot.Role == TelegramRole.Admin)
        {
            var site = _configuration["Site:PublicUrl"]?.TrimEnd('/');
            if (site is not null && site.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                await TryAsync(() => _bot.SetChatMenuButtonAsync($"{site}/admin", "Панель", stoppingToken),
                    "поставить кнопку меню");
        }
        else
        {
            // Клиентскому боту кнопка-сайт не нужна: она уводит человека из чата, так и не
            // нажав Start, — а без Start Telegram не даёт боту написать первым, и клиент
            // не получает ни подтверждения, ни напоминания. Возвращаем меню команд:
            // кнопку могли поставить раньше, и она живёт в настройках бота, а не у нас.
            await TryAsync(() => _bot.ResetChatMenuButtonAsync(stoppingToken), "вернуть меню команд");
        }

        _logger.LogInformation("{Bot} готов к работе: @{Username}", _bot.Describe(), _bot.Username ?? "?");

        long offset = 0;
        while (!stoppingToken.IsCancellationRequested)
        {
            if (!_telegram.IsEnabled)
            {
                await SafeDelay(TimeSpan.FromSeconds(15), stoppingToken);
                continue;
            }

            try
            {
                var updates = await _bot.GetUpdatesAsync(offset, PollTimeoutSeconds, stoppingToken);
                if (updates is null)
                {
                    await SafeDelay(TimeSpan.FromSeconds(8), stoppingToken);
                    continue;
                }

                if (!updates.Value.TryGetProperty("result", out var results) ||
                    results.ValueKind != JsonValueKind.Array)
                {
                    // Ответ без result — это ошибка API, и раньше она пропускалась молча.
                    // Именно так выглядит самая частая причина «бот не отвечает на команды»:
                    // на боте выставлен webhook, и Telegram отказывает getUpdates с 409 Conflict.
                    // Без этой строки такое не видно ни в логах, ни в админке.
                    var reason = updates.Value.TryGetProperty("description", out var description)
                        ? description.GetString()
                        : updates.Value.GetRawText();

                    _logger.LogWarning(
                        "{Bot}: команды не принимаются — Telegram ответил «{Reason}»",
                        _bot.Describe(), reason);

                    await SafeDelay(TimeSpan.FromSeconds(8), stoppingToken);
                    continue;
                }

                foreach (var update in results.EnumerateArray())
                {
                    if (update.TryGetProperty("update_id", out var idElement))
                        offset = Math.Max(offset, idElement.GetInt64() + 1);

                    try
                    {
                        await HandleUpdateAsync(update, stoppingToken);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "{Bot}: не удалось обработать событие", _bot.Describe());
                    }
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "{Bot}: ошибка цикла опроса", _bot.Describe());
                await SafeDelay(TimeSpan.FromSeconds(10), stoppingToken);
            }
        }
    }

    private async Task HandleUpdateAsync(JsonElement update, CancellationToken cancellationToken)
    {
        if (update.TryGetProperty("callback_query", out var callback))
        {
            await HandleCallbackAsync(callback, cancellationToken);
            return;
        }

        if (update.TryGetProperty("message", out var message))
            await HandleMessageAsync(message, cancellationToken);
    }

    private async Task HandleMessageAsync(JsonElement message, CancellationToken cancellationToken)
    {
        var chatId = ReadChatId(message);
        if (chatId is null) return;

        // Контакт приходит отдельным полем и без текста: если проверять text первым,
        // номер телефона потеряется и привязка чата не сработает.
        if (message.TryGetProperty("contact", out var contact) &&
            contact.TryGetProperty("phone_number", out var phoneElement) &&
            phoneElement.GetString() is { Length: > 0 } phone)
        {
            await _handler.HandleContactAsync(
                _bot, chatId.Value, phone, ContactName(contact), cancellationToken);
            return;
        }

        if (!message.TryGetProperty("text", out var textElement)) return;
        var text = textElement.GetString();
        if (string.IsNullOrWhiteSpace(text)) return;

        await _handler.HandleMessageAsync(_bot, chatId.Value, text.Trim(), cancellationToken);
    }

    private async Task HandleCallbackAsync(JsonElement callback, CancellationToken cancellationToken)
    {
        var callbackId = callback.TryGetProperty("id", out var idElement) ? idElement.GetString() : null;
        var data = callback.TryGetProperty("data", out var dataElement) ? dataElement.GetString() : null;
        if (string.IsNullOrWhiteSpace(data)) return;

        // Сообщение нужно и для chat_id, и для правки карточки на месте.
        if (!callback.TryGetProperty("message", out var message)) return;

        var chatId = ReadChatId(message);
        var messageId = message.TryGetProperty("message_id", out var messageIdElement)
            ? messageIdElement.GetInt32()
            : 0;

        if (chatId is null || messageId == 0) return;

        await _handler.HandleCallbackAsync(
            _bot, callbackId ?? "", chatId.Value, messageId, data, cancellationToken);
    }

    private static long? ReadChatId(JsonElement container)
    {
        if (!container.TryGetProperty("chat", out var chat)) return null;
        return chat.TryGetProperty("id", out var id) ? id.GetInt64() : null;
    }

    /// <summary>
    /// Имя из присланного контакта. Телефон — главное, но пустая карточка клиента выглядит
    /// в админке как сбой, поэтому подставляем хотя бы имя; точное имя перезапишется
    /// при первой записи с сайта.
    /// </summary>
    private static string ContactName(JsonElement contact)
    {
        var parts = new[] { "first_name", "last_name" }
            .Select(key => contact.TryGetProperty(key, out var value) ? value.GetString() : null)
            .Where(value => !string.IsNullOrWhiteSpace(value));

        var name = string.Join(' ', parts).Trim();
        return string.IsNullOrWhiteSpace(name) ? "Клиент из Telegram" : name;
    }

    private async Task TryAsync(Func<Task> action, string what)
    {
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "{Bot}: не удалось {What} — продолжаю без этого", _bot.Describe(), what);
        }
    }

    private static async Task SafeDelay(TimeSpan delay, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(delay, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // приложение останавливается
        }
    }
}
