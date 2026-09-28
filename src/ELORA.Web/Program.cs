using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using ELORA.Web.Background;
using ELORA.Web.Data;
using ELORA.Web.Data.Repositories;
using ELORA.Web.DTOs;
using ELORA.Web.Endpoints;
using ELORA.Web.Helpers;
using ELORA.Web.Middleware;
using ELORA.Web.Services;
using ELORA.Web.Services.Ai;
using ELORA.Web.Services.Telegram;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;

// ---------- Кодировка логов ----------
// Windows пишет кириллицу в кодировке консоли (CP866), и перенаправленный в файл
// лог превращается в кракозябры — читать его в редакторе невозможно. Явный UTF-8
// делает файл читаемым где угодно. Трогаем только перенаправленный вывод: в живой
// консоли кодировку задаёт пользователь, и ломать её не наше дело.
// На Linux (в том числе на хостинге) здесь уже UTF-8, то есть ничего не меняется.
if (Console.IsOutputRedirected)
{
    try
    {
        Console.OutputEncoding = Encoding.UTF8;
    }
    catch (IOException)
    {
        // Нет консоли — писать всё равно некуда, это не повод не запускаться.
    }
}

var builder = WebApplication.CreateBuilder(args);

// Сервер не представляется наружу. Заголовок Server сообщает точное имя и версию
// платформы — это подсказка тому, кто подбирает уязвимость под неё, и ничего больше.
builder.WebHost.ConfigureKestrel(options => options.AddServerHeader = false);

// ---------- Локальные секреты из .env ----------
// Ключ AI удобно держать рядом с проектом, а не в переменных оболочки.
// Файл .env в репозиторий не попадает (см. .gitignore), а его значения
// становятся переменными окружения процесса — их читает блок ниже.
// На хостинге файла нет: там секреты приходят штатными переменными.
// Ищем рядом с приложением и вверх по дереву: при запуске из bin/Debug/net10.0
// до .env в корне проекта несколько уровней.
LoadDotEnv(FindDotEnv(builder.Environment.ContentRootPath));

// ---------- Переменные окружения для AI-консультанта ----------
// Имена NVIDIA_API_KEY / NVIDIA_MODEL / NVIDIA_MODELS ожидаемы на любом хостинге,
// а секция Ai:* в appsettings остаётся точкой входа для User Secrets. Здесь они
// сводятся вместе, поэтому ключ достаточно положить в окружение — конфиг править не нужно.
{
    var environment = builder.Configuration.AddEnvironmentVariables();
    var providerKey = Environment.GetEnvironmentVariable("NVIDIA_API_KEY");
    var providerModel = Environment.GetEnvironmentVariable("NVIDIA_MODEL");
    var providerModels = Environment.GetEnvironmentVariable("NVIDIA_MODELS");

    var settings = new Dictionary<string, string?>();
    if (!string.IsNullOrWhiteSpace(providerKey)) settings["Ai:ApiKey"] = providerKey;
    if (!string.IsNullOrWhiteSpace(providerModel)) settings["Ai:Model"] = providerModel;

    // Цепочка моделей одной строкой через запятую: удобно для секретов хостинга,
    // где многострочные значения задавать неудобно.
    if (!string.IsNullOrWhiteSpace(providerModels))
    {
        var chain = providerModels.Split(
            ',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        for (var index = 0; index < chain.Length; index++)
            settings[$"Ai:Models:{index}"] = chain[index];
    }

    // AddInMemoryCollection перекрывает appsettings, но уступает User Secrets:
    // на машине разработчика ключ удобнее держать именно там.
    if (settings.Count > 0) environment.AddInMemoryCollection(settings);
}

// ---------- Порт и адрес прослушивания ----------
// На хостинге (Fly.io, Render, Railway) порт приходит в переменной окружения PORT,
// причём Kestrel по умолчанию слушает только localhost — тогда трафик снаружи до
// контейнера просто не доходит. Поэтому слушаем 0.0.0.0 и именно выданный порт.
// При локальном запуске PORT не задан, и продолжают работать --urls и launchSettings.
var hostingPort = Environment.GetEnvironmentVariable("PORT");
if (!string.IsNullOrWhiteSpace(hostingPort))
{
    builder.WebHost.UseUrls($"http://0.0.0.0:{hostingPort}");
}

// ---------- Заголовки обратного прокси ----------
// Хостинг терминирует HTTPS на своём прокси и передаёт настоящую схему в заголовке
// X-Forwarded-Proto. Без его обработки приложение считает, что работает по HTTP:
// это ломает проверку источника у antiforgery и признак Secure у cookie админки.
// Список доверенных прокси очищаем — адрес прокси хостинга заранее неизвестен.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

// ---------- Razor Pages + Cookie Authentication для админки ----------
var razorPages = builder.Services.AddRazorPages(options =>
{
    // Вся папка /Admin закрыта, открыт только вход.
    options.Conventions.AuthorizeFolder("/Admin");
    options.Conventions.AllowAnonymousToPage("/Admin/Login");
});

// В режиме разработки виды перечитываются с диска — правки видны сразу, без перезапуска.
if (builder.Environment.IsDevelopment())
{
    razorPages.AddRazorRuntimeCompilation();
}

builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "elora.admin";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.LoginPath = "/admin/login";
        options.LogoutPath = "/admin/logout";
        options.AccessDeniedPath = "/admin/login";
        options.ExpireTimeSpan = TimeSpan.FromHours(10);
        options.SlidingExpiration = true;
    });

