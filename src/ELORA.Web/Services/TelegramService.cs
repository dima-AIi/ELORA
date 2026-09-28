using ELORA.Web.Data.Repositories;
using ELORA.Web.Helpers;
using ELORA.Web.Models;
using ELORA.Web.Services.Telegram;

namespace ELORA.Web.Services;

/// <summary>
/// Одна строка проверки готовности Telegram: что проверяем, выполнено ли и что делать,
/// если нет. Список таких строк показывается в админке — уведомления молчат по четырём
/// разным причинам, и «не приходит» без объяснения не лечится.
/// </summary>
public sealed record TelegramCheck(string Title, bool Ok, string Hint);

/// <summary>
/// Сценарии уведомлений: что и кому отправляем. Транспорт — в <see cref="TelegramBot"/>,
/// выбор бота — здесь: клиенту пишет публичный бот, администратору — админский.
/// Токен можно задать переменной окружения (Telegram__BotToken) или прямо в админке —
/// тогда он ложится в таблицу Settings. Переменная окружения главнее.
/// </summary>
public sealed class TelegramService
{
    private readonly TelegramBots _bots;
    private readonly SettingsRepository _settings;
    private readonly ILogger<TelegramService> _logger;

    public TelegramService(TelegramBots bots, SettingsRepository settings, ILogger<TelegramService> logger)
    {
        _bots = bots;
        _settings = settings;
        _logger = logger;
    }

    public TelegramBots Bots => _bots;

    public bool IsConfigured => _bots.Client.IsConfigured || _bots.Admin.IsConfigured;
    public bool IsEnabled => _settings.GetBool("Telegram.Enabled");
    public bool IsClientBotConfigured => _bots.Client.IsConfigured;
    public bool IsAdminBotConfigured => _bots.Admin.IsConfigured;

    /// <summary>Ключ настройки с токеном для роли. Оба ключа живут в таблице Settings.</summary>
    private static string TokenKey(TelegramRole role) =>
        role == TelegramRole.Admin ? "Telegram.AdminBotToken" : "Telegram.BotToken";

    /// <summary>
    /// Берёт токены, сохранённые в админке, и отдаёт их ботам. Вызывается один раз при старте,
    /// до запуска фоновых служб: те проверяют готовность бота в самом начале и без токена
    /// просто выключаются. Токен из переменных окружения не перезаписывается.
    /// </summary>
    public void ApplyStoredTokens()
    {
        foreach (var bot in _bots.All)
        {
            var stored = NullIfEmpty(_settings.Get(TokenKey(bot.Role)));
            if (stored is null) continue;
            if (_bots.ApplyStoredToken(bot.Role, stored))
                _logger.LogInformation("{Bot}: токен взят из настроек сайта", bot.Describe());
        }
    }

    /// <summary>Откуда взят токен — чтобы в админке было видно, почему поле не действует.</summary>
    public string TokenSource(TelegramRole role)
    {
        var bot = _bots.For(role);
        if (!bot.IsConfigured) return "не задан";
        return bot.TokenFromEnvironment ? "переменная окружения панели" : "сохранён в настройках сайта";
    }

    /// <summary>
    /// Сохраняет токены из админки и сразу применяет их. Пустая строка означает «не менять»:
    /// поле пароля всегда приходит пустым, и стирать токен при каждом сохранении нельзя.
    /// Чтобы стереть осознанно, есть отдельный флаг <paramref name="clear"/>.
    /// </summary>
    public string? SaveTokens(string? clientToken, string? adminToken, bool clearClient, bool clearAdmin)
    {
        var problems = new List<string>();

        foreach (var (role, value, clear) in new[]
                 {
                     (TelegramRole.Client, clientToken, clearClient),
                     (TelegramRole.Admin, adminToken, clearAdmin)
                 })
        {
            var bot = _bots.For(role);
            var label = role == TelegramRole.Admin ? "админского бота" : "клиентского бота";

            if (clear)
            {
                _settings.Set(TokenKey(role), "");
                _bots.ApplyStoredToken(role, null);
                if (bot.TokenFromEnvironment) problems.Add($"токен {label} задан переменной окружения — её сброс из админки не действует");
                continue;
            }

            var token = NullIfEmpty(value);
            if (token is null) continue;

            if (!LooksLikeToken(token))
            {
                problems.Add($"токен {label} не похож на токен: ожидается вид 123456789:AA… (цифры, двоеточие, буквы)");
                continue;
            }

            _settings.Set(TokenKey(role), token);
            _bots.ApplyStoredToken(role, token);
            if (bot.TokenFromEnvironment)
                problems.Add($"токен {label} сохранён, но сейчас действует токен из переменной окружения панели");
        }

        return problems.Count > 0 ? string.Join("; ", problems) : null;
    }

