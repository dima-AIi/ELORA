using ELORA.Web.Data.Repositories;
using ELORA.Web.Helpers;
using ELORA.Web.Models;
using ELORA.Web.Services;
using ELORA.Web.Services.Telegram;

namespace ELORA.Web.Background;

/// <summary>
/// Клиентский бот: привязка чата, список записей, перенос и отмена — всё кнопками в чате,
/// без выхода на сайт.
/// </summary>
/// <remarks>
/// Карточка записи всегда одна и та же: шаги переноса переписывают её через
/// <c>editMessageText</c>, а не присылают новые сообщения. Состояние шага лежит в
/// <c>TelegramStates</c>, потому что в <c>callback_data</c> влезает только 64 байта.
/// Шаги новой записи вынесены в <see cref="ClientBookingWizard"/>.
/// </remarks>
public sealed class ClientBotHandler : ITelegramUpdateHandler
{
    private const int DatesPerPage = 3;
    private const int SlotsPerRow = 3;
    private const int MaxBookingsInList = 5;

    private readonly TelegramService _telegram;
    private readonly ClientBookingWizard _wizard;
    private readonly TelegramStateRepository _states;
    private readonly BookingRepository _bookings;
    private readonly BookingService _bookingService;
    private readonly MasterRepository _masters;
    private readonly ScheduleService _schedule;
    private readonly SettingsRepository _settings;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ClientBotHandler> _logger;

    public ClientBotHandler(
        TelegramService telegram,
        ClientBookingWizard wizard,
        TelegramStateRepository states,
        BookingRepository bookings,
        BookingService bookingService,
        MasterRepository masters,
        ScheduleService schedule,
        SettingsRepository settings,
        IConfiguration configuration,
        ILogger<ClientBotHandler> logger)
    {
        _telegram = telegram;
        _wizard = wizard;
        _states = states;
        _bookings = bookings;
        _bookingService = bookingService;
        _masters = masters;
        _schedule = schedule;
        _settings = settings;
        _configuration = configuration;
        _logger = logger;
    }

    public IReadOnlyList<(string Command, string Description)> Commands => new[]
    {
        ("book", "Записаться"),
        ("my", "Мои записи"),
        ("site", "Сайт студии"),
        ("help", "Помощь")
    };

    // ================= Текстовые команды =================

    public async Task HandleMessageAsync(
        TelegramBot bot, long chatId, string text, CancellationToken cancellationToken)
    {
        // Шаг новой записи ждёт имя или телефон обычным текстом, поэтому проверяем его раньше команд.
        if (!text.StartsWith('/') && await _wizard.TryContinueWithTextAsync(bot, chatId, text, cancellationToken))
            return;

        if (text.StartsWith("/start", StringComparison.OrdinalIgnoreCase))
        {
            await StartAsync(bot, chatId, text, cancellationToken);
            return;
        }

        if (text.StartsWith("/my", StringComparison.OrdinalIgnoreCase))
        {
            await SendBookingsAsync(bot, chatId, cancellationToken);
            return;
        }

        if (text.StartsWith("/book", StringComparison.OrdinalIgnoreCase))
        {
            await _wizard.StartAsync(bot, chatId, cancellationToken);
            return;
        }

        if (text.StartsWith("/site", StringComparison.OrdinalIgnoreCase))
        {
            await SendSiteAsync(bot, chatId, cancellationToken);
            return;
        }

        if (text.StartsWith("/help", StringComparison.OrdinalIgnoreCase))
        {
            await bot.SendMessageAsync(chatId.ToString(),
                "Что умеет этот бот:\n\n" +
                "/my — мои записи: посмотреть, перенести, отменить\n" +
                "/book — записаться\n" +
                "/site — сайт студии: услуги, цены, работы, отзывы\n\n" +
                "Переносить и отменять можно прямо здесь, кнопками — заходить на сайт не нужно.",
                WelcomeButtons(), cancellationToken);
            return;
        }

        await bot.SendMessageAsync(chatId.ToString(),
            "Не понял команду. Список команд — /help.", null, cancellationToken);
    }