builder.Services.AddAuthorization();

// ---------- Данные ----------
builder.Services.AddSingleton<SqliteConnectionFactory>();
builder.Services.AddSingleton<DatabaseInitializer>();
builder.Services.AddSingleton<CatalogRepository>();
builder.Services.AddSingleton<MasterRepository>();
builder.Services.AddSingleton<ScheduleRepository>();
builder.Services.AddSingleton<BookingRepository>();
builder.Services.AddSingleton<ClientRepository>();
builder.Services.AddSingleton<ContentRepository>();
builder.Services.AddSingleton<SettingsRepository>();
builder.Services.AddSingleton<AdminUserRepository>();
builder.Services.AddSingleton<TelegramStateRepository>();

// ---------- Бизнес-логика ----------
// Общий таймаут HttpClient здесь недопустим: длинный опрос Telegram держит соединение
// 25 секунд, и лимит обрывал бы запрос раньше ответа — бот переставал получать команды.
// Таймаут задаётся точечно внутри TelegramBot.
builder.Services.AddHttpClient("telegram", client => client.Timeout = Timeout.InfiniteTimeSpan);

builder.Services.AddSingleton<TelegramBots>();
builder.Services.AddSingleton<TelegramService>();
builder.Services.AddSingleton<ScheduleService>();
builder.Services.AddSingleton<BookingService>();
builder.Services.AddSingleton<WorksService>();
builder.Services.AddSingleton<AdminService>();
builder.Services.AddSingleton<ReminderService>();
builder.Services.AddSingleton<ClientReminderService>();

// ---------- AI-консультант ----------
// Ключ читается из конфигурации (User Secrets локально, переменные окружения на хостинге)
// и в браузер не попадает: с фронтом общается только серверный маршрут /api/chat.
builder.Services.Configure<AiOptions>(builder.Configuration.GetSection(AiOptions.SectionName));
builder.Services.AddHttpClient("ai", client => client.Timeout = Timeout.InfiniteTimeSpan);
builder.Services.AddSingleton<ChatRateLimiter>();
builder.Services.AddSingleton<EloraAssistant>();

// ---------- Боты: два обработчика и два цикла опроса ----------
builder.Services.AddSingleton<ClientBookingWizard>();
builder.Services.AddSingleton<ClientBotHandler>();
builder.Services.AddSingleton<AdminBotHandler>();

// ---------- Фоновые задачи ----------
// Уведомления и напоминания — вспомогательный канал. Падение фоновой службы не должно
// останавливать сайт: по умолчанию BackgroundServiceExceptionBehavior = StopHost, и одна
// ошибка Telegram (например, недоступный api.telegram.org) гасила весь хост.
builder.Services.Configure<HostOptions>(options =>
    options.BackgroundServiceExceptionBehavior = BackgroundServiceExceptionBehavior.Ignore);

