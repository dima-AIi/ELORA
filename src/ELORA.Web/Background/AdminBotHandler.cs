using ELORA.Web.Data.Repositories;
using ELORA.Web.Helpers;
using ELORA.Web.Models;
using ELORA.Web.Services;
using ELORA.Web.Services.Telegram;

namespace ELORA.Web.Background;

/// <summary>
/// Админский бот: уведомления о записях, сводки по дням, поиск клиентов и статистика.
/// </summary>
/// <remarks>
/// Отдельный бот нужен не ради безопасности — она держится на проверке <c>chat_id</c> в
/// <see cref="EnsureAdminAsync"/>. Причина в том, что меню команд в Telegram задаётся на бота:
/// в публичном боте клиенты видели бы «Сегодня» и «Статистика».
/// </remarks>
public sealed class AdminBotHandler : ITelegramUpdateHandler
{
    private const int UpcomingDays = 7;

    private readonly TelegramService _telegram;
    private readonly TelegramStateRepository _states;
    private readonly BookingRepository _bookings;
    private readonly BookingService _bookingService;
    private readonly ClientRepository _clients;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AdminBotHandler> _logger;

    public AdminBotHandler(
        TelegramService telegram,
        TelegramStateRepository states,
        BookingRepository bookings,
        BookingService bookingService,
        ClientRepository clients,
        IConfiguration configuration,
        ILogger<AdminBotHandler> logger)
    {
        _telegram = telegram;
        _states = states;
        _bookings = bookings;
        _bookingService = bookingService;
        _clients = clients;
        _configuration = configuration;
        _logger = logger;
    }

    public IReadOnlyList<(string Command, string Description)> Commands => new[]
    {
        ("today", "Записи на сегодня"),
        ("week", "Ближайшая неделя"),
        ("clients", "Поиск клиента"),
        ("stats", "Выручка за месяц"),
        ("help", "Помощь")
    };

    /// <summary>
    /// Контакт в админском боте смысла не имеет: владелец и так известен, а чат клиента
    /// привязан к клиентскому боту — id чата у каждого бота свой, переносить его бессмысленно.
    /// </summary>
    public Task HandleContactAsync(
        TelegramBot bot, long chatId, string phone, string name, CancellationToken cancellationToken) =>
        Task.CompletedTask;

    // ================= Сообщения =================

    public async Task HandleMessageAsync(
        TelegramBot bot, long chatId, string text, CancellationToken cancellationToken)
    {
        if (!await EnsureAdminAsync(bot, chatId, cancellationToken)) return;

        // Если админ начал писать клиенту — текст уходит клиенту, а не в поиск.
        if (!text.StartsWith('/') && await TrySendToClientAsync(bot, chatId, text, cancellationToken))
            return;

        if (text.StartsWith("/today", StringComparison.OrdinalIgnoreCase))
        {
            await SendTodayAsync(bot, chatId, cancellationToken);
            return;
        }

        if (text.StartsWith("/week", StringComparison.OrdinalIgnoreCase))
        {
            await SendWeekAsync(bot, chatId, cancellationToken);
            return;
        }

        if (text.StartsWith("/stats", StringComparison.OrdinalIgnoreCase))
        {
            await SendStatsAsync(bot, chatId, cancellationToken);
            return;
        }

        if (text.StartsWith("/clients", StringComparison.OrdinalIgnoreCase))
        {
            await SendClientsAsync(bot, chatId, text.Length > 8 ? text[8..].Trim() : null, cancellationToken);
            return;
        }

        if (text.StartsWith("/start", StringComparison.OrdinalIgnoreCase) ||
            text.StartsWith("/help", StringComparison.OrdinalIgnoreCase))
        {
            await SendHelpAsync(bot, chatId, cancellationToken);
            return;
        }

        // Любой свободный текст считаем поиском клиента — так быстрее, чем набирать /clients.
        await SendClientsAsync(bot, chatId, text, cancellationToken);
    }

    private async Task SendHelpAsync(TelegramBot bot, long chatId, CancellationToken cancellationToken)
    {
        var text = "Бот администратора ELORA.\n\n" +
                   "/today — записи на сегодня\n" +
                   "/week — ближайшая неделя и загрузка\n" +
                   "/clients — поиск клиента (или просто напишите имя либо телефон)\n" +
                   "/stats — выручка за текущий месяц\n\n" +
                   "Карточки новых записей приходят сюда сами: их можно подтвердить, перенести или отменить.";

        await bot.SendMessageAsync(chatId.ToString(), text, PanelButtons(), cancellationToken);
    }