    /// <summary>
    /// Проверка формы токена. Полная проверка — только запросом getMe, но отсеять
    /// очевидную опечатку (вставили имя бота, ссылку или обрезанный токен) можно сразу.
    /// </summary>
    private static bool LooksLikeToken(string token)
    {
        var colon = token.IndexOf(':');
        if (colon < 5 || colon > 12) return false;
        if (!token[..colon].All(char.IsDigit)) return false;
        var tail = token[(colon + 1)..];
        return tail.Length >= 30 && tail.All(c => char.IsLetterOrDigit(c) || c is '_' or '-');
    }

    /// <summary>
    /// Четыре условия, без которых уведомление администратору не уйдёт. Проверяются
    /// независимо: человек включил галочку, но не задал токен — молчание объясняется
    /// не галочкой, а токеном, и наоборот.
    /// </summary>
    public IReadOnlyList<TelegramCheck> Checks() => new[]
    {
        new TelegramCheck(
            "Токен клиентского бота",
            _bots.Client.IsConfigured,
            "Вставьте токен в поле «Токен клиентского бота» ниже и нажмите «Сохранить токены» — " +
            "перезапуск не нужен. Токен выдаёт @BotFather командой /token."),

        new TelegramCheck(
            "Токен админского бота",
            _bots.Admin.IsConfigured,
            "Вставьте токен в поле «Токен админского бота» ниже. Именно админский бот пишет " +
            "администратору о новых записях; без него сообщение уйдёт от клиентского бота."),

        new TelegramCheck(
            "Уведомления включены",
            IsEnabled,
            "Поставьте галочку «Уведомления включены» ниже и нажмите «Сохранить». " +
            "Пока она снята, сайт не отправляет ничего и не слушает команды ботов."),

        new TelegramCheck(
            "Chat ID администратора",
            AdminChatId is not null,
            "Напишите /start админскому боту в Telegram — он ответит вашим Chat ID. " +
            "Впишите его ниже: без Chat ID уведомление отправлять некуда.")
    };

    /// <summary>
    /// Почему уведомление администратору не уйдёт — одной строкой, для лога и для админки.
    /// <c>null</c> означает, что канал готов.
    /// </summary>
    public string? BlockReason()
    {
        if (!IsEnabled) return "в настройках выключен Telegram (галочка «Уведомления включены» снята)";
        if (AdminChatId is null) return "не указан Chat ID администратора";
        if (!AdminChannel.IsConfigured) return "не задан токен ни одного бота";
        return null;
    }

    public string? AdminChatId => NullIfEmpty(_settings.Get("Telegram.AdminChatId"));

    public int ReminderHoursBefore => Math.Max(1, _settings.GetInt("Telegram.ReminderHoursBefore", 24));

    /// <summary>Через какого бота говорим с администратором: админский, а если его нет — клиентский.</summary>
    private TelegramBot AdminChannel => _bots.Admin.IsConfigured ? _bots.Admin : _bots.Client;

    /// <summary>Клиенту пишет только публичный бот — тот, которого он видел на сайте.</summary>
    private TelegramBot ClientChannel => _bots.Client;

    public string BuildStartLink(string code) => _bots.Client.BuildStartLink(code);