builder.Services.AddHostedService<ReminderBackgroundService>();

// Резервные копии базы. Регистрируем как singleton и отдельно как hosted service —
// так ту же копию можно сделать кнопкой из админки, а не только по расписанию.
builder.Services.AddSingleton<DatabaseBackupService>();
builder.Services.AddHostedService(provider => provider.GetRequiredService<DatabaseBackupService>());

// По экземпляру опроса на бота: у них разные токены, разные меню команд и разные обработчики.
builder.Services.AddSingleton<IHostedService>(provider => new TelegramPollingService(
    provider.GetRequiredService<TelegramBots>().Client,
    provider.GetRequiredService<ClientBotHandler>(),
    provider.GetRequiredService<TelegramService>(),
    provider.GetRequiredService<IConfiguration>(),
    provider.GetRequiredService<ILogger<TelegramPollingService>>()));

builder.Services.AddSingleton<IHostedService>(provider => new TelegramPollingService(
    provider.GetRequiredService<TelegramBots>().Admin,
    provider.GetRequiredService<AdminBotHandler>(),
    provider.GetRequiredService<TelegramService>(),
    provider.GetRequiredService<IConfiguration>(),
    provider.GetRequiredService<ILogger<TelegramPollingService>>()));

var app = builder.Build();

// ---------- Инициализация базы: schema + seed ----------
using (var scope = app.Services.CreateScope())
{
    var initializer = scope.ServiceProvider.GetRequiredService<DatabaseInitializer>();
    initializer.Initialize();

    // Аварийный вход: если владелец задал ELORA_ADMIN_RESET_PASSWORD в панели, пароль
    // администратора ставится на её значение. Пароль в базе лежит хешем, и при утере
    // войти иначе нечем. Переменную надо удалить сразу после входа.
    AdminCredentialRecovery.Apply(
        scope.ServiceProvider.GetRequiredService<AdminUserRepository>(),
        scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger(nameof(AdminCredentialRecovery)));
}

// Токены ботов, сохранённые в админке, отдаём ботам до запуска фоновых служб: те
// проверяют готовность бота первой же строкой и без токена выключаются насовсем.
app.Services.GetRequiredService<TelegramService>().ApplyStoredTokens();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

// Обработка заголовков прокси должна стоять первой: от неё зависит определение
// схемы (http/https) для всех последующих компонентов, включая cookie админки.
app.UseForwardedHeaders();

// Заголовки безопасности ставим после обработчика исключений: тот при сбое очищает
// заголовки ответа, и без этого порядка на странице ошибки политик бы не осталось.
app.UseSecurityHeaders();

// Ошибки статуса: страницы — на русском и в общем оформлении, ответы /api — в JSON.
app.UseRussianErrorPages();

app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.MapRazorPages();

// Служебные адреса для поисковых систем: robots.txt и sitemap.xml. Отдаются из
// приложения, а не файлами в wwwroot, чтобы абсолютные адреса внутри брались
// из настройки Site:PublicUrl и не устаревали при переезде на другой домен.
app.MapSeo();

// =====================================================================
//  JSON API онлайн-записи и галереи работ.
//  Вся бизнес-логика — в сервисах, здесь только тонкая обвязка.
// =====================================================================
var api = app.MapGroup("/api");

api.MapGet("/services", (CatalogRepository catalog, string? category) =>
{
    var services = catalog.GetServices()
        .Where(s => string.IsNullOrWhiteSpace(category) || s.CategorySlug == category)
        .Select(s => new
        {
            id = s.Id,
            categoryId = s.CategoryId,
            categorySlug = s.CategorySlug,
            categoryName = s.CategoryName,
            name = s.Name,
            description = s.Description,
            duration = s.DurationMinutes,
            price = s.Price,
            priceLabel = FormatPrice(s.Price)
        });
    return Results.Ok(services);
});

api.MapGet("/masters", (MasterRepository masters, int serviceId) =>
{
    if (serviceId <= 0) return Results.BadRequest(new { error = "Не указана услуга" });

    var list = masters.GetForService(serviceId).Select(m => new MasterDto
    {
        Id = m.Id,
        Name = m.Name,
        Specialization = m.Specialization,
        PhotoPath = m.PhotoPath
    });
    return Results.Ok(list);
});