    private IReadOnlyList<IReadOnlyList<TelegramButton>>? PanelButtons()
    {
        var site = _configuration["Site:PublicUrl"]?.TrimEnd('/');
        if (string.IsNullOrWhiteSpace(site)) return null;

        // Mini App Telegram открывает только по HTTPS: на localhost кнопка открыла бы
        // пустой экран вместо панели. Пока адрес не публичный — обычная ссылка в браузер.
        var button = site.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            ? TelegramButton.WebApp("🖥 Панель управления", $"{site}/admin")
            : TelegramButton.Link("🖥 Панель управления", $"{site}/admin");

        return new[] { new[] { button } };
    }

    // ================= Сводки =================

    private async Task SendTodayAsync(TelegramBot bot, long chatId, CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        var items = _bookings.GetBookings(from: today, to: today)
            .Where(b => b.Status is BookingStatuses.Pending or BookingStatuses.Confirmed)
            .OrderBy(b => b.StartLocal)
            .ToList();

        if (items.Count == 0)
        {
            await bot.SendMessageAsync(chatId.ToString(),
                $"На сегодня ({today:dd.MM.yyyy}) записей нет.", PanelButtons(), cancellationToken);
            return;
        }

        var total = items.Sum(b => b.Price);
        await bot.SendMessageAsync(chatId.ToString(),
            $"<b>Сегодня, {Money.DateWithWeekday(today.ToDateTime(TimeOnly.MinValue))}</b>\n" +
            $"Записей: {items.Count} · ожидаемая выручка {Money.Format(total)}",
            null, cancellationToken);

        foreach (var booking in items)
        {
            var status = booking.Status == BookingStatuses.Confirmed ? "✅" : "🕐";
            await bot.SendMessageAsync(chatId.ToString(),
                $"{status} <b>{booking.StartLocal:HH:mm}–{booking.EndLocal:HH:mm}</b>\n" +
                $"{TelegramService.Escape(booking.ServiceName)}\n" +
                $"Мастер: {TelegramService.Escape(booking.MasterName)}\n" +
                $"Клиент: {TelegramService.Escape(booking.ClientName)}, {TelegramService.PhoneLink(booking.ClientPhone)}" +
                (string.IsNullOrWhiteSpace(booking.Comment)
                    ? ""
                    : $"\nКомментарий: {TelegramService.Escape(booking.Comment!)}"),
                BookingButtons(booking), cancellationToken);
        }
    }

    private async Task SendWeekAsync(TelegramBot bot, long chatId, CancellationToken cancellationToken)
    {
        var from = DateOnly.FromDateTime(DateTime.Now);
        var to = from.AddDays(UpcomingDays);
        var items = _bookings.GetBookings(from: from, to: to)
            .Where(b => b.Status is BookingStatuses.Pending or BookingStatuses.Confirmed)
            .ToList();

        if (items.Count == 0)
        {
            await bot.SendMessageAsync(chatId.ToString(),
                "На ближайшую неделю записей нет.", PanelButtons(), cancellationToken);
            return;
        }

        var lines = new List<string>();
        for (var day = from; day <= to; day = day.AddDays(1))
        {
            var perDay = items.Where(b => DateOnly.FromDateTime(b.StartLocal) == day).ToList();
            if (perDay.Count == 0) continue;

            lines.Add($"<b>{Money.DateWithWeekday(day.ToDateTime(TimeOnly.MinValue))}</b> — " +
                      $"{perDay.Count} · {Money.Format(perDay.Sum(b => b.Price))}");
            lines.AddRange(perDay.Select(b =>
                $"   {b.StartLocal:HH:mm} {TelegramService.Escape(b.ServiceName)} — " +
                $"{TelegramService.Escape(b.ClientName)}"));
        }

        await bot.SendMessageAsync(chatId.ToString(),
            $"<b>Ближайшая неделя</b>\nВсего записей: {items.Count} · " +
            $"ожидаемая выручка {Money.Format(items.Sum(b => b.Price))}\n\n" +
            string.Join("\n", lines),
            PanelButtons(), cancellationToken);
    }

    private async Task SendStatsAsync(TelegramBot bot, long chatId, CancellationToken cancellationToken)
    {
        var today = DateTime.Now;
        var from = new DateOnly(today.Year, today.Month, 1);
        var to = from.AddMonths(1).AddDays(-1);

        var items = _bookings.GetBookings(from: from, to: to);
        var completed = items.Where(b => b.Status == BookingStatuses.Completed).ToList();
        var cancelled = items.Count(b => b.Status == BookingStatuses.Cancelled);
        var upcoming = items.Count(b => b.Status is BookingStatuses.Pending or BookingStatuses.Confirmed);

        await bot.SendMessageAsync(chatId.ToString(),
            $"<b>{Money.Month(today.Month)} {today.Year}</b>\n\n" +
            $"Завершено визитов: {completed.Count}\n" +
            $"Выручка: {Money.Format(completed.Sum(b => b.Price))}\n" +
            $"Средний чек: {Money.Format(completed.Count == 0 ? 0 : completed.Sum(b => b.Price) / completed.Count)}\n\n" +
            $"Ещё впереди: {upcoming}\n" +
            $"Отменено: {cancelled}",
            PanelButtons(), cancellationToken);
    }

