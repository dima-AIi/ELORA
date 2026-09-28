using ELORA.Web.Data.Repositories;
using ELORA.Web.DTOs;
using ELORA.Web.Helpers;
using ELORA.Web.Models;
using ELORA.Web.Services;
using ELORA.Web.Services.Telegram;

namespace ELORA.Web.Background;

/// <summary>
/// Новая запись прямо в клиентском боте: услуга → мастер → день → время → имя → телефон →
/// комментарий → подтверждение.
/// </summary>
/// <remarks>
/// Запись создаётся тем же <see cref="BookingService"/>, что и на сайте: проверка занятости
/// идёт в транзакции SQLite, поэтому бот не может занять чужой слот.
/// Все шаги правят одно и то же сообщение — его идентификатор лежит в состоянии (<c>msg</c>).
/// </remarks>
public sealed class ClientBookingWizard
{
    private const int DatesPerPage = 3;
    private const int SlotsPerRow = 3;

    private readonly TelegramStateRepository _states;
    private readonly CatalogRepository _catalog;
    private readonly MasterRepository _masters;
    private readonly BookingRepository _bookings;
    private readonly BookingService _bookingService;
    private readonly ScheduleService _schedule;
    private readonly ILogger<ClientBookingWizard> _logger;

    public ClientBookingWizard(
        TelegramStateRepository states,
        CatalogRepository catalog,
        MasterRepository masters,
        BookingRepository bookings,
        BookingService bookingService,
        ScheduleService schedule,
        ILogger<ClientBookingWizard> logger)
    {
        _states = states;
        _catalog = catalog;
        _masters = masters;
        _bookings = bookings;
        _bookingService = bookingService;
        _schedule = schedule;
        _logger = logger;
    }

    // ================= Старт и текстовые шаги =================

    public async Task StartAsync(
        TelegramBot bot, long chatId, CancellationToken cancellationToken, int? messageId = null)
    {
        var state = new TelegramState { ChatId = chatId, Step = TelegramSteps.BookCategory };
        if (messageId is > 0) state.With("msg", messageId.Value.ToString());

        await ShowCategoriesAsync(bot, state, cancellationToken);
    }

    /// <summary>
    /// Принимает обычный текст, если мастер ждёт имя, телефон или комментарий.
    /// Возвращает false, если диалог сейчас на другом шаге.
    /// </summary>
    public async Task<bool> TryContinueWithTextAsync(
        TelegramBot bot, long chatId, string text, CancellationToken cancellationToken)
    {
        var state = _states.Get(TelegramRole.Client, chatId);
        if (state is null) return false;

        switch (state.Step)
        {
            case TelegramSteps.BookName:
                if (text.Trim().Length < 2)
                {
                    await RenderAsync(bot, state, "Имя слишком короткое. Напишите, пожалуйста, как к вам обращаться:",
                        null, cancellationToken);
                    return true;
                }

                state.With("name", text.Trim());
                await ShowPhoneAsync(bot, state, cancellationToken);
                return true;

            case TelegramSteps.BookPhone:
                if (text.Count(char.IsDigit) < 10)
                {
                    await RenderAsync(bot, state, "Не похоже на номер телефона. Напишите в формате +7 900 000-00-00:",
                        null, cancellationToken);
                    return true;
                }

                state.With("phone", text.Trim());
                await ShowCommentAsync(bot, state, cancellationToken);
                return true;

            case TelegramSteps.BookComment:
                state.With("comment", text.Trim());
                await ShowConfirmAsync(bot, state, cancellationToken);
                return true;

            default:
                return false;
        }
    }

    // ================= Кнопочные шаги =================