api.MapGet("/slots", (ScheduleService schedule, int serviceId, int masterId, string date) =>
{
    if (!DateOnly.TryParse(date, out var parsed))
        return Results.BadRequest(new { ok = false, error = "Некорректная дата" });

    var result = schedule.GetSlots(serviceId, masterId, parsed);
    return Results.Ok(new { ok = result.Ok, error = result.Error, slots = result.Slots });
});

api.MapGet("/dates", (ScheduleService schedule, int serviceId, int masterId) =>
{
    if (serviceId <= 0 || masterId <= 0)
        return Results.BadRequest(new { ok = false, error = "Не указаны услуга или мастер" });

    var dates = schedule.GetAvailableDates(masterId, serviceId)
        .Select(d => new
        {
            value = d.ToString("yyyy-MM-dd"),
            day = d.Day,
            month = Money.Month(d.Month),
            monthShort = Money.Month(d.Month)[..3],
            weekday = Money.WeekdayShort(d.DayOfWeek),
            label = Money.DateWithWeekday(d.ToDateTime(TimeOnly.MinValue))
        });

    return Results.Ok(dates);
});

api.MapGet("/works", (WorksService works, string? category) =>
{
    var list = works.GetGallery(category).Select(w => new
    {
        id = w.Id,
        title = w.Title,
        category = w.Category,
        categoryTitle = ELORA.Web.Models.WorkCategories.Title(w.Category),
        image = w.ImagePath,
        description = w.Description
    });
    return Results.Ok(list);
});

api.MapPost("/bookings", async (
    HttpContext context,
    IAntiforgery antiforgery,
    BookingService bookingService,
    BookingRequest request,
    CancellationToken cancellationToken) =>
{
    try
    {
        await antiforgery.ValidateRequestAsync(context);
    }
    catch (AntiforgeryValidationException)
    {
        return Results.BadRequest(new { ok = false, error = "Сессия устарела. Обновите страницу и попробуйте снова." });
    }

    var result = await bookingService.CreateAsync(request, cancellationToken);

    // Запоминаем запись в браузере: клиент вернётся на сайт и увидит её на странице
    // «Мои записи» без всякой регистрации. В куку идёт только токен управления.
    if (result.Ok)
        MyBookings.Remember(context.Request, context.Response, result.ManageToken);

    return result.Ok ? Results.Ok(result) : Results.BadRequest(result);
});

api.MapPost("/bookings/{token}/cancel", async (
    HttpContext context,
    IAntiforgery antiforgery,
    BookingService bookingService,
    string token,
    CancellationToken cancellationToken) =>
{
    try
    {
        await antiforgery.ValidateRequestAsync(context);
    }
    catch (AntiforgeryValidationException)
    {
        return Results.BadRequest(new { ok = false, error = "Сессия устарела. Обновите страницу." });
    }

    var result = await bookingService.CancelAsync(token, false, cancellationToken);
    return result.Ok ? Results.Ok(result) : Results.BadRequest(result);
});

api.MapPost("/bookings/{token}/reschedule", async (
    HttpContext context,
    IAntiforgery antiforgery,
    BookingService bookingService,
    string token,
    RescheduleRequest request,
    CancellationToken cancellationToken) =>
{
    try
    {
        await antiforgery.ValidateRequestAsync(context);
    }
    catch (AntiforgeryValidationException)
    {
        return Results.BadRequest(new { ok = false, error = "Сессия устарела. Обновите страницу." });
    }

    var result = await bookingService.RescheduleAsync(token, request.MasterId, request.Date, request.Time, cancellationToken);
    return result.Ok ? Results.Ok(result) : Results.BadRequest(result);
});

app.MapGet("/healthz", (SqliteConnectionFactory factory) =>
    Results.Ok(new { status = "ok", database = Path.GetFileName(factory.DatabasePath) }));