    private async Task SendClientsAsync(
        TelegramBot bot, long chatId, string? search, CancellationToken cancellationToken)
    {
        var found = _clients.GetAll(search);
        if (found.Count == 0)
        {
            await bot.SendMessageAsync(chatId.ToString(),
                string.IsNullOrWhiteSpace(search)
                    ? "Клиентов пока нет."
                    : $"По запросу «{TelegramService.Escape(search!)}» никого не нашёл.",
                PanelButtons(), cancellationToken);
            return;
        }

        var shown = found.Take(10).ToList();
        var lines = shown.Select(c =>
            $"• <b>{TelegramService.Escape(c.Name)}</b> — {TelegramService.PhoneLink(c.Phone)}\n" +
            $"   визитов: {c.Visits}" +
            (c.LastVisit is null ? ", ещё не были" : $", последний {c.LastVisit:dd.MM.yyyy}") +
            (c.TelegramConnected ? ", Telegram подключён" : ""));

        var header = string.IsNullOrWhiteSpace(search)
            ? $"<b>Клиенты</b> (показаны {shown.Count} из {found.Count})"
            : $"<b>Найдено по «{TelegramService.Escape(search!)}»</b>: {found.Count}";

        await bot.SendMessageAsync(chatId.ToString(),
            header + "\n\n" + string.Join("\n", lines),
            null, cancellationToken);

        foreach (var client in shown.Take(3))
            await bot.SendMessageAsync(chatId.ToString(), ClientCard(client), null, cancellationToken);
    }

    private static string ClientCard(ClientSummary client)
    {
        var text = $"👤 <b>{TelegramService.Escape(client.Name)}</b>\n" +
                   $"{TelegramService.PhoneLink(client.Phone)}\n" +
                   $"Визитов: {client.Visits}";

        if (client.LastVisit is not null)
            text += $"\nПоследний визит: {client.LastVisit:dd.MM.yyyy} ({client.DaysSinceVisit} дн. назад)";

        if (client.NextVisit is not null)
            text += $"\nБлижайшая запись: {client.NextVisit:dd.MM.yyyy HH:mm}";

        if (client.TotalSpent > 0) text += $"\nОставил всего: {Money.Format(client.TotalSpent)}";
        if (!string.IsNullOrWhiteSpace(client.Notes))
            text += $"\n\nЗаметка: {TelegramService.Escape(client.Notes!)}";

        return text;
    }

    // ================= Кнопки =================

    public async Task HandleCallbackAsync(
        TelegramBot bot, string callbackId, long chatId, int messageId, string data,
        CancellationToken cancellationToken)
    {
        if (!await EnsureAdminAsync(bot, chatId, cancellationToken)) return;

        var (action, payload) = ClientBotHandler.Split(data);
        if (!int.TryParse(payload, out var bookingId))
        {
            await bot.AnswerCallbackAsync(callbackId, "Не понял команду", cancellationToken);
            return;
        }

        var booking = _bookings.GetById(bookingId);
        if (booking is null)
        {
            await bot.AnswerCallbackAsync(callbackId, "Запись не найдена", cancellationToken);
            return;
        }

        switch (action)
        {
            case TelegramActions.Confirm:
                await ConfirmAsync(bot, callbackId, chatId, booking, cancellationToken);
                break;

            case TelegramActions.Cancel:
                await AskCancelAsync(bot, callbackId, chatId, messageId, booking, cancellationToken);
                break;

            case TelegramActions.CancelYes:
                await CancelAsync(bot, callbackId, chatId, messageId, booking, cancellationToken);
                break;

            case TelegramActions.CancelNo:
                _states.Clear(TelegramRole.Admin, chatId);
                await bot.AnswerCallbackAsync(callbackId, "Оставил как есть", cancellationToken);
                await ShowCardAsync(bot, chatId, messageId, booking, cancellationToken);
                break;

            case TelegramActions.Reschedule:
                await SendManageLinkAsync(bot, callbackId, chatId, booking, cancellationToken);
                break;

            case TelegramActions.Details:
                await bot.AnswerCallbackAsync(callbackId, null, cancellationToken);
                await bot.SendMessageAsync(chatId.ToString(),
                    TelegramService.ComposeBookingText("📋 <b>Запись</b>", booking),
                    BookingButtons(booking), cancellationToken);
                break;

            case TelegramActions.WriteToClient:
                await AskWriteAsync(bot, callbackId, chatId, booking, cancellationToken);
                break;

            case TelegramActions.ClientCard:
                await bot.AnswerCallbackAsync(callbackId, null, cancellationToken);
                await ShowCardAsync(bot, chatId, messageId, booking, cancellationToken);
                break;

            default:
                await bot.AnswerCallbackAsync(callbackId, "Кнопка устарела", cancellationToken);
                break;
        }
    }

