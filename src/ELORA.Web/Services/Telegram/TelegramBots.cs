namespace ELORA.Web.Services.Telegram;

/// <summary>
/// Оба бота проекта. Держим их вместе, потому что токены читаются из конфигурации один раз
/// при старте (после смены токена нужен перезапуск), а логика обращается то к одному, то к другому.
/// </summary>
public sealed class TelegramBots
{
    public TelegramBots(
        IConfiguration configuration,
        IHttpClientFactory httpFactory,
        ILoggerFactory loggerFactory)
    {
        Client = new TelegramBot(
            TelegramRole.Client,
            configuration["Telegram:BotToken"],
            httpFactory,
            loggerFactory.CreateLogger<TelegramBot>());

        Admin = new TelegramBot(
            TelegramRole.Admin,
            configuration["Telegram:AdminBotToken"],
            httpFactory,
            loggerFactory.CreateLogger<TelegramBot>());
    }

    /// <summary>Публичный бот: на нём клиенты, его имя печатается на сайте и в визитках.</summary>
    public TelegramBot Client { get; }

    /// <summary>Приватный бот администратора: уведомления, сводки, кнопка панели.</summary>
    public TelegramBot Admin { get; }

    public TelegramBot For(TelegramRole role) => role == TelegramRole.Admin ? Admin : Client;

    /// <summary>
    /// Подставляет токен, сохранённый в настройках сайта. Токен из переменных окружения
    /// главнее: он задаётся при выкладке, и перебивать его базой нельзя — иначе на хостинге
    /// с настроенными переменными поле в админке молча ничего не делало бы.
    /// </summary>
    public bool ApplyStoredToken(TelegramRole role, string? token)
    {
        var bot = For(role);
        if (bot.TokenFromEnvironment) return false;
        return bot.SetToken(token);
    }

    public IEnumerable<TelegramBot> All
    {
        get
        {
            yield return Client;
            yield return Admin;
        }
    }
}