// ---------- AI-консультант: вопрос с сайта ----------
// Публичный маршрут, который тратит ключ владельца, поэтому:
//   • ограничение частоты по адресу посетителя (Ai:RequestsPerMinute);
//   • предел длины вопроса (Ai:MaxMessageLength);
//   • наружу уходят только готовые фразы, без подробностей провайдера.
api.MapPost("/chat", async (
    HttpContext context,
    ChatRequest request,
    EloraAssistant assistant,
    ChatRateLimiter limiter,
    IConfiguration configuration,
    CancellationToken cancellationToken) =>
{
    var message = (request.Message ?? "").Trim();
    if (message.Length == 0)
        return Results.BadRequest(new ChatReply { Ok = false, Error = "Введите вопрос" });

    var maxLength = assistant.MaxMessageLength;
    if (message.Length > maxLength)
        return Results.BadRequest(new ChatReply { Ok = false, Error = $"Вопрос слишком длинный — сократите до {maxLength} символов" });

    var perMinute = configuration.GetValue("Ai:RequestsPerMinute", 12);
    var clientKey = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    if (!limiter.TryPass(clientKey, perMinute, out var retryAfter))
    {
        context.Response.Headers.RetryAfter = retryAfter.ToString(CultureInfo.InvariantCulture);
        return Results.Json(
            new ChatReply { Ok = false, Error = $"Слишком много вопросов подряд. Попробуйте через {retryAfter} с." },
            statusCode: StatusCodes.Status429TooManyRequests);
    }

    var history = (request.History ?? new List<ChatTurnDto>())
        .Select(turn => new ChatTurn { Role = turn.Role, Content = turn.Content })
        .ToList();

    var reply = await assistant.AskAsync(message, history, cancellationToken);

    return reply.Ok
        ? Results.Ok(new ChatReply { Ok = true, Reply = reply.Text })
        : Results.Json(new ChatReply { Ok = false, Error = reply.Error }, statusCode: reply.Status);
});