    private async Task ConfirmAsync(
        TelegramBot bot, string callbackId, long chatId, Booking booking, CancellationToken cancellationToken)
    {
        _bookings.UpdateStatus(booking.Id, BookingStatuses.Confirmed);
        await bot.AnswerCallbackAsync(callbackId, "Запись подтверждена", cancellationToken);

        var updated = _bookings.GetById(booking.Id);
        if (updated is not null)
        {
            await bot.SendMessageAsync(chatId.ToString(),
                $"✅ Подтверждено: {updated.ClientName}, {updated.StartLocal:dd.MM HH:mm}",
                null, cancellationToken);
            await _telegram.NotifyStatusChangedAsync(updated, cancellationToken);
        }
    }

    private async Task AskCancelAsync(
        TelegramBot bot, string callbackId, long chatId, int messageId, Booking booking,
        CancellationToken cancellationToken)
    {
        _states.Save(TelegramRole.Admin, new TelegramState
        {
            ChatId = chatId,
            Step = TelegramSteps.CancelConfirm,
            BookingId = booking.Id
        });

        await bot.AnswerCallbackAsync(callbackId, null, cancellationToken);

        var rows = new[]
        {
            new[]
            {
                TelegramButton.Callback("✅ Да, отменить", $"{TelegramActions.CancelYes}:{booking.Id}"),
                TelegramButton.Callback("← Нет", $"{TelegramActions.CancelNo}:{booking.Id}")
            }
        };

        await bot.EditMessageTextAsync(chatId.ToString(), messageId,
            "❌ <b>Отменить запись?</b>\n\n" +
            $"{TelegramService.Escape(booking.ClientName)}, {booking.StartLocal:dd.MM.yyyy HH:mm}\n" +
            $"{TelegramService.Escape(booking.ServiceName)} · {TelegramService.Escape(booking.MasterName)}\n\n" +
            "Клиент получит уведомление об отмене.",
            rows, cancellationToken: cancellationToken);
    }

    private async Task CancelAsync(
        TelegramBot bot, string callbackId, long chatId, int messageId, Booking booking,
        CancellationToken cancellationToken)
    {
        var result = await _bookingService.CancelAsync(booking.ManageToken, true, cancellationToken);

        _states.Clear(TelegramRole.Admin, chatId);

        if (!result.Ok)
        {
            await bot.AnswerCallbackAsync(callbackId, result.Error ?? "Не получилось отменить", cancellationToken);
            await ShowCardAsync(bot, chatId, messageId, booking, cancellationToken);
            return;
        }

        await bot.AnswerCallbackAsync(callbackId, "Запись отменена", cancellationToken);
        await bot.EditMessageTextAsync(chatId.ToString(), messageId,
            $"❌ Запись отменена: {TelegramService.Escape(booking.ClientName)}, " +
            $"{booking.StartLocal:dd.MM.yyyy HH:mm}",
            null, clearKeyboard: true, cancellationToken: cancellationToken);
    }