    /// <summary>
    /// Подтягивает имена обоих ботов и сохраняет их в настройках. Нужно не для красоты:
    /// имя клиентского бота подставляется в ссылку привязки на странице успешной записи,
    /// и без него клиент получит ссылку без адреса. Поэтому имя пишется в БД, а не только в память.
    /// </summary>
    public async Task EnsureUsernamesAsync(CancellationToken cancellationToken = default)
    {
        foreach (var bot in _bots.All)
        {
            if (!bot.IsConfigured) continue;
            try
            {
                var username = await bot.GetBotUsernameAsync(cancellationToken);
                if (string.IsNullOrWhiteSpace(username)) continue;

                var key = bot.Role == TelegramRole.Admin ? "Telegram.AdminBotUsername" : "Telegram.BotUsername";
                var clean = username.TrimStart('@');
                if (_settings.Get(key) != clean) _settings.Set(key, clean);
            }
            catch (Exception ex)
            {
                // Имя бота — необязательное украшение, падать из-за него нельзя.
                _logger.LogWarning(ex, "Не удалось получить имя бота ({Role})", bot.Role);
            }
        }
    }

    // ---------------- Служебное ----------------

    /// <summary>Проверка связи с администратором. Возвращает null при успехе либо причину отказа словами.</summary>
    public async Task<string?> SendTestAsync(string chatId, string text, CancellationToken cancellationToken = default)
    {
        if (!AdminChannel.IsConfigured) return "токен бота не задан";
        return await AdminChannel.SendMessageExAsync(chatId, text, null, cancellationToken);
    }

    /// <summary>
    /// Отправка администратору с запасным каналом.
    /// </summary>
    /// <remarks>
    /// Админский бот — правильный канал: только он умеет кнопки «Подтвердить» и «Перенести».
    /// Но Telegram запрещает боту писать первым, и пока владелец не нажал Start в админском
    /// боте, каждое уведомление упирается в «chat not found». Клиентский бот у него уже
    /// открыт, поэтому при отказе повторяем через него — <b>без кнопок</b>: нажатие ушло бы
    /// обработчику клиентского бота, который про подтверждение записи ничего не знает,
    /// и кнопка просто не сработала бы. Вместо кнопок в текст добавляется подсказка.
    /// </remarks>
    private async Task<bool> SendToAdminAsync(
        string chatId, string text, IReadOnlyList<IReadOnlyList<TelegramButton>>? buttons,
        CancellationToken cancellationToken)
    {
        if (await AdminChannel.SendMessageAsync(chatId, text, buttons, cancellationToken)) return true;

        if (!ReferenceEquals(AdminChannel, _bots.Admin) || !_bots.Client.IsConfigured) return false;

        _logger.LogWarning(
            "Админский бот не доставил сообщение в чат {ChatId} — повторяю через клиентского. " +
            "Кнопки подтверждения работают только в админском боте: напишите ему /start.",
            chatId);

        return await _bots.Client.SendMessageAsync(
            chatId,
            text + "\n\n<i>Кнопки подтверждения доступны в админском боте — напишите ему /start.</i>",
            null, cancellationToken);
    }

    // ---------------- Сценарии ----------------

    /// <param name="notifyClient">
    /// Писать ли подтверждение клиенту. Диалог-мастер в боте передаёт <c>false</c>:
    /// он сам сообщает «Вы записаны!» в том же чате.
    /// </param>
    public async Task NotifyNewBookingAsync(
        Booking booking, bool notifyClient = true, CancellationToken cancellationToken = default)
    {
        // Молчание уведомлений — самая частая жалоба владельца. Поэтому причина отказа
        // пишется в лог: иначе «не приходит» приходится выяснять перебором настроек.
        var blocked = BlockReason();

        // Клиенту пишем ДО карточки администратору — тогда в самой карточке видно, дошло ли
        // подтверждение. Telegram не даёт боту написать первым тому, кто не открывал бота,
        // и без этой строки владелец уверен, что клиента предупредили, и не звонит.
        var clientFailure = notifyClient
            ? await NotifyClientAboutNewBookingAsync(booking, cancellationToken)
            : null;

        var text = ComposeBookingText("🆕 <b>Новая запись</b>", booking);

        if (notifyClient)
        {
            text += clientFailure is null
                ? "\n\n✅ Подтверждение клиенту отправлено — чат привязан."
                : $"\n\n⚠️ <b>Клиенту не ушло:</b> {clientFailure}.\n" +
                  "Позвоните: " + PhoneLink(booking.ClientPhone);
        }

        if (blocked is not null)
        {
            _logger.LogWarning(
                "Запись #{Id} на {Start:dd.MM HH:mm}: уведомление администратору не отправлено — {Reason}",
                booking.Id, booking.StartLocal, blocked);
        }
        else if (_settings.GetBool("Telegram.NotifyOnNewBooking", true))
        {
            var buttons = AdminBookingButtons(booking);

            if (!await SendToAdminAsync(AdminChatId!, text, buttons, cancellationToken))
                _logger.LogWarning("Запись #{Id}: администратору отправить не удалось (chat {ChatId})",
                    booking.Id, AdminChatId);

            var masterChat = GetMasterChatId(booking.MasterId);
            if (masterChat is not null && masterChat != AdminChatId)
                await AdminChannel.SendMessageAsync(masterChat, text, buttons, cancellationToken);
        }
    }