// =====================================================================
//  Стенд для проверки ботов — ТОЛЬКО в Development.
//  Long polling живёт внутри приложения, поэтому нажать кнопку в чужом Telegram
//  при проверке невозможно: чтобы дойти до обработчика, нужно живое нажатие.
//  Здесь команда или нажатие подаются в тот же обработчик напрямую — так
//  проверяются сценарии, которые иначе остались бы непроверенными.
//  В Production маршрута нет вовсе: группа регистрируется под условием.
// =====================================================================
if (app.Environment.IsDevelopment())
{
    var dev = app.MapGroup("/dev/bot");

    dev.MapPost("/command", async (DevBotMessage request, TelegramBots bots,
        ClientBotHandler clientBot, AdminBotHandler adminBot, CancellationToken cancellationToken) =>
    {
        var role = request.Role == "admin" ? TelegramRole.Admin : TelegramRole.Client;
        var bot = bots.For(role);
        if (!bot.IsConfigured) return Results.BadRequest(new { error = $"бот ({role}) не настроен" });
        if (request.ChatId == 0) return Results.BadRequest(new { error = "не указан chatId" });

        ITelegramUpdateHandler handler = role == TelegramRole.Admin ? adminBot : clientBot;
        await handler.HandleMessageAsync(bot, request.ChatId, request.Text ?? "", cancellationToken);

        return Results.Ok(new { ok = true, bot = bot.Username, role = role.ToString(), sent = request.Text });
    });

    dev.MapPost("/callback", async (DevBotCallback request, TelegramBots bots,
        ClientBotHandler clientBot, AdminBotHandler adminBot, CancellationToken cancellationToken) =>
    {
        var role = request.Role == "admin" ? TelegramRole.Admin : TelegramRole.Client;
        var bot = bots.For(role);
        if (!bot.IsConfigured) return Results.BadRequest(new { error = $"бот ({role}) не настроен" });
        if (string.IsNullOrWhiteSpace(request.Data)) return Results.BadRequest(new { error = "не указан data" });

        ITelegramUpdateHandler handler = role == TelegramRole.Admin ? adminBot : clientBot;
        await handler.HandleCallbackAsync(bot, "", request.ChatId, request.MessageId, request.Data, cancellationToken);

        return Results.Ok(new { ok = true, role = role.ToString(), pressed = request.Data });
    });

    // Нажатие «отправить мой номер»: контакт приходит без текста, поэтому у него свой вход.
    dev.MapPost("/contact", async (DevBotContact request, TelegramBots bots,
        ClientBotHandler clientBot, AdminBotHandler adminBot, CancellationToken cancellationToken) =>
    {
        var role = request.Role == "admin" ? TelegramRole.Admin : TelegramRole.Client;
        var bot = bots.For(role);
        if (!bot.IsConfigured) return Results.BadRequest(new { error = $"бот ({role}) не настроен" });
        if (request.ChatId == 0) return Results.BadRequest(new { error = "не указан chatId" });
        if (string.IsNullOrWhiteSpace(request.Phone)) return Results.BadRequest(new { error = "не указан телефон" });

        ITelegramUpdateHandler handler = role == TelegramRole.Admin ? adminBot : clientBot;
        await handler.HandleContactAsync(bot, request.ChatId, request.Phone,
            request.Name ?? "Проверка привязки", cancellationToken);

        return Results.Ok(new { ok = true, role = role.ToString(), phone = request.Phone });
    });

    // Mini App в Telegram открывается только по HTTPS, поэтому живого initData на localhost
    // не получить. Здесь строка собирается и подписывается тем же токеном, что и настоящая,
    // — этого достаточно, чтобы проверить вход без пароля целиком: от формы до cookie.
    // По умолчанию берём Chat ID администратора: подпись от чужого id должна быть отклонена.
    dev.MapGet("/initdata", (IConfiguration configuration, SettingsRepository settings, long? userId, string? name) =>
    {
        var token = configuration["Telegram:AdminBotToken"];
        if (string.IsNullOrWhiteSpace(token))
            return Results.BadRequest(new { error = "не задан Telegram:AdminBotToken" });

        var rawId = settings.Get("Telegram.AdminChatId");
        var effectiveId = userId ?? (long.TryParse(rawId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : 0);

        if (effectiveId == 0)
            return Results.BadRequest(new { error = "не задан Chat ID администратора и не передан userId" });

        var initData = SignInitData(token, effectiveId, name ?? "Администратор", "elora_admin");
        return Results.Ok(new { ok = true, userId = effectiveId, initData });
    });

    // Форма кнопки Mini App. Telegram молча не покажет кнопку, если поле названо неверно,
    // а увидеть её без HTTPS невозможно — поэтому проверяем сам JSON, который уйдёт в Bot API.
    dev.MapGet("/webapp-button", (string? url) =>
    {
        var target = url ?? "https://example.com/admin";
        return Results.Ok(new { ok = true, button = TelegramBot.ButtonJson(TelegramButton.WebApp("🖥 Панель управления", target)) });
    });

    // Форма клавиатуры с кнопкой «отправить мой номер»: в живом чате её видно только
    // в Telegram, поэтому проверяем сам JSON, который уйдёт в Bot API. Ошибка в имени поля
    // тут не видна никак — Telegram просто не покажет кнопку, и привязка чата не заработает.
    dev.MapGet("/contact-keyboard", () => Results.Ok(new
    {
        ok = true,
        reply_markup = TelegramBot.ReplyKeyboardJson(
            new[] { new[] { TelegramButton.Contact("📱 Отправить мой номер") } })
    }));

    // Кнопки подменю «Сайт»: разделы сайта ссылками из чата. Telegram молча не покажет
    // кнопку с неверно названным полем, а живого чата на стенде нет — проверяем JSON.
    dev.MapGet("/site-keyboard", (string? url) =>
    {
        var site = (string.IsNullOrWhiteSpace(url) ? "https://example.com" : url.Trim()).TrimEnd('/');
        var keyboard = ClientBotHandler.SiteButtons(site)
            .Select(row => row.Select(TelegramBot.ButtonJson).ToArray())
            .ToArray();

        return Results.Ok(new { ok = true, site, inline_keyboard = keyboard });
    });
}

app.Run();

static string FormatPrice(decimal price) =>
    price.ToString("N0", System.Globalization.CultureInfo.GetCultureInfo("ru-RU")).Replace('\u00A0', ' ') + " ₽";

/// <summary>
/// Читает простой файл .env (<c>KEY=VALUE</c>, строки с <c>#</c> — комментарии)
/// и кладёт значения в переменные окружения процесса.
/// </summary>
/// <remarks>
/// Нужен только для локального запуска: на хостинге файла нет, секреты приходят
/// обычными переменными окружения. Уже заданное окружение главнее файла — так
/// можно быстро перекрыть значение, не правя .env.
/// </remarks>
/// <summary>
/// Ищет <c>.env</c> рядом с приложением и выше по дереву каталогов.
/// При запуске из <c>bin/Debug/net10.0</c> до файла в корне проекта несколько уровней,
/// поэтому одной проверки родителя мало.
/// </summary>
static string? FindDotEnv(string startDirectory)
{
    var directory = new DirectoryInfo(startDirectory);

    for (var level = 0; level < 6 && directory is not null; level++)
    {
        var candidate = Path.Combine(directory.FullName, ".env");
        if (File.Exists(candidate)) return candidate;

        directory = directory.Parent;
    }

    return null;
}

static void LoadDotEnv(string? path)
{
    if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;

    foreach (var raw in File.ReadAllLines(path))
    {
        var line = raw.Trim();
        if (line.Length == 0 || line.StartsWith('#')) continue;

        var separator = line.IndexOf('=');
        if (separator <= 0) continue;

        var key = line[..separator].Trim();
        var value = line[(separator + 1)..].Trim().Trim('"');

        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(key)))
            Environment.SetEnvironmentVariable(key, value);
    }
}