    /// <summary>Возвращает true, если действие относилось к мастеру записи.</summary>
    public async Task<bool> TryHandleCallbackAsync(
        TelegramBot bot, string callbackId, long chatId, int messageId,
        string action, string payload, CancellationToken cancellationToken)
    {
        if (!action.StartsWith("bk-", StringComparison.Ordinal)) return false;

        var state = _states.Get(TelegramRole.Client, chatId);

        if (action == TelegramActions.BookStart)
        {
            await bot.AnswerCallbackAsync(callbackId, null, cancellationToken);
            await StartAsync(bot, chatId, cancellationToken, messageId);
            return true;
        }

        if (state is null || !state.Step.StartsWith("book-", StringComparison.Ordinal))
        {
            await bot.AnswerCallbackAsync(callbackId, "Шаг устарел, начните заново: /book", cancellationToken);
            return true;
        }

        // Сообщение могло быть отправлено заново после ввода текста — запоминаем актуальный id.
        state.With("msg", messageId.ToString());

        switch (action)
        {
            case TelegramActions.BookCategory:
                if (int.TryParse(payload, out var categoryId))
                    await ShowServicesAsync(bot, state, categoryId, cancellationToken);
                break;

            case TelegramActions.BookService:
                if (int.TryParse(payload, out var serviceId))
                    await ShowMastersAsync(bot, state, serviceId, cancellationToken);
                break;

            case TelegramActions.BookMaster:
                if (int.TryParse(payload, out var masterId))
                    await ShowDatesAsync(bot, state, masterId, 0, cancellationToken);
                break;

            case TelegramActions.BookPage:
                if (int.TryParse(payload, out var page) && state.GetInt("masterId") is { } master)
                    await ShowDatesAsync(bot, state, master, page, cancellationToken);
                break;

            case TelegramActions.BookDate:
                await ShowSlotsAsync(bot, callbackId, state, payload, null, cancellationToken);
                break;

            case TelegramActions.BookTime:
                state.With("time", payload);
                await ShowNameAsync(bot, state, cancellationToken);
                break;

            case TelegramActions.BookSkip:
                state.With("comment", null);
                await ShowConfirmAsync(bot, state, cancellationToken);
                break;

            case TelegramActions.BookUsePhone:
                state.With("phone", payload);
                await ShowCommentAsync(bot, state, cancellationToken);
                break;

            case TelegramActions.BookCancel:
                _states.Clear(TelegramRole.Client, chatId);
                await bot.AnswerCallbackAsync(callbackId, "Отменено", cancellationToken);
                await bot.EditMessageTextAsync(chatId.ToString(), messageId,
                    "Запись отменена. Начать заново — команда /book.",
                    ClientBotHandler.WelcomeButtons(), cancellationToken: cancellationToken);
                break;

            case TelegramActions.BookBack:
                await GoBackAsync(bot, callbackId, state, cancellationToken);
                break;

            case TelegramActions.BookApply:
                await ApplyAsync(bot, callbackId, state, cancellationToken);
                break;

            default:
                await bot.AnswerCallbackAsync(callbackId, "Кнопка устарела", cancellationToken);
                break;
        }

        return true;
    }

    private async Task GoBackAsync(
        TelegramBot bot, string callbackId, TelegramState state, CancellationToken cancellationToken)
    {
        await bot.AnswerCallbackAsync(callbackId, null, cancellationToken);

        switch (state.Step)
        {
            case TelegramSteps.BookService:
                await ShowCategoriesAsync(bot, state, cancellationToken);
                break;

            case TelegramSteps.BookMaster:
                await ShowServicesAsync(bot, state, state.GetInt("categoryId") ?? 0, cancellationToken);
                break;

            case TelegramSteps.BookDate when state.GetInt("serviceId") is { } serviceId:
                await ShowMastersAsync(bot, state, serviceId, cancellationToken);
                break;

            case TelegramSteps.BookTime when state.GetInt("masterId") is { } masterId:
                await ShowDatesAsync(bot, state, masterId, 0, cancellationToken);
                break;

            case TelegramSteps.BookConfirm:
                await ShowCommentAsync(bot, state, cancellationToken);
                break;

            default:
                await ShowCategoriesAsync(bot, state, cancellationToken);
                break;
        }
    }

    // ================= Экраны =================