    private async Task StartAsync(TelegramBot bot, long chatId, string text, CancellationToken cancellationToken)
    {
        var parts = text.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length < 2)
        {
            // Чат не привязан — значит человек открыл бота сам, без ссылки с кодом, и бот
            // не может написать ему первым: подтверждения и напоминания до него не доходят,
            // а он об этом не знает. Просим номер — это единственный способ связать чат
            // с карточкой клиента в этом случае.
            if (!_bookings.IsChatBound(chatId.ToString()))
            {
                await bot.SendMessageAsync(chatId.ToString(),
                    "Здравствуйте! Это бот студии ELORA.\n\n" +
                    "Чтобы я присылал подтверждения записей и напоминал о визите, привяжите чат: " +
                    "нажмите кнопку ниже и отправьте свой номер телефона — тот же, что указывали " +
                    "при записи.",
                    new[] { new[] { TelegramButton.Contact("📱 Отправить мой номер") } },
                    cancellationToken);
                return;
            }

            await bot.SendMessageAsync(chatId.ToString(),
                "Здравствуйте! Это бот студии ELORA.\n\n" +
                "Здесь видно ваши записи, можно перенести или отменить визит — кнопками, не заходя " +
                "на сайт. Разделы сайта — кнопкой «🌐 Сайт студии» ниже.",
                WelcomeButtons(), cancellationToken);
            return;
        }

        var code = parts[1].Trim();
        var link = _bookings.ConsumeLinkCode(code);

        if (link is null)
        {
            await bot.SendMessageAsync(chatId.ToString(),
                "Ссылка уже использована или устарела. Оформите новую запись на сайте ELORA, " +
                "и я пришлю новую ссылку для подключения.", null, cancellationToken);
            return;
        }

        _bookings.SetClientTelegram(link.Value.ClientId, chatId.ToString());

        await bot.SendMessageAsync(chatId.ToString(),
            "✅ Telegram подключён!\n\n" +
            "Напомню о визите примерно за сутки. Перенести или отменить запись можно здесь же — команда /my.",
            WelcomeButtons(), cancellationToken);

