namespace ELORA.Web.Services.Telegram;

/// <summary>Какой из двух ботов. Роль передаётся явно — сравнивать токены между собой нельзя.</summary>
public enum TelegramRole
{
    /// <summary>Публичный бот для клиентов: запись, перенос, отмена, напоминания.</summary>
    Client,

    /// <summary>Приватный бот для администратора: уведомления, сводки, панель.</summary>
    Admin
}

/// <summary>
/// Кнопка inline-клавиатуры. Либо действие (<c>CallbackData</c>), либо ссылка (<c>Url</c>),
/// либо Mini App (<c>WebAppUrl</c>) — Telegram не разрешает заполнять несколько полей сразу.
/// </summary>
/// <remarks>
/// Разница между <c>Url</c> и <c>WebAppUrl</c> не косметическая: обычная ссылка открывает
/// браузер, а WebApp-кнопка открывает страницу внутри Telegram, и только там страница получает
/// подписанный <c>initData</c>, по которому работает вход без пароля. Требует HTTPS.
/// </remarks>
public sealed record TelegramButton(
    string Text, string? CallbackData = null, string? Url = null, string? WebAppUrl = null,
    bool RequestContact = false)
{
    public static TelegramButton Callback(string text, string data) => new(text, data, null);

    public static TelegramButton Link(string text, string url) => new(text, null, url);

    public static TelegramButton WebApp(string text, string url) => new(text, null, null, url);

    /// <summary>
    /// Кнопка обычной клавиатуры (не inline): Telegram сам подставит номер телефона владельца
    /// аккаунта. Это единственный способ узнать телефон клиента без ручного ввода — и именно
    /// он нужен, чтобы привязать чат к карточке клиента, когда тот открыл бота без ссылки.
    /// </summary>
    public static TelegramButton Contact(string text) => new(text, null, null, null, true);
}

/// <summary>
/// Имена действий в <c>callback_data</c>. Формат — <c>действие:значение</c>, разбор по первому
/// двоеточию.
/// </summary>
/// <remarks>
/// Telegram ограничивает <c>callback_data</c> 64 байтами, поэтому в кнопку кладём только
/// идентификатор или дату. Всё остальное состояние шага живёт в таблице <c>TelegramStates</c>.
/// </remarks>
public static class TelegramActions
{
    // Навигация в клиентском боте
    public const string MyBookings = "my-list";
    public const string BookStart = "bk-start";

    /// <summary>
    /// Подменю «Сайт»: разделы открываются кнопками-ссылками прямо из чата.
    /// </summary>
    /// <remarks>
    /// Это не кнопка меню бота (та слева от поля ввода): кнопка меню уводит человека из чата,
    /// так и не нажав Start, и тогда бот не может написать ему первым. Ссылка внутри
    /// сообщения открывает браузер, а чат остаётся на месте.
    /// </remarks>
    public const string Site = "site";

    // Карточка записи
    public const string Confirm = "confirm";
    public const string Reschedule = "move";
    public const string Cancel = "cancel";
    public const string Details = "info";

    /// <summary>Вернуться к карточке записи: рисуется заново поверх текущего сообщения.</summary>
    public const string Card = "card";

    // Подтверждение отмены
    public const string CancelYes = "cancel-yes";
    public const string CancelNo = "cancel-no";

    // Мастер переноса: мастер → дата → время → подтверждение
    public const string MoveMaster = "mv-master";
    public const string MoveDate = "mv-date";
    public const string MovePage = "mv-page";
    public const string MoveTime = "mv-time";
    public const string MoveApply = "mv-apply";
    public const string MoveBack = "mv-back";

    // Мастер новой записи: услуга → мастер → дата → время → имя → телефон
    public const string BookCategory = "bk-cat";
    public const string BookService = "bk-svc";
    public const string BookMaster = "bk-master";
    public const string BookDate = "bk-date";
    public const string BookPage = "bk-page";
    public const string BookTime = "bk-time";
    public const string BookApply = "bk-apply";
    public const string BookSkip = "bk-skip";
    public const string BookBack = "bk-back";
    public const string BookCancel = "bk-cancel";
    public const string BookUsePhone = "bk-use-phone";

    // Админский бот
    public const string WriteToClient = "a-write";
    public const string ClientCard = "a-client";
}

/// <summary>Шаги диалога, которые хранятся в <c>TelegramStates.Step</c>.</summary>
public static class TelegramSteps
{
    public const string CancelConfirm = "cancel-confirm";

    public const string MoveMaster = "move-master";
    public const string MoveDate = "move-date";
    public const string MoveTime = "move-time";
    public const string MoveConfirm = "move-confirm";

    public const string BookCategory = "book-category";
    public const string BookService = "book-service";
    public const string BookMaster = "book-master";
    public const string BookDate = "book-date";
    public const string BookTime = "book-time";
    public const string BookName = "book-name";
    public const string BookPhone = "book-phone";
    public const string BookComment = "book-comment";
    public const string BookConfirm = "book-confirm";

    /// <summary>Админ пишет текст, который надо передать клиенту.</summary>
    public const string AdminWrite = "admin-write";
}