    private async Task ShowCategoriesAsync(TelegramBot bot, TelegramState state, CancellationToken cancellationToken)
    {
        var categories = _catalog.GetCategories().Where(c => c.IsActive).ToList();

        if (categories.Count == 0)
        {
            await RenderAsync(bot, state, "Каталог услуг пока пуст. Позвоните нам, пожалуйста.", null, cancellationToken);
            return;
        }

        state = NextStep(state, TelegramSteps.BookCategory);

        var rows = ClientBotHandler.Chunk(
                categories.Select(c => TelegramButton.Callback(c.Name, $"{TelegramActions.BookCategory}:{c.Id}")), 2)
            .ToList();
        rows.Add(new[] { TelegramButton.Callback("✖️ Отмена", TelegramActions.BookCancel) });

        await RenderAsync(bot, state, "✍️ <b>Новая запись</b>\n\nШаг 1 из 6. Выберите направление:",
            rows, cancellationToken);
    }

    private async Task ShowServicesAsync(
        TelegramBot bot, TelegramState state, int categoryId, CancellationToken cancellationToken)
    {
        var services = _catalog.GetServices().Where(s => s.CategoryId == categoryId).ToList();
        if (services.Count == 0)
        {
            await ShowCategoriesAsync(bot, state, cancellationToken);
            return;
        }

        state = NextStep(state, TelegramSteps.BookService).With("categoryId", categoryId.ToString());

        var rows = services
            .Select(s => (IReadOnlyList<TelegramButton>)new[]
            {
                TelegramButton.Callback($"{s.Name} · {Money.Format(s.Price)}",
                    $"{TelegramActions.BookService}:{s.Id}")
            })
            .ToList();

        rows.Add(BackAndCancel());

        await RenderAsync(bot, state, "✍️ <b>Новая запись</b>\n\nШаг 2 из 6. Выберите услугу:",
            rows, cancellationToken);
    }

    private async Task ShowMastersAsync(
        TelegramBot bot, TelegramState state, int serviceId, CancellationToken cancellationToken)
    {
        var service = _catalog.GetService(serviceId);
        var masters = _masters.GetForService(serviceId);

        if (service is null || masters.Count == 0)
        {
            await ShowCategoriesAsync(bot, state, cancellationToken);
            return;
        }

        state = NextStep(state, TelegramSteps.BookMaster)
            .With("serviceId", serviceId.ToString())
            .With("serviceName", service.Name)
            .With("price", service.Price.ToString());

        var rows = masters
            .Select(m => (IReadOnlyList<TelegramButton>)new[]
            {
                TelegramButton.Callback(m.Name, $"{TelegramActions.BookMaster}:{m.Id}")
            })
            .ToList();

        rows.Add(BackAndCancel());

        await RenderAsync(bot, state,
            $"✍️ <b>Новая запись</b>\n\nУслуга: {TelegramService.Escape(service.Name)}\n\nШаг 3 из 6. Выберите мастера:",
            rows, cancellationToken);
    }

    private async Task ShowDatesAsync(
        TelegramBot bot, TelegramState state, int masterId, int page, CancellationToken cancellationToken)
    {
        var serviceId = state.GetInt("serviceId") ?? 0;
        var dates = _schedule.GetAvailableDates(masterId, serviceId);

        if (dates.Count == 0)
        {
            await RenderAsync(bot, state,
                "На ближайшие два месяца свободных дней нет. Позвоните нам, пожалуйста.",
                new[] { BackAndCancel() }, cancellationToken);
            return;
        }

        state = NextStep(state, TelegramSteps.BookDate)
            .With("serviceId", serviceId.ToString())
            .With("masterId", masterId.ToString())
            .With("masterName", _masters.GetById(masterId)?.Name ?? "");

        var pageCount = (int)Math.Ceiling(dates.Count / (double)DatesPerPage);
        page = Math.Clamp(page, 0, pageCount - 1);
        var slice = dates.Skip(page * DatesPerPage).Take(DatesPerPage).ToList();

        var rows = new List<IReadOnlyList<TelegramButton>>
        {
            slice.Select(d => TelegramButton.Callback(
                    $"{d.Day:00}.{d.Month:00} {Money.WeekdayShort(d.DayOfWeek)}",
                    $"{TelegramActions.BookDate}:{d:yyyy-MM-dd}"))
                .ToArray()
        };

        if (pageCount > 1)
        {
            var nav = new List<TelegramButton>();
            if (page > 0) nav.Add(TelegramButton.Callback("‹", $"{TelegramActions.BookPage}:{page - 1}"));
            nav.Add(TelegramButton.Callback($"{page + 1} из {pageCount}", $"{TelegramActions.BookPage}:{page}"));
            if (page < pageCount - 1) nav.Add(TelegramButton.Callback("›", $"{TelegramActions.BookPage}:{page + 1}"));
            rows.Add(nav);
        }

        rows.Add(BackAndCancel());

        await RenderAsync(bot, state,
            $"✍️ <b>Новая запись</b>\n\nУслуга: {TelegramService.Escape(state.Get("serviceName") ?? "")}\n" +
            $"Мастер: {TelegramService.Escape(state.Get("masterName") ?? "")}\n\nШаг 4 из 6. Выберите день:",
            rows, cancellationToken);
    }