    private async Task ShowCardAsync(
        TelegramBot bot, long chatId, int messageId, Booking booking, CancellationToken cancellationToken)
    {
        await bot.EditMessageTextAsync(chatId.ToString(), messageId,
            TelegramService.ComposeBookingText("📋 <b>Запись</b>", booking),
            BookingButtons(booking), cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Перенос у админа открывается страницей управления: там видно расписание целиком,
    /// а набирать даты кнопками с телефона администратору ни к чему.
    /// </summary>
    private async Task SendManageLinkAsync(
        TelegramBot bot, string callbackId, long chatId, Booking booking, CancellationToken cancellationToken)
    {
        var site = _configuration["Site:PublicUrl"]?.TrimEnd('/');
        if (string.IsNullOrWhiteSpace(site))
        {
            await bot.AnswerCallbackAsync(callbackId, "Не задан адрес сайта (Site:PublicUrl)", cancellationToken);
            return;
        }

        await bot.AnswerCallbackAsync(callbackId, null, cancellationToken);
        await bot.SendMessageAsync(chatId.ToString(),
            $"Выберите новое время на странице ELORA:\n{site}/manage/{booking.ManageToken}",
            null, cancellationToken);
    }

    private async Task AskWriteAsync(
        TelegramBot bot, string callbackId, long chatId, Booking booking, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(booking.ClientTelegramChatId))
        {
            await bot.AnswerCallbackAsync(callbackId, "Клиент не подключил Telegram", cancellationToken);
            return;
        }

        _states.Save(TelegramRole.Admin, new TelegramState
        {
            ChatId = chatId,
            Step = TelegramSteps.AdminWrite,
            BookingId = booking.Id
        });

        await bot.AnswerCallbackAsync(callbackId, null, cancellationToken);
        await bot.SendMessageAsync(chatId.ToString(),
            $"Напишите сообщение для {TelegramService.Escape(booking.ClientName)}. " +
            "Оно уйдёт от имени студии, ответ придёт сюда же.",
            new[] { new[] { TelegramButton.Callback("✖️ Отмена", $"{TelegramActions.ClientCard}:{booking.Id}") } },
            cancellationToken);
    }

    /// <summary>Передаёт текст администратора клиенту. Возвращает false, если сейчас не этот шаг.</summary>
    private async Task<bool> TrySendToClientAsync(
        TelegramBot bot, long chatId, string text, CancellationToken cancellationToken)
    {
        var state = _states.Get(TelegramRole.Admin, chatId);
        if (state?.Step != TelegramSteps.AdminWrite || state.BookingId is null) return false;

        var booking = _bookings.GetById(state.BookingId.Value);
        _states.Clear(TelegramRole.Admin, chatId);

        if (booking?.ClientTelegramChatId is null or "")
        {
            await bot.SendMessageAsync(chatId.ToString(), "Клиент не подключил Telegram — написать не получилось.",
                null, cancellationToken);
            return true;
        }

        // Отправляем через клиентский бот: сообщение должно прийти от знакомого бота студии.
        var sent = await _telegram.Bots.Client.SendMessageAsync(booking.ClientTelegramChatId!,
            $"💬 <b>Сообщение от студии ELORA</b>\n\n{TelegramService.Escape(text)}", null, cancellationToken);

        await bot.SendMessageAsync(chatId.ToString(),
            sent ? "Сообщение отправлено." : "Не удалось отправить сообщение.",
            null, cancellationToken);

        return true;
    }

    private IReadOnlyList<IReadOnlyList<TelegramButton>> BookingButtons(Booking booking) => new[]
    {
        new[]
        {
            TelegramButton.Callback("✅ Подтвердить", $"{TelegramActions.Confirm}:{booking.Id}"),
            TelegramButton.Callback("🕒 Перенести", $"{TelegramActions.Reschedule}:{booking.Id}")
        },
        new[]
        {
            TelegramButton.Callback("❌ Отменить", $"{TelegramActions.Cancel}:{booking.Id}"),
            TelegramButton.Callback("💬 Написать", $"{TelegramActions.WriteToClient}:{booking.Id}")
        }
    };

    /// <summary>Пускать в админского бота можно только администратора.</summary>
    private async Task<bool> EnsureAdminAsync(TelegramBot bot, long chatId, CancellationToken cancellationToken)
    {
        var adminChatId = _telegram.AdminChatId;
        if (!string.IsNullOrWhiteSpace(adminChatId) &&
            string.Equals(adminChatId, chatId.ToString(), StringComparison.Ordinal))
            return true;

        // Chat ID администратора ещё не задан — показать его здесь единственный способ:
        // узнать свой номер в Telegram иначе неоткуда, а без него бот бесполезен.
        if (string.IsNullOrWhiteSpace(adminChatId))
        {
            await bot.SendMessageAsync(chatId.ToString(),
                "Бот настроен, но в админке не указан Chat ID администратора, поэтому работать с ним нельзя.\n\n" +
                $"Ваш Chat ID: <code>{chatId}</code>\n\n" +
                "Скопируйте его в «Настройки» → «Chat ID администратора» на странице /admin/settings, " +
                "и напишите здесь /start.",
                null, cancellationToken);

            return false;
        }

        _logger.LogWarning("Админский бот: отказ постороннему чату {ChatId}", chatId);

        await bot.SendMessageAsync(chatId.ToString(),
            "Этот бот только для администратора студии.\n\n" +
            "Записаться и управлять своей записью можно у бота студии ELORA.",
            null, cancellationToken);

        return false;
    }
}