    /// <summary>
    /// Подтверждение клиенту о только что созданной записи. <c>null</c> — ушло,
    /// иначе причина отказа словами: её дописывают в карточку администратору.
    /// </summary>
    /// <remarks>
    /// Telegram запрещает боту писать первым, поэтому сообщение уходит лишь тем, кто уже
    /// привязал чат: написал боту <c>/start</c> или записался через бота. Ссылку для привязки
    /// страница успешной записи показывает всем — без неё подтверждение отправить некуда.
    /// </remarks>
    private async Task<string?> NotifyClientAboutNewBookingAsync(Booking booking, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(booking.ClientTelegramChatId))
        {
            // Так выглядит самая частая причина «клиенту не пришло»: он записался на сайте
            // и не открывал бота, а бот не может написать первым. В логе это должно быть
            // видно, иначе владелец снова будет искать поломку перебором настроек.
            _logger.LogInformation(
                "Запись #{Id}: подтверждение клиенту не отправлено — чат Telegram не привязан " +
                "(клиент не открывал бота и не переходил по ссылке привязки)", booking.Id);
            return "клиент не открывал бота — чат Telegram не привязан";
        }

        if (!IsEnabled)
        {
            _logger.LogWarning(
                "Запись #{Id}: подтверждение клиенту не отправлено — в настройках выключен Telegram",
                booking.Id);
            return "в настройках выключены уведомления";
        }

        if (!ClientChannel.IsConfigured)
        {
            _logger.LogWarning(
                "Запись #{Id}: подтверждение клиенту не отправлено — не задан токен клиентского бота",
                booking.Id);
            return "не задан токен клиентского бота";
        }

        var text = "✅ <b>Вы записаны в ELORA</b>\n\n" +
                   $"Услуга: {Escape(booking.ServiceName)}\n" +
                   $"Мастер: {Escape(booking.MasterName)}\n" +
                   $"Когда: {booking.StartLocal:dd.MM.yyyy} в {booking.StartLocal:HH:mm}\n" +
                   $"Стоимость: {Money.Format(booking.Price)}\n\n" +
                   "Перенести или отменить запись можно кнопками ниже — " +
                   "напомним за сутки до визита.";

        if (!await ClientChannel.SendMessageAsync(
                booking.ClientTelegramChatId!, text, ClientBookingButtons(booking), cancellationToken))
        {
            _logger.LogWarning(
                "Запись #{Id}: клиентское подтверждение не доставлено (чат {ChatId})",
                booking.Id, booking.ClientTelegramChatId);
            return "Telegram не принял сообщение — возможно, клиент заблокировал бота";
        }