    private async Task ShowSlotsAsync(
        TelegramBot bot, string callbackId, TelegramState state, string date, string? problem,
        CancellationToken cancellationToken)
    {
        if (!DateOnly.TryParse(date, out var parsedDate)) return;

        var serviceId = state.GetInt("serviceId") ?? 0;
        var masterId = state.GetInt("masterId") ?? 0;
        var slots = _schedule.GetSlots(serviceId, masterId, parsedDate);

        if (!slots.Ok || slots.Slots.Count == 0)
        {
            await bot.AnswerCallbackAsync(callbackId, slots.Error ?? "На этот день всё занято", cancellationToken);
            var index = _schedule.GetAvailableDates(masterId, serviceId).IndexOf(parsedDate);
            await ShowDatesAsync(bot, state, masterId, index >= 0 ? index / DatesPerPage : 0, cancellationToken);
            return;
        }

        state = NextStep(state, TelegramSteps.BookTime).With("date", date);

        var rows = ClientBotHandler.Chunk(
                slots.Slots.Select(s => TelegramButton.Callback(s.Time, $"{TelegramActions.BookTime}:{s.Time}")),
                SlotsPerRow)
            .ToList();
        rows.Add(BackAndCancel());

        var header = problem is null ? "" : $"{problem}\n\n";

        await RenderAsync(bot, state,
            $"{header}✍️ <b>Новая запись</b>\n\n" +
            $"Услуга: {TelegramService.Escape(state.Get("serviceName") ?? "")}\n" +
            $"Мастер: {TelegramService.Escape(state.Get("masterName") ?? "")}\n" +
            $"День: {Money.DateWithWeekday(parsedDate.ToDateTime(TimeOnly.MinValue))}\n\n" +
            "Шаг 4 из 6. Выберите время:",
            rows, cancellationToken);
    }

    private async Task ShowNameAsync(TelegramBot bot, TelegramState state, CancellationToken cancellationToken)
    {
        state = NextStep(state, TelegramSteps.BookName);

        // Отправляем новое сообщение: дальше человек пишет текстом, и правка того же сообщения
        // сбила бы его с толку.
        state.With("msg", null);

        await RenderAsync(bot, state,
            $"✍️ <b>Новая запись</b>\n\n" +
            $"Услуга: {TelegramService.Escape(state.Get("serviceName") ?? "")}\n" +
            $"Мастер: {TelegramService.Escape(state.Get("masterName") ?? "")}\n" +
            $"Когда: {FormatDate(state.Get("date"))}, {state.Get("time")}\n\n" +
            "Шаг 5 из 6. Напишите, как к вам обращаться:",
            new[] { BackAndCancel() }, cancellationToken);
    }

    private async Task ShowPhoneAsync(TelegramBot bot, TelegramState state, CancellationToken cancellationToken)
    {
        state = NextStep(state, TelegramSteps.BookPhone);
        state.With("msg", null);

        var rows = new List<IReadOnlyList<TelegramButton>>();

        // Если телефон уже известен по прошлым записям — не заставляем вводить заново.
        var known = FindKnownPhone(state.ChatId);
        if (known is not null)
        {
            rows.Add(new[] { TelegramButton.Callback($"Использовать {known}", $"{TelegramActions.BookUsePhone}:{known}") });
            rows.Add(new[] { TelegramButton.Callback("← Назад", TelegramActions.BookBack) });
            await RenderAsync(bot, state,
                $"✍️ <b>Новая запись</b>\n\nИмя: {TelegramService.Escape(state.Get("name") ?? "")}\n\n" +
                "Шаг 6 из 6. Телефон для связи — можно взять прежний или написать другой:",
                rows, cancellationToken);
            return;
        }

        rows.Add(BackAndCancel());
        await RenderAsync(bot, state,
            $"✍️ <b>Новая запись</b>\n\nИмя: {TelegramService.Escape(state.Get("name") ?? "")}\n\n" +
            "Шаг 6 из 6. Напишите телефон для связи, например +7 900 000-00-00:",
            rows, cancellationToken);
    }