        // Сразу показываем ту запись, ради которой человек перешёл по ссылке. Раньше в ответ
        // приходило только «Telegram подключён!», а саму запись нужно было запрашивать
        // командой /my — со стороны это выглядело так, будто подтверждение не пришло.
        // Код привязки помнит BookingId, поэтому искать запись по чату не нужно.
        if (link.Value.BookingId is int bookingId && _bookings.GetById(bookingId) is { } booked)
            await bot.SendMessageAsync(chatId.ToString(), CardText(booked),
                TelegramService.ClientBookingButtons(booked), cancellationToken);
    }

    /// <summary>
    /// Человек нажал «отправить мой номер». Привязываем чат к его карточке и сразу
    /// показываем записи: без этого он нажал кнопку и не увидел никакого результата.
    /// </summary>
    public async Task HandleContactAsync(
        TelegramBot bot, long chatId, string phone, string name, CancellationToken cancellationToken)
    {
        _bookings.BindChatByPhone(phone, chatId.ToString(), name);

        await bot.SendMessageAsync(chatId.ToString(),
            "✅ Готово, чат привязан. Теперь подтверждение записи и напоминание о визите придут сюда.",
            WelcomeButtons(), cancellationToken);

        if (_bookings.GetByClientTelegram(chatId.ToString()).Count > 0)
            await SendBookingsAsync(bot, chatId, cancellationToken);
    }

    internal static IReadOnlyList<IReadOnlyList<TelegramButton>> WelcomeButtons() => new[]
    {
        new[]
        {
            TelegramButton.Callback("📅 Мои записи", TelegramActions.MyBookings),
            TelegramButton.Callback("✍️ Записаться", TelegramActions.BookStart)
        },
        new[] { TelegramButton.Callback("🌐 Сайт студии", TelegramActions.Site) }
    };

    /// <summary>
    /// Подменю «Сайт»: разделы открываются кнопками-ссылками прямо из чата.
    /// </summary>
    /// <remarks>
    /// Кнопкой меню бота (слева от поля ввода) сайт не ставим намеренно: она уводит человека
    /// из чата, так и не нажав Start, — а без Start Telegram не даёт боту написать первым,
    /// и подтверждение записи до клиента не доходит. Ссылка внутри сообщения открывает
    /// браузер, оставляя чат на месте.
    /// </remarks>
    private async Task SendSiteAsync(TelegramBot bot, long chatId, CancellationToken cancellationToken)
    {
        var site = _configuration["Site:PublicUrl"]?.TrimEnd('/');

        if (string.IsNullOrWhiteSpace(site))
        {
            await bot.SendMessageAsync(chatId.ToString(),
                "Адрес сайта пока не задан — его вписывают в настройках панели.",
                WelcomeButtons(), cancellationToken);
            return;
        }

        var address = _settings.Get("Site.Address", "");
        var phone = _settings.Get("Site.Phone", "");

        var text = "🌐 <b>Сайт ELORA</b>\n\n" +
                   "Выберите раздел — он откроется в браузере, а этот чат останется на месте.";
        if (!string.IsNullOrWhiteSpace(address)) text += $"\n\nАдрес: {TelegramService.Escape(address)}";
        if (!string.IsNullOrWhiteSpace(phone)) text += $"\nТелефон: {TelegramService.PhoneLink(phone)}";

        var rows = SiteButtons(site);

        await bot.SendMessageAsync(chatId.ToString(), text, rows, cancellationToken);
    }

    /// <summary>
    /// Кнопки подменю «Сайт»: каждый раздел — ссылка, а не действие бота.
    /// </summary>
    /// <remarks>
    /// <c>internal</c> ради стенда разработки: в живом чате неверно названное поле кнопки
    /// выглядит просто как «кнопки нет», поэтому форму проверяем по JSON до отправки.
    /// </remarks>
    internal static IReadOnlyList<IReadOnlyList<TelegramButton>> SiteButtons(string site) => new[]
    {
        new[] { TelegramButton.Link("✍️ Записаться онлайн", $"{site}/booking") },
        new[]
        {
            TelegramButton.Link("💅 Услуги", $"{site}/services"),
            TelegramButton.Link("💰 Прайс", $"{site}/price")
        },
        new[]
        {
            TelegramButton.Link("🖼 Наши работы", $"{site}/works"),
            // Отзывы живут не отдельной страницей, а разделом на главной.
            TelegramButton.Link("💬 Отзывы", $"{site}/#reviews")
        },
        new[]
        {
            TelegramButton.Link("📞 Контакты", $"{site}/contacts"),
            TelegramButton.Link("❓ Вопросы", $"{site}/faq")
        },
        new[]
        {
            TelegramButton.Link("🏠 Главная", site),
            TelegramButton.Callback("📅 Мои записи", TelegramActions.MyBookings)
        }
    };

    /// <summary>Присылает по одному сообщению на каждую ближайшую запись — у каждой свои кнопки.</summary>
    private async Task SendBookingsAsync(TelegramBot bot, long chatId, CancellationToken cancellationToken)
    {
        var all = _bookings.GetByClientTelegram(chatId.ToString());
        var active = all
            .Where(b => b.IsActiveStatus && b.StartLocal >= DateTime.Now.AddHours(-2))
            .OrderBy(b => b.StartLocal)
            .Take(MaxBookingsInList)
            .ToList();

        if (active.Count == 0)
        {
            await bot.SendMessageAsync(chatId.ToString(),
                "Активных записей нет.\n\nЗаписаться можно командой /book.",
                WelcomeButtons(), cancellationToken);
            return;
        }

        await bot.SendMessageAsync(chatId.ToString(),
            active.Count == 1 ? "Ваша запись:" : $"Ваши записи ({active.Count}):",
            null, cancellationToken);

        foreach (var booking in active)
            await bot.SendMessageAsync(chatId.ToString(), CardText(booking),
                TelegramService.ClientBookingButtons(booking), cancellationToken);
    }

    // ================= Нажатия кнопок =================

    public async Task HandleCallbackAsync(
        TelegramBot bot, string callbackId, long chatId, int messageId, string data,
        CancellationToken cancellationToken)
    {
        var (action, payload) = ClientBotHandler.Split(data);

        if (action == TelegramActions.MyBookings)
        {
            await bot.AnswerCallbackAsync(callbackId, null, cancellationToken);
            await SendBookingsAsync(bot, chatId, cancellationToken);
            return;
        }

        if (action == TelegramActions.Site)
        {
            await bot.AnswerCallbackAsync(callbackId, null, cancellationToken);
            await SendSiteAsync(bot, chatId, cancellationToken);
            return;
        }

        // Шаги новой записи — в своём обработчике: у них длинный диалог с вводом текста.
        if (await _wizard.TryHandleCallbackAsync(bot, callbackId, chatId, messageId, action, payload, cancellationToken))
            return;

        // Шаги переноса: идентификатор записи берём из состояния, в кнопке его нет.
        if (IsMoveStep(action))
        {
            await HandleMoveStepAsync(bot, callbackId, chatId, messageId, action, payload, cancellationToken);
            return;
        }

        // Всё остальное относится к конкретной записи — сразу проверяем, что она своя.
        if (!int.TryParse(payload, out var bookingId))
        {
            await bot.AnswerCallbackAsync(callbackId, "Не понял команду", cancellationToken);
            return;
        }

        var booking = GetOwnedBooking(chatId, bookingId);
        if (booking is null)
        {
            await bot.AnswerCallbackAsync(callbackId, "Запись не найдена", cancellationToken);
            return;
        }

        switch (action)
        {
            case TelegramActions.Card:
                await bot.AnswerCallbackAsync(callbackId, null, cancellationToken);
                await ShowCardAsync(bot, chatId, messageId, booking, cancellationToken);
                break;

            case TelegramActions.Details:
                await bot.AnswerCallbackAsync(callbackId, null, cancellationToken);
                await ShowDetailsAsync(bot, chatId, messageId, booking, cancellationToken);
                break;

            case TelegramActions.Reschedule:
                await bot.AnswerCallbackAsync(callbackId, null, cancellationToken);
                await StartMoveAsync(bot, chatId, messageId, booking, cancellationToken);
                break;

            case TelegramActions.Cancel:
                await bot.AnswerCallbackAsync(callbackId, null, cancellationToken);
                await AskCancelAsync(bot, chatId, messageId, booking, cancellationToken);
                break;

            case TelegramActions.CancelYes:
                await ConfirmCancelAsync(bot, callbackId, chatId, messageId, booking, cancellationToken);
                break;

            case TelegramActions.CancelNo:
                _states.Clear(TelegramRole.Client, chatId);
                await bot.AnswerCallbackAsync(callbackId, "Запись оставлена", cancellationToken);
                await ShowCardAsync(bot, chatId, messageId, booking, cancellationToken);
                break;

            default:
                await bot.AnswerCallbackAsync(callbackId, "Кнопка устарела", cancellationToken);
                break;
        }
    }

    private static bool IsMoveStep(string action) =>
        action is TelegramActions.MoveMaster or TelegramActions.MoveDate or TelegramActions.MovePage
            or TelegramActions.MoveTime or TelegramActions.MoveApply or TelegramActions.MoveBack;

    private async Task HandleMoveStepAsync(
        TelegramBot bot, string callbackId, long chatId, int messageId,
        string action, string payload, CancellationToken cancellationToken)
    {
        var state = _states.Get(TelegramRole.Client, chatId);
        if (state?.BookingId is null)
        {
            await bot.AnswerCallbackAsync(callbackId, "Шаг устарел, откройте /my", cancellationToken);
            return;
        }

        var booking = GetOwnedBooking(chatId, state.BookingId.Value);
        if (booking is null)
        {
            await bot.AnswerCallbackAsync(callbackId, "Запись не найдена", cancellationToken);
            _states.Clear(TelegramRole.Client, chatId);
            return;
        }

        if (!booking.IsActiveStatus)
        {
            await bot.AnswerCallbackAsync(callbackId, "Эту запись уже нельзя перенести", cancellationToken);
            _states.Clear(TelegramRole.Client, chatId);
            await ShowCardAsync(bot, chatId, messageId, booking, cancellationToken);
            return;
        }

        var masterId = state.MasterId ?? booking.MasterId;

        switch (action)
        {
            case TelegramActions.MoveMaster:
                if (int.TryParse(payload, out var chosenMaster) && chosenMaster > 0)
                    await ShowMoveDatesAsync(bot, chatId, messageId, booking, chosenMaster, 0, cancellationToken);
                break;

            case TelegramActions.MovePage:
                if (int.TryParse(payload, out var page))
                    await ShowMoveDatesAsync(bot, chatId, messageId, booking, masterId, page, cancellationToken);
                break;

            case TelegramActions.MoveDate:
                await ShowMoveSlotsAsync(bot, callbackId, chatId, messageId, booking, masterId, payload, null, cancellationToken);
                break;

            case TelegramActions.MoveTime:
                await bot.AnswerCallbackAsync(callbackId, null, cancellationToken);
                await ShowMoveConfirmAsync(bot, chatId, messageId, booking, masterId,
                    state.Get("date") ?? "", payload, cancellationToken);
                break;

            case TelegramActions.MoveApply:
                await ApplyMoveAsync(bot, callbackId, chatId, messageId, booking, state, cancellationToken);
                break;

            case TelegramActions.MoveBack:
                await bot.AnswerCallbackAsync(callbackId, null, cancellationToken);
                await GoBackAsync(bot, chatId, messageId, booking, state, cancellationToken);
                break;
        }
    }

    /// <summary>Шаг назад внутри мастера переноса: с подтверждения — к времени, со времени — к датам.</summary>
    private async Task GoBackAsync(
        TelegramBot bot, long chatId, int messageId, Booking booking, TelegramState state,
        CancellationToken cancellationToken)
    {
        var masterId = state.MasterId ?? booking.MasterId;

        if (state.Step == TelegramSteps.MoveConfirm)
        {
            await ShowMoveSlotsAsync(bot, "", chatId, messageId, booking, masterId,
                state.Get("date") ?? "", null, cancellationToken);
            return;
        }

        var index = DateOnly.TryParse(state.Get("date"), out var date)
            ? _schedule.GetAvailableDates(masterId, booking.ServiceId).IndexOf(date)
            : -1;

        await ShowMoveDatesAsync(bot, chatId, messageId, booking, masterId,
            index >= 0 ? index / DatesPerPage : 0, cancellationToken);
    }

    // ================= Экраны =================

    private async Task ShowCardAsync(
        TelegramBot bot, long chatId, int messageId, Booking booking, CancellationToken cancellationToken)
    {
        var keyboard = booking.IsActiveStatus ? TelegramService.ClientBookingButtons(booking) : null;
        await bot.EditMessageTextAsync(chatId.ToString(), messageId, CardText(booking), keyboard,
            clearKeyboard: keyboard is null, cancellationToken);
    }

    private async Task ShowDetailsAsync(
        TelegramBot bot, long chatId, int messageId, Booking booking, CancellationToken cancellationToken)
    {
        var address = _settings.Get("Site.Address", "");
        var phone = _settings.Get("Site.Phone", "");

        var text = $"<b>{TelegramService.Escape(booking.ServiceName)}</b>\n" +
                   $"Мастер: {TelegramService.Escape(booking.MasterName)}\n" +
                   $"Дата: {booking.StartLocal:dd.MM.yyyy}\n" +
                   $"Время: {booking.StartLocal:HH:mm}–{booking.EndLocal:HH:mm}\n" +
                   $"Длительность: {Money.Duration(booking.DurationMinutes)}\n" +
                   $"Статус: {BookingStatuses.Title(booking.Status)}\n" +
                   $"Стоимость: {Money.Format(booking.Price)}";

        if (!string.IsNullOrWhiteSpace(address)) text += $"\n\nАдрес: {TelegramService.Escape(address)}";
        if (!string.IsNullOrWhiteSpace(phone)) text += $"\nТелефон: {TelegramService.PhoneLink(phone)}";
        if (!string.IsNullOrWhiteSpace(booking.Comment))
            text += $"\n\nВаш комментарий: {TelegramService.Escape(booking.Comment!)}";

        var rows = new List<IReadOnlyList<TelegramButton>>();
        if (booking.IsActiveStatus)
        {
            rows.Add(new[]
            {
                TelegramButton.Callback("🕒 Перенести", $"{TelegramActions.Reschedule}:{booking.Id}"),
                TelegramButton.Callback("❌ Отменить", $"{TelegramActions.Cancel}:{booking.Id}")
            });
        }
        rows.Add(new[] { TelegramButton.Callback("← К записи", $"{TelegramActions.Card}:{booking.Id}") });

        await bot.EditMessageTextAsync(chatId.ToString(), messageId, text, rows, cancellationToken: cancellationToken);
    }

    private async Task StartMoveAsync(
        TelegramBot bot, long chatId, int messageId, Booking booking, CancellationToken cancellationToken)
    {
        var masters = _masters.GetForService(booking.ServiceId);

        if (masters.Count == 0)
        {
            await bot.EditMessageTextAsync(chatId.ToString(), messageId,
                "Для этой услуги сейчас нет доступных мастеров. Позвоните нам, пожалуйста.",
                new[] { new[] { TelegramButton.Callback("← К записи", $"{TelegramActions.Card}:{booking.Id}") } },
                cancellationToken: cancellationToken);
            return;
        }

        // Один мастер — спрашивать нечего, сразу переходим к дате.
        if (masters.Count == 1)
        {
            await ShowMoveDatesAsync(bot, chatId, messageId, booking, masters[0].Id, 0, cancellationToken);
            return;
        }

        _states.Save(TelegramRole.Client, new TelegramState
        {
            ChatId = chatId,
            Step = TelegramSteps.MoveMaster,
            BookingId = booking.Id,
            MasterId = booking.MasterId
        });

        var rows = masters
            .Select(m => (IReadOnlyList<TelegramButton>)new[]
            {
                TelegramButton.Callback(m.Name, $"{TelegramActions.MoveMaster}:{m.Id}")
            })
            .ToList();

        rows.Add(new[] { TelegramButton.Callback("← Отмена", $"{TelegramActions.Card}:{booking.Id}") });

        await bot.EditMessageTextAsync(chatId.ToString(), messageId,
            "🕒 <b>Перенос записи</b>\n\n" +
            $"Сейчас: {booking.StartLocal:dd.MM.yyyy}, {booking.StartLocal:HH:mm}\n\n" +
            "Выберите мастера:",
            rows, cancellationToken: cancellationToken);
    }

    private async Task ShowMoveDatesAsync(
        TelegramBot bot, long chatId, int messageId, Booking booking, int masterId, int page,
        CancellationToken cancellationToken)
    {
        var dates = _schedule.GetAvailableDates(masterId, booking.ServiceId);
        if (dates.Count == 0)
        {
            await bot.EditMessageTextAsync(chatId.ToString(), messageId,
                "На ближайшие два месяца свободных дней нет. Позвоните нам, пожалуйста.",
                new[] { new[] { TelegramButton.Callback("← К записи", $"{TelegramActions.Card}:{booking.Id}") } },
                cancellationToken: cancellationToken);
            return;
        }

        _states.Save(TelegramRole.Client, new TelegramState
        {
            ChatId = chatId,
            Step = TelegramSteps.MoveDate,
            BookingId = booking.Id,
            MasterId = masterId
        });

        var pageCount = (int)Math.Ceiling(dates.Count / (double)DatesPerPage);
        page = Math.Clamp(page, 0, pageCount - 1);
        var slice = dates.Skip(page * DatesPerPage).Take(DatesPerPage).ToList();

        var rows = new List<IReadOnlyList<TelegramButton>>
        {
            slice.Select(d => TelegramButton.Callback(
                    $"{d.Day:00}.{d.Month:00} {Money.WeekdayShort(d.DayOfWeek)}",
                    $"{TelegramActions.MoveDate}:{d:yyyy-MM-dd}"))
                .ToArray()
        };

        if (pageCount > 1)
        {
            var nav = new List<TelegramButton>();
            if (page > 0) nav.Add(TelegramButton.Callback("‹", $"{TelegramActions.MovePage}:{page - 1}"));
            nav.Add(TelegramButton.Callback($"{page + 1} из {pageCount}", $"{TelegramActions.MovePage}:{page}"));
            if (page < pageCount - 1) nav.Add(TelegramButton.Callback("›", $"{TelegramActions.MovePage}:{page + 1}"));
            rows.Add(nav);
        }

        rows.Add(new[] { TelegramButton.Callback("← Отмена", $"{TelegramActions.Card}:{booking.Id}") });

        await bot.EditMessageTextAsync(chatId.ToString(), messageId,
            "🕒 <b>Перенос записи</b>\n\n" +
            $"Мастер: {TelegramService.Escape(MasterName(masterId, booking))}\n" +
            "Выберите новый день:",
            rows, cancellationToken: cancellationToken);
    }

    private async Task ShowMoveSlotsAsync(
        TelegramBot bot, string callbackId, long chatId, int messageId, Booking booking,
        int masterId, string date, string? problem, CancellationToken cancellationToken)
    {
        if (!DateOnly.TryParse(date, out var parsedDate))
        {
            if (!string.IsNullOrEmpty(callbackId))
                await bot.AnswerCallbackAsync(callbackId, "Дата непонятна", cancellationToken);
            return;
        }

        var slots = _schedule.GetSlots(booking.ServiceId, masterId, parsedDate);

        if (!slots.Ok || slots.Slots.Count == 0)
        {
            var reason = slots.Error ?? "На этот день всё занято.";
            if (!string.IsNullOrEmpty(callbackId)) await bot.AnswerCallbackAsync(callbackId, reason, cancellationToken);

            // Возвращаем к выбору дня: держать человека на пустом экране нельзя.
            var index = _schedule.GetAvailableDates(masterId, booking.ServiceId).IndexOf(parsedDate);
            await ShowMoveDatesAsync(bot, chatId, messageId, booking, masterId,
                index >= 0 ? index / DatesPerPage : 0, cancellationToken);
            return;
        }

        _states.Save(TelegramRole.Client, new TelegramState
        {
            ChatId = chatId,
            Step = TelegramSteps.MoveTime,
            BookingId = booking.Id,
            MasterId = masterId
        }.With("date", date));

        var rows = Chunk(slots.Slots
                .Select(s => TelegramButton.Callback(s.Time, $"{TelegramActions.MoveTime}:{s.Time}")), SlotsPerRow)
            .ToList();

        rows.Add(new[]
        {
            TelegramButton.Callback("← К датам", TelegramActions.MoveBack),
            TelegramButton.Callback("Отмена", $"{TelegramActions.Card}:{booking.Id}")
        });

        var header = problem is null ? "" : $"{problem}\n\n";

        await bot.EditMessageTextAsync(chatId.ToString(), messageId,
            $"{header}🕒 <b>Перенос записи</b>\n\n" +
            $"Мастер: {TelegramService.Escape(MasterName(masterId, booking))}\n" +
            $"День: {Money.DateWithWeekday(parsedDate.ToDateTime(TimeOnly.MinValue))}\n" +
            "Выберите время:",
            rows, cancellationToken: cancellationToken);
    }

    private async Task ShowMoveConfirmAsync(
        TelegramBot bot, long chatId, int messageId, Booking booking, int masterId, string date, string time,
        CancellationToken cancellationToken)
    {
        if (!DateOnly.TryParse(date, out _)) return;

        _states.Save(TelegramRole.Client, new TelegramState
        {
            ChatId = chatId,
            Step = TelegramSteps.MoveConfirm,
            BookingId = booking.Id,
            MasterId = masterId
        }.With("date", date).With("time", time));

        var rows = new List<IReadOnlyList<TelegramButton>>
        {
            new[] { TelegramButton.Callback("✅ Перенести", TelegramActions.MoveApply) },
            new[] { TelegramButton.Callback("← Назад", TelegramActions.MoveBack) }
        };

        await bot.EditMessageTextAsync(chatId.ToString(), messageId,
            "🕒 <b>Перенести запись?</b>\n\n" +
            $"{TelegramService.Escape(booking.ServiceName)}\n" +
            $"Мастер: {TelegramService.Escape(MasterName(masterId, booking))}\n\n" +
            $"Было: {booking.StartLocal:dd.MM.yyyy}, {booking.StartLocal:HH:mm}\n" +
            $"Станет: {Money.DateWithWeekday(DateOnly.Parse(date).ToDateTime(TimeOnly.MinValue))}, {time}",
            rows, cancellationToken: cancellationToken);
    }

    private async Task ApplyMoveAsync(
        TelegramBot bot, string callbackId, long chatId, int messageId, Booking booking,
        TelegramState state, CancellationToken cancellationToken)
    {
        var date = state.Get("date");
        var time = state.Get("time");
        var masterId = state.MasterId ?? booking.MasterId;

        if (date is null || time is null)
        {
            await bot.AnswerCallbackAsync(callbackId, "Шаг устарел, начните заново", cancellationToken);
            _states.Clear(TelegramRole.Client, chatId);
            await ShowCardAsync(bot, chatId, messageId, booking, cancellationToken);
            return;
        }

        // Перенос идёт через тот же сервис, что и на сайте: проверка занятости выполняется
        // в транзакции, поэтому занять чужой слот не получится.
        var result = await _bookingService.RescheduleAsync(
            booking.ManageToken, masterId, date, time, cancellationToken);

        if (!result.Ok)
        {
            await bot.AnswerCallbackAsync(callbackId, result.Error ?? "Не получилось перенести", cancellationToken);
            await ShowMoveSlotsAsync(bot, "", chatId, messageId, booking, masterId, date,
                "Это время только что заняли. Выберите другое.", cancellationToken);
            return;
        }

        _states.Clear(TelegramRole.Client, chatId);
        await bot.AnswerCallbackAsync(callbackId, "Готово", cancellationToken);

        var updated = _bookings.GetById(booking.Id) ?? booking;
        await bot.EditMessageTextAsync(chatId.ToString(), messageId,
            $"✅ <b>Запись перенесена</b>\n\n{CardText(updated)}",
            TelegramService.ClientBookingButtons(updated), cancellationToken: cancellationToken);
    }

    private async Task AskCancelAsync(
        TelegramBot bot, long chatId, int messageId, Booking booking, CancellationToken cancellationToken)
    {
        _states.Save(TelegramRole.Client, new TelegramState
        {
            ChatId = chatId,
            Step = TelegramSteps.CancelConfirm,
            BookingId = booking.Id
        });

        // Отмена необратима, поэтому одним нажатием её не делаем.
        var rows = new[]
        {
            new[]
            {
                TelegramButton.Callback("✅ Да, отменить", $"{TelegramActions.CancelYes}:{booking.Id}"),
                TelegramButton.Callback("← Нет, оставить", $"{TelegramActions.CancelNo}:{booking.Id}")
            }
        };

        await bot.EditMessageTextAsync(chatId.ToString(), messageId,
            "❌ <b>Отменить запись?</b>\n\n" +
            $"{TelegramService.Escape(booking.ServiceName)} · {TelegramService.Escape(booking.MasterName)}\n" +
            $"{booking.StartLocal:dd.MM.yyyy}, {booking.StartLocal:HH:mm}–{booking.EndLocal:HH:mm}\n\n" +
            "Освободившееся время смогут занять другие. Вернуть запись потом не получится.",
            rows, cancellationToken: cancellationToken);
    }

    private async Task ConfirmCancelAsync(
        TelegramBot bot, string callbackId, long chatId, int messageId, Booking booking,
        CancellationToken cancellationToken)
    {
        var result = await _bookingService.CancelAsync(booking.ManageToken, false, cancellationToken);

        if (!result.Ok)
        {
            await bot.AnswerCallbackAsync(callbackId, result.Error ?? "Не получилось отменить", cancellationToken);
            await ShowCardAsync(bot, chatId, messageId, booking, cancellationToken);
            return;
        }

        _states.Clear(TelegramRole.Client, chatId);
        await bot.AnswerCallbackAsync(callbackId, "Запись отменена", cancellationToken);

        await bot.EditMessageTextAsync(chatId.ToString(), messageId,
            "❌ <b>Запись отменена</b>\n\n" +
            $"{TelegramService.Escape(booking.ServiceName)}\n" +
            $"{booking.StartLocal:dd.MM.yyyy}, {booking.StartLocal:HH:mm}\n\n" +
            "Будем рады видеть вас снова — записаться можно командой /book.",
            WelcomeButtons(), clearKeyboard: true, cancellationToken: cancellationToken);
    }

    // ================= Вспомогательное =================

    private string CardText(Booking booking) =>
        $"<b>{TelegramService.Escape(booking.ServiceName)}</b>\n" +
        $"Мастер: {TelegramService.Escape(booking.MasterName)}\n" +
        $"Когда: {booking.StartLocal:dd.MM.yyyy}, {booking.StartLocal:HH:mm}–{booking.EndLocal:HH:mm}\n" +
        $"Статус: {BookingStatuses.Title(booking.Status)}\n" +
        $"Стоимость: {Money.Format(booking.Price)}";

    private string MasterName(int masterId, Booking fallback)
    {
        var master = _masters.GetById(masterId);
        return master?.Name ?? fallback.MasterName;
    }

    /// <summary>Запись считается своей, только если чат привязан именно к её клиенту.</summary>
    private Booking? GetOwnedBooking(long chatId, int bookingId)
    {
        var booking = _bookings.GetById(bookingId);
        if (booking is null) return null;

        return string.Equals(booking.ClientTelegramChatId, chatId.ToString(), StringComparison.Ordinal)
            ? booking
            : null;
    }

    internal static (string Action, string Payload) Split(string data)
    {
        var separator = data.IndexOf(':');
        return separator <= 0
            ? (data, "")
            : (data[..separator], data[(separator + 1)..]);
    }

    internal static IEnumerable<IReadOnlyList<TelegramButton>> Chunk(
        IEnumerable<TelegramButton> buttons, int size)
    {
        var row = new List<TelegramButton>(size);
        foreach (var button in buttons)
        {
            row.Add(button);
            if (row.Count == size)
            {
                yield return row.ToArray();
                row.Clear();
            }
        }

        if (row.Count > 0) yield return row.ToArray();
    }
}