/// <summary>
/// Собирает строку initData так, как её собирает Telegram: поля плюс подпись,
/// посчитанная от производного ключа HMAC_SHA256("WebAppData", токен).
/// Только для стенда разработки — в самом приложении подпись можно лишь проверять.
/// </summary>
static string SignInitData(string botToken, long userId, string firstName, string username)
{
    var user = JsonSerializer.Serialize(new { id = userId, first_name = firstName, username },
        new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });

    var fields = new List<KeyValuePair<string, string>>
    {
        new("auth_date", DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)),
        new("query_id", "AA" + Guid.NewGuid().ToString("N")),
        new("user", user)
    };

    var checkString = string.Join('\n', fields
        .OrderBy(field => field.Key, StringComparer.Ordinal)
        .Select(field => $"{field.Key}={field.Value}"));

    using var secretHmac = new HMACSHA256(Encoding.UTF8.GetBytes("WebAppData"));
    var secretKey = secretHmac.ComputeHash(Encoding.UTF8.GetBytes(botToken));

    using var dataHmac = new HMACSHA256(secretKey);
    var hash = Convert.ToHexString(dataHmac.ComputeHash(Encoding.UTF8.GetBytes(checkString))).ToLowerInvariant();

    var query = string.Join('&', fields.Select(field =>
        $"{Uri.EscapeDataString(field.Key)}={Uri.EscapeDataString(field.Value)}"));

    return $"{query}&hash={hash}";
}

internal sealed class RescheduleRequest
{
    public int MasterId { get; set; }
    public string Date { get; set; } = "";
    public string Time { get; set; } = "";
}

/// <summary>Тело запроса к стенду ботов: подать команду в обработчик.</summary>
internal sealed class DevBotMessage
{
    public string Role { get; set; } = "client";
    public long ChatId { get; set; }
    public string? Text { get; set; }
}

/// <summary>Тело запроса к стенду ботов: сымитировать нажатие inline-кнопки.</summary>
internal sealed class DevBotCallback
{
    public string Role { get; set; } = "client";
    public long ChatId { get; set; }
    public int MessageId { get; set; }
    public string Data { get; set; } = "";
}

/// <summary>
/// Тело запроса к стенду ботов: сымитировать «отправить мой номер».
/// </summary>
/// <remarks>
/// Контакт приходит от Telegram отдельным полем и без текста, поэтому обычной командой
/// его не проверить: нужен свой вход, иначе привязка чата кнопкой остаётся непроверенной.
/// </remarks>
internal sealed class DevBotContact
{
    public string Role { get; set; } = "client";
    public long ChatId { get; set; }
    public string Phone { get; set; } = "";
    public string? Name { get; set; }
}