    private async Task ShowCommentAsync(TelegramBot bot, TelegramState state, CancellationToken cancellationToken)
    {
        state = NextStep(state, TelegramSteps.BookComment);
        state.With("msg", null);

        var rows = new[]
        {
            new[]
            {
                TelegramButton.Callback("Пропустить", TelegramActions.BookSkip),
                TelegramButton.Callback("✖️ Отмена", TelegramActions.BookCancel)
            }
        };

        await RenderAsync(bot, state,
            "Комментарий мастеру — по желанию. Напишите пожелания одним сообщением " +
            "(длина, форма, аллергии) или нажмите «Пропустить».",
            rows, cancellationToken);
    }

    private async Task ShowConfirmAsync(TelegramBot bot, TelegramState state, CancellationToken cancellationToken)
    {
        state = NextStep(state, TelegramSteps.BookConfirm);

        var serviceName = state.Get("serviceName") ?? "";
        var price = decimal.TryParse(state.Get("price"), out var parsed) ? parsed : 0m;

        var text = "✍️ <b>Проверьте запись</b>\n\n" +
                   $"Услуга: {TelegramService.Escape(serviceName)}\n" +
                   $"Мастер: {TelegramService.Escape(state.Get("masterName") ?? "")}\n" +
                   $"Когда: {FormatDate(state.Get("date"))}, {state.Get("time")}\n" +
                   $"Имя: {TelegramService.Escape(state.Get("name") ?? "")}\n" +
                   $"Телефон: {TelegramService.Escape(state.Get("phone") ?? "")}\n" +
                   $"Стоимость: {Money.Format(price)}";

        if (!string.IsNullOrWhiteSpace(state.Get("comment")))
            text += $"\nКомментарий: {TelegramService.Escape(state.Get("comment")!)}";

        var rows = new List<IReadOnlyList<TelegramButton>>
        {
            new[] { TelegramButton.Callback("✅ Записаться", TelegramActions.BookApply) },
            new[] { TelegramButton.Callback("← Назад", TelegramActions.BookBack) }
        };

        await RenderAsync(bot, state, text, rows, cancellationToken);
    }

    private async Task ApplyAsync(
        TelegramBot bot, string callbackId, TelegramState state, CancellationToken cancellationToken)
    {
        var serviceId = state.GetInt("serviceId") ?? 0;
        var masterId = state.GetInt("masterId") ?? 0;
        var date = state.Get("date");
        var time = state.Get("time");
        var name = state.Get("name");
        var phone = state.Get("phone");

        if (serviceId <= 0 || masterId <= 0 || date is null || time is null || name is null || phone is null)
        {
            await bot.AnswerCallbackAsync(callbackId, "Не хватает данных, начните заново: /book", cancellationToken);
            _states.Clear(TelegramRole.Client, state.ChatId);
            return;
        }

        var result = await _bookingService.CreateAsync(new BookingRequest
        {
            ServiceId = serviceId,
            MasterId = masterId,
            Date = date,
            Time = time,
            Name = name,
            Phone = phone,
            Comment = state.Get("comment"),
            Telegram = null
        }, cancellationToken, notifyClient: false);

        if (!result.Ok)
        {
            await bot.AnswerCallbackAsync(callbackId, result.Error ?? "Не получилось записать", cancellationToken);
            await ShowSlotsAsync(bot, "", state, date, "Это время только что заняли. Выберите другое.",
                cancellationToken);
            return;
        }

        // Идентификатор сообщения нужен и после очистки состояния — забираем заранее.
        var messageId = state.GetInt("msg") ?? 0;

        // Чат уже в боте, поэтому привязываем клиента сразу — без одноразового кода.
        var booking = _bookings.GetById(result.BookingId);
        if (booking is not null && string.IsNullOrWhiteSpace(booking.ClientTelegramChatId))
            _bookings.SetClientTelegram(booking.ClientId, state.ChatId.ToString());

        _states.Clear(TelegramRole.Client, state.ChatId);
        await bot.AnswerCallbackAsync(callbackId, "Запись создана", cancellationToken);

        var created = _bookings.GetById(result.BookingId) ?? booking;
        if (created is null || messageId == 0)
        {
            await bot.SendMessageAsync(state.ChatId.ToString(),
                "✅ Запись создана. Детали — в команде /my.",
                ClientBotHandler.WelcomeButtons(), cancellationToken);
            return;
        }

        await bot.EditMessageTextAsync(state.ChatId.ToString(), messageId,
            $"✅ <b>Вы записаны!</b>\n\n{CardText(created)}",
            TelegramService.ClientBookingButtons(created), cancellationToken: cancellationToken);
    }

