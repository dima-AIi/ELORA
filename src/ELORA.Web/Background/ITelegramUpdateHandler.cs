using ELORA.Web.Services.Telegram;

namespace ELORA.Web.Background;

/// <summary>
/// Обработчик событий одного бота. Реализаций две — клиентская и админская, — и именно
/// поэтому ботов два: список команд в Telegram задаётся на бота, а не на пользователя.
/// </summary>
public interface ITelegramUpdateHandler
{
    /// <summary>Команды в меню этого бота. Публикуются при старте опроса.</summary>
    IReadOnlyList<(string Command, string Description)> Commands { get; }

    Task HandleMessageAsync(
        TelegramBot bot, long chatId, string text, CancellationToken cancellationToken);

    /// <summary>
    /// Человек нажал «отправить мой номер» и поделился телефоном.
    /// </summary>
    /// <remarks>
    /// Приходит <b>не текстом</b>: у такого сообщения есть только поле <c>contact</c>,
    /// и обычный обработчик сообщений его не увидит. Это единственный способ привязать чат
    /// к карточке клиента, когда он открыл бота сам, без ссылки с кодом, — а без привязки
    /// бот не может написать ему первым и подтверждение не доходит.
    /// </remarks>
    Task HandleContactAsync(
        TelegramBot bot, long chatId, string phone, string name, CancellationToken cancellationToken);

    /// <summary>
    /// Нажатие inline-кнопки. <paramref name="messageId"/> нужен, чтобы переписать карточку
    /// на месте, а не присылать новую.
    /// </summary>
    Task HandleCallbackAsync(
        TelegramBot bot, string callbackId, long chatId, int messageId, string data,
        CancellationToken cancellationToken);
}