        // Успех пишем тоже: без этой строки «клиенту не пришло» невозможно отличить
        // от «ушло, но он не посмотрел» — а вопрос владельца всегда звучит именно так.
        _logger.LogInformation(
            "Запись #{Id}: подтверждение клиенту отправлено (чат {ChatId})",
            booking.Id, booking.ClientTelegramChatId);
        return null;
    }

    public async Task NotifyCancelledAsync(Booking booking, bool byAdmin, CancellationToken cancellationToken = default)
    {
        if (Skipped("Отмена записи", booking)) return;

        var who = byAdmin ? "администратором" : "клиентом";
        var text = $"❌ <b>Запись отменена</b> ({who})\n\n" +
                   $"Услуга: {Escape(booking.ServiceName)}\n" +
                   $"Мастер: {Escape(booking.MasterName)}\n" +
                   $"Было: {booking.StartLocal:dd.MM.yyyy HH:mm}\n" +
                   $"Клиент: {Escape(booking.ClientName)}, {PhoneLink(booking.ClientPhone)}";

        if (AdminChatId is not null) await SendToAdminAsync(AdminChatId, text, null, cancellationToken);

        var masterChat = GetMasterChatId(booking.MasterId);
        if (masterChat is not null && masterChat != AdminChatId)
            await AdminChannel.SendMessageAsync(masterChat, text, null, cancellationToken);

        if (!string.IsNullOrWhiteSpace(booking.ClientTelegramChatId))
            await ClientChannel.SendMessageAsync(booking.ClientTelegramChatId!,
                $"Ваша запись на {booking.StartLocal:dd.MM.yyyy HH:mm} отменена.\n" +
                "Будем рады видеть вас снова — записаться можно на сайте ELORA.",
                null, cancellationToken);
    }

    public async Task NotifyRescheduledAsync(Booking booking, CancellationToken cancellationToken = default)
    {
        if (Skipped("Перенос записи", booking)) return;

        var text = $"🕒 <b>Запись перенесена</b>\n\n" +
                   $"Услуга: {Escape(booking.ServiceName)}\n" +
                   $"Мастер: {Escape(booking.MasterName)}\n" +
                   $"Новое время: {booking.StartLocal:dd.MM.yyyy HH:mm}\n" +
                   $"Клиент: {Escape(booking.ClientName)}, {PhoneLink(booking.ClientPhone)}";

        if (AdminChatId is not null) await SendToAdminAsync(AdminChatId, text, null, cancellationToken);

        var masterChat = GetMasterChatId(booking.MasterId);
        if (masterChat is not null && masterChat != AdminChatId)
            await AdminChannel.SendMessageAsync(masterChat, text, null, cancellationToken);

        if (!string.IsNullOrWhiteSpace(booking.ClientTelegramChatId))
            await ClientChannel.SendMessageAsync(booking.ClientTelegramChatId!,
                $"Ваша запись перенесена на {booking.StartLocal:dd.MM.yyyy HH:mm}.", null, cancellationToken);
    }

    public async Task NotifyStatusChangedAsync(Booking booking, CancellationToken cancellationToken = default)
    {
        if (!IsEnabled) return;
        if (string.IsNullOrWhiteSpace(booking.ClientTelegramChatId)) return;

        var text = booking.Status switch
        {
            BookingStatuses.Confirmed =>
                $"✅ Ваша запись подтверждена: {booking.StartLocal:dd.MM.yyyy HH:mm}\n{booking.ServiceName}, мастер {booking.MasterName}",
            BookingStatuses.Completed =>
                "Спасибо, что были у нас! Ждём вас снова в ELORA 💫",
            _ => $"Статус записи обновлён: {BookingStatuses.Title(booking.Status)}"
        };

        await ClientChannel.SendMessageAsync(booking.ClientTelegramChatId!, text, null, cancellationToken);
    }

    public async Task<bool> SendReminderAsync(Booking booking, CancellationToken cancellationToken = default)
    {
        if (Skipped("Напоминание о визите", booking)) return false;

        var text = $"⏰ <b>Напоминание о визите</b>\n\n" +
                   $"Завтра в {booking.StartLocal:HH:mm} ждём вас в ELORA.\n" +
                   $"Услуга: {Escape(booking.ServiceName)}\n" +
                   $"Мастер: {Escape(booking.MasterName)}\n\n" +
                   "Если планы изменились — перенесите или отмените прямо здесь.";

        // Клиенту напоминание уходит только если он привязал чат; иначе напоминаем админу,
        // чтобы он позвонил сам.
        if (!string.IsNullOrWhiteSpace(booking.ClientTelegramChatId))
            return await ClientChannel.SendMessageAsync(
                booking.ClientTelegramChatId!, text, ClientBookingButtons(booking), cancellationToken);

        if (AdminChatId is not null)
        {
            var adminText = text + $"\n\nКлиент: {Escape(booking.ClientName)}, {PhoneLink(booking.ClientPhone)}";
            return await SendToAdminAsync(
                AdminChatId, adminText, AdminBookingButtons(booking), cancellationToken);
        }

        return false;
    }

    // ---------------- Кнопки карточки ----------------

    /// <summary>Карточка для администратора: он подтверждает запись, поэтому «Подтвердить» есть.</summary>
    public static IReadOnlyList<IReadOnlyList<TelegramButton>> AdminBookingButtons(Booking booking) => new[]
    {
        new[]
        {
            TelegramButton.Callback("✅ Подтвердить", $"{TelegramActions.Confirm}:{booking.Id}"),
            TelegramButton.Callback("🕒 Перенести", $"{TelegramActions.Reschedule}:{booking.Id}")
        },
        new[] { TelegramButton.Callback("❌ Отменить", $"{TelegramActions.Cancel}:{booking.Id}") }
    };

    /// <summary>
    /// Карточка для клиента. «Подтвердить» здесь нет намеренно: подтверждает запись студия,
    /// а не клиент. Ему доступны детали, перенос и отмена.
    /// </summary>
    public static IReadOnlyList<IReadOnlyList<TelegramButton>> ClientBookingButtons(Booking booking) => new[]
    {
        new[]
        {
            TelegramButton.Callback("ℹ️ Детали", $"{TelegramActions.Details}:{booking.Id}"),
            TelegramButton.Callback("🕒 Перенести", $"{TelegramActions.Reschedule}:{booking.Id}")
        },
        new[] { TelegramButton.Callback("❌ Отменить", $"{TelegramActions.Cancel}:{booking.Id}") }
    };

    // ---------------- Текст ----------------

    public static string ComposeBookingText(string header, Booking booking) =>
        $"{header}\n\n" +
        $"Услуга: {Escape(booking.ServiceName)}\n" +
        $"Мастер: {Escape(booking.MasterName)}\n" +
        $"Дата: {booking.StartLocal:dd.MM.yyyy}\n" +
        $"Время: {booking.StartLocal:HH:mm}–{booking.EndLocal:HH:mm}\n" +
        $"Клиент: {Escape(booking.ClientName)}\n" +
        $"Телефон: {PhoneLink(booking.ClientPhone)}" +
        (string.IsNullOrWhiteSpace(booking.Comment) ? "" : $"\nКомментарий: {Escape(booking.Comment!)}");

    /// <summary>
    /// Телефон кликабельной ссылкой: inline-кнопка с <c>tel:</c> в Telegram запрещена,
    /// а ссылка в тексте сообщения работает — с неё администратор звонит в один тап.
    /// </summary>
    public static string PhoneLink(string phone)
    {
        var digits = new string(phone.Where(c => char.IsDigit(c) || c == '+').ToArray());
        return string.IsNullOrWhiteSpace(digits)
            ? Escape(phone)
            : $"<a href=\"tel:{digits}\">{Escape(phone)}</a>";
    }

    public static string Escape(string value) =>
        value.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    /// <summary>
    /// Общая проверка «Telegram выключен» для всех сценариев, кроме новой записи: там причина
    /// может быть и в Chat ID, поэтому у неё отдельная ветка с полным объяснением.
    /// </summary>
    private bool Skipped(string what, Booking booking)
    {
        if (IsEnabled) return false;
        _logger.LogWarning("{What} (запись #{Id}): не отправлено — в настройках выключен Telegram",
            what, booking.Id);
        return true;
    }

    // У мастеров отдельного чата пока нет: колонка Masters.TelegramChatId есть, но поля
    // в админке и сценария привязки тоже нет. Поэтому уведомления идут только администратору.
    private string? GetMasterChatId(int masterId) => null;
}