    // ================= Вспомогательное =================

    /// <summary>
    /// Переводит диалог на следующий шаг, сохраняя всё уже выбранное: услугу, мастера, дату,
    /// время, имя, телефон и идентификатор сообщения, которое правим.
    /// </summary>
    private static TelegramState NextStep(TelegramState state, string step) => new()
    {
        ChatId = state.ChatId,
        Step = step,
        MasterId = state.GetInt("masterId") ?? state.MasterId,
        Data = new Dictionary<string, string>(state.Data, StringComparer.OrdinalIgnoreCase)
    };

    /// <summary>Одна строка кнопок: назад и отмена. Возвращается строка, а не вся клавиатура.</summary>
    private static IReadOnlyList<TelegramButton> BackAndCancel() => new[]
    {
        TelegramButton.Callback("← Назад", TelegramActions.BookBack),
        TelegramButton.Callback("✖️ Отмена", TelegramActions.BookCancel)
    };

    /// <summary>Пишет шаг в то же сообщение, а если его ещё нет — отправляет новое и запоминает id.</summary>
    private async Task RenderAsync(
        TelegramBot bot, TelegramState state, string text,
        IReadOnlyList<IReadOnlyList<TelegramButton>>? keyboard, CancellationToken cancellationToken)
    {
        var chatId = state.ChatId.ToString();
        var messageId = state.GetInt("msg");

        if (messageId is > 0)
        {
            await bot.EditMessageTextAsync(chatId, messageId.Value, text, keyboard, cancellationToken: cancellationToken);
            _states.Save(TelegramRole.Client, state);
            return;
        }

        var sent = await bot.SendMessageReturningIdAsync(chatId, text, keyboard, cancellationToken);
        if (sent is not null) state.With("msg", sent.Value.ToString());
        _states.Save(TelegramRole.Client, state);
    }

    private string? FindKnownPhone(long chatId)
    {
        var known = _bookings.GetByClientTelegram(chatId.ToString())
            .Where(b => !string.IsNullOrWhiteSpace(b.ClientPhone))
            .OrderByDescending(b => b.StartLocal)
            .Select(b => b.ClientPhone)
            .FirstOrDefault();

        return string.IsNullOrWhiteSpace(known) ? null : known;
    }

    private static string FormatDate(string? isoDate) =>
        DateOnly.TryParse(isoDate, out var date)
            ? Money.DateWithWeekday(date.ToDateTime(TimeOnly.MinValue))
            : isoDate ?? "";

    private string CardText(Booking booking) =>
        $"<b>{TelegramService.Escape(booking.ServiceName)}</b>\n" +
        $"Мастер: {TelegramService.Escape(booking.MasterName)}\n" +
        $"Когда: {booking.StartLocal:dd.MM.yyyy}, {booking.StartLocal:HH:mm}–{booking.EndLocal:HH:mm}\n" +
        $"Статус: {BookingStatuses.Title(booking.Status)}\n" +
        $"Стоимость: {Money.Format(booking.Price)}";
}
