using System.Security.Claims;
using ELORA.Web.Data.Repositories;
using ELORA.Web.Helpers;
using ELORA.Web.Services;
using ELORA.Web.Services.Telegram;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ELORA.Web.Pages.Admin;

public class SettingsModel : PageModel
{
    private static readonly string[] SiteKeys =
    {
        "Site.Phone", "Site.Address", "Site.Email", "Site.WorkHours",
        "Site.Telegram", "Site.Vk", "Site.Instagram",
        "Site.LegalName", "Site.LegalInn"
    };

    private readonly SettingsRepository _settings;
    private readonly AdminUserRepository _users;
    private readonly TelegramService _telegram;
    private readonly DatabaseBackupService _backups;
    private readonly IConfiguration _configuration;
    private readonly ILogger<SettingsModel> _logger;

    public SettingsModel(
        SettingsRepository settings,
        AdminUserRepository users,
        TelegramService telegram,
        DatabaseBackupService backups,
        IConfiguration configuration,
        ILogger<SettingsModel> logger)
    {
        _settings = settings;
        _users = users;
        _telegram = telegram;
        _backups = backups;
        _configuration = configuration;
        _logger = logger;
    }

    public Dictionary<string, string> Values { get; private set; } = new();
    public bool ClientTokenConfigured { get; private set; }
    public bool AdminTokenConfigured { get; private set; }
    public bool TokenConfigured => ClientTokenConfigured || AdminTokenConfigured;
    public string? BotInfo { get; private set; }

    /// <summary>Откуда взят токен: из панели хостинга или из этой формы. Нужно, чтобы было
    /// понятно, почему поле ниже «не действует». </summary>
    public string ClientTokenSource { get; private set; } = "не задан";
    public string AdminTokenSource { get; private set; } = "не задан";

    /// <summary>Токен задан переменной окружения — поле в форме его не перебивает.</summary>
    public bool ClientTokenFromEnvironment { get; private set; }
    public bool AdminTokenFromEnvironment { get; private set; }

    /// <summary>Чек-лист «почему уведомления молчат». Показывается прямо в карточке Telegram.</summary>
    public IReadOnlyList<TelegramCheck> Checks { get; private set; } = Array.Empty<TelegramCheck>();

    /// <summary>Адрес сайта из настроек — по нему Telegram открывает панель как Mini App.</summary>
    public string? SiteUrl { get; private set; }

    public bool SiteUrlIsHttps { get; private set; }

    /// <summary>Ссылка на админского бота, чтобы владелец мог написать ему /start в один клик.</summary>
    public string? AdminBotLink { get; private set; }

    /// <summary>
    /// Контакты на сайте всё ещё демонстрационные. Это не косметика: +7 (999) — реальный
    /// диапазон номеров, и по нему может ответить посторонний человек, а адрес не существует.
    /// </summary>
    public bool ContactsAreDemo { get; private set; }

    /// <summary>Резервные копии базы, от свежей к старой.</summary>
    public IReadOnlyList<DatabaseBackup> Backups { get; private set; } = Array.Empty<DatabaseBackup>();

    /// <summary>Возраст самой свежей копии в днях. <c>null</c> — копий нет вообще.</summary>
    public int? LastBackupDays { get; private set; }

    /// <summary>
    /// Задана переменная аварийного сброса пароля. Тогда пароль администратора при каждом
    /// запуске сайта берётся из неё, и менять его формой бессмысленно — об этом надо сказать.
    /// </summary>
    public bool ResetPasswordArmed { get; private set; }

    public void OnGet()
    {
        Values = _settings.GetAll();
        ClientTokenConfigured = _telegram.IsClientBotConfigured;
        AdminTokenConfigured = _telegram.IsAdminBotConfigured;
        Checks = _telegram.Checks();

        ClientTokenSource = _telegram.TokenSource(TelegramRole.Client);
        AdminTokenSource = _telegram.TokenSource(TelegramRole.Admin);
        ClientTokenFromEnvironment = _telegram.Bots.Client.TokenFromEnvironment;
        AdminTokenFromEnvironment = _telegram.Bots.Admin.TokenFromEnvironment;

        SiteUrl = _configuration["Site:PublicUrl"]?.TrimEnd('/');
        SiteUrlIsHttps = SiteUrl is not null &&
                         SiteUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

        AdminBotLink = _telegram.Bots.Admin.BuildChatLink();

        var phone = Values.GetValueOrDefault("Site.Phone") ?? "";
        var address = Values.GetValueOrDefault("Site.Address") ?? "";
        ContactsAreDemo = phone.Contains("999") || address.Contains("Примерная", StringComparison.OrdinalIgnoreCase);

        Backups = _backups.List();
        LastBackupDays = Backups.Count == 0
            ? null
            : (int)(DateTime.Now.Date - Backups[0].CreatedAt.Date).TotalDays;

        ResetPasswordArmed = AdminCredentialRecovery.IsArmed;
    }

    public IActionResult OnPostSaveSite(string phone, string address, string email, string workHours,
        string? telegram, string? vk, string? instagram, string? legalName, string? legalInn)
    {
        var map = new Dictionary<string, string>
        {
            ["Site.Phone"] = phone ?? "",
            ["Site.Address"] = address ?? "",
            ["Site.Email"] = email ?? "",
            ["Site.WorkHours"] = workHours ?? "",
            ["Site.Telegram"] = telegram ?? "",
            ["Site.Vk"] = vk ?? "",
            ["Site.Instagram"] = instagram ?? "",
            ["Site.LegalName"] = legalName ?? "",
            ["Site.LegalInn"] = legalInn ?? ""
        };

        foreach (var key in SiteKeys)
            _settings.Set(key, map.TryGetValue(key, out var value) ? value : "");

        TempData["Flash"] = "Контакты и часы работы сохранены";
        return RedirectToPage();
    }

    public IActionResult OnPostSaveTelegram(bool enabled, string? adminChatId, string? botUsername,
        string? adminBotUsername, int reminderHoursBefore, bool notifyOnNewBooking)
    {
        _settings.Set("Telegram.Enabled", enabled ? "true" : "false");
        _settings.Set("Telegram.AdminChatId", adminChatId ?? "");
        _settings.Set("Telegram.BotUsername", (botUsername ?? "").TrimStart('@'));
        _settings.Set("Telegram.AdminBotUsername", (adminBotUsername ?? "").TrimStart('@'));
        _settings.Set("Telegram.ReminderHoursBefore", Math.Clamp(reminderHoursBefore, 1, 168).ToString());
        _settings.Set("Telegram.NotifyOnNewBooking", notifyOnNewBooking ? "true" : "false");

        TempData["Flash"] = "Настройки Telegram сохранены";
        return RedirectToPage();
    }

    /// <summary>
    /// Срок «пора напомнить». Для маникюра норма — около трёх недель; значение вынесено
    /// в настройки, потому что у каждой студии свой цикл.
    /// </summary>
    public IActionResult OnPostSaveClients(int winBackWeeks)
    {
        _settings.Set("Clients.WinBackWeeks", Math.Clamp(winBackWeeks, 1, 52).ToString());
        TempData["Flash"] = "Настройки напоминаний клиентам сохранены";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostTestTelegramAsync(CancellationToken cancellationToken)
    {
        if (!_telegram.IsConfigured)
        {
            TempData["FlashError"] = "Токен бота не задан: добавьте Telegram:BotToken в User Secrets или переменные окружения";
            return RedirectToPage();
        }

        var chatId = _telegram.AdminChatId;
        if (string.IsNullOrWhiteSpace(chatId))
        {
            TempData["FlashError"] = "Укажите Chat ID администратора — иначе отправлять некуда";
            return RedirectToPage();
        }

        try
        {
            var error = await _telegram.SendTestAsync(chatId,
                "✅ ELORA: проверка связи. Уведомления о записях будут приходить сюда.",
                cancellationToken);

            TempData[error is null ? "Flash" : "FlashError"] = error is null
                ? "Тестовое сообщение отправлено"
                : $"Telegram отклонил отправку: {error}";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Тест Telegram не удался");
            TempData["FlashError"] = "Не удалось отправить сообщение. Проверьте токен и Chat ID.";
        }

        return RedirectToPage();
    }

    /// <summary>
    /// Опрашивает оба бота через getMe и сохраняет их имена. Бот может быть не задан —
    /// тогда просто пропускаем; важно, чтобы сбой одного не мешал проверке второго.
    /// </summary>
    public async Task<IActionResult> OnPostBotInfoAsync(CancellationToken cancellationToken)
    {
        if (!_telegram.IsConfigured)
        {
            TempData["FlashError"] = "Ни один токен не задан: Telegram:BotToken и Telegram:AdminBotToken";
            return RedirectToPage();
        }

        var found = new List<string>();
        var failed = new List<string>();

        if (_telegram.Bots.Client.IsConfigured)
            await ProbeAsync(_telegram.Bots.Client, "Telegram.BotUsername", "клиентский", found, failed, cancellationToken);

        if (_telegram.Bots.Admin.IsConfigured)
            await ProbeAsync(_telegram.Bots.Admin, "Telegram.AdminBotUsername", "админский", found, failed, cancellationToken);

        var message = found.Count > 0 ? $"Найдены боты: {string.Join(", ", found)}" : "";
        if (failed.Count > 0)
        {
            if (message.Length > 0) message += ". ";
            TempData["FlashError"] = message + $"Не ответили: {string.Join(", ", failed)} — проверьте токены";
        }
        else
        {
            TempData["Flash"] = message.Length > 0 ? message : "Токенов нет";
        }

        return RedirectToPage();
    }

    private async Task ProbeAsync(TelegramBot bot, string settingKey, string label,
        List<string> found, List<string> failed, CancellationToken cancellationToken)
    {
        try
        {
            var username = await bot.GetBotUsernameAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(username))
            {
                failed.Add(label);
                return;
            }

            var clean = username.TrimStart('@');
            _settings.Set(settingKey, clean);
            found.Add($"@{clean} ({label})");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Проверка бота {Role} не удалась", bot.Role);
            failed.Add(label);
        }
    }

    /// <summary>
    /// Токены ботов из формы. Пустое поле означает «не менять» — иначе сохранение настроек
    /// каждый раз стирало бы токен, ведь поле пароля всегда приходит пустым.
    /// </summary>
    public async Task<IActionResult> OnPostSaveTokensAsync(string? clientToken, string? adminToken,
        bool clearClientToken, bool clearAdminToken, CancellationToken cancellationToken)
    {
        var problems = _telegram.SaveTokens(clientToken, adminToken, clearClientToken, clearAdminToken);

        // Имя бота тянем сразу: без него ссылка привязки на странице успешной записи
        // останется без адреса, и клиент не поймёт, куда идти.
        await _telegram.EnsureUsernamesAsync(cancellationToken);

        if (problems is not null)
        {
            TempData["FlashError"] = problems;
        }
        else
        {
            TempData["Flash"] = _telegram.IsConfigured
                ? "Токены сохранены и применены — перезапуск сайта не нужен"
                : "Сохранено. Токенов по-прежнему нет — уведомления не уйдут";
        }

        return RedirectToPage();
    }

    /// <summary>Тест клиентского бота: он пишет только тем, кто уже нажал Start в нём.</summary>
    public async Task<IActionResult> OnPostTestClientAsync(CancellationToken cancellationToken)
    {
        var bot = _telegram.Bots.Client;
        if (!bot.IsConfigured)
        {
            TempData["FlashError"] = "Токен клиентского бота не задан: Telegram:BotToken";
            return RedirectToPage();
        }

        var chatId = _telegram.AdminChatId;
        if (string.IsNullOrWhiteSpace(chatId))
        {
            TempData["FlashError"] = "Укажите Chat ID администратора — иначе отправлять некуда";
            return RedirectToPage();
        }

        var error = await bot.SendMessageExAsync(chatId,
            "✅ ELORA: проверка клиентского бота. Сюда будут приходить напоминания клиентам и ответы студии.",
            null, cancellationToken);

        TempData[error is null ? "Flash" : "FlashError"] = error is null
            ? "Клиентский бот отправил сообщение"
            : $"Клиентский бот: {error}";

        return RedirectToPage();
    }

    /// <summary>
    /// Ставит админскому боту кнопку меню, открывающую панель как Mini App.
    /// Telegram принимает только HTTPS-адрес: по http он отвечает ошибкой, поэтому причину
    /// отказываемся объяснять словами, а не молчанием кнопки.
    /// </summary>
    public async Task<IActionResult> OnPostMiniAppAsync(CancellationToken cancellationToken)
    {
        var site = _configuration["Site:PublicUrl"]?.TrimEnd('/');

        if (string.IsNullOrWhiteSpace(site))
        {
            TempData["FlashError"] = "В настройках сайта пустой адрес (Site:PublicUrl) — боту нечего открывать";
            return RedirectToPage();
        }

        if (!site.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            TempData["FlashError"] =
                $"Mini App Telegram открывает только по HTTPS, а сайт сейчас на {site}. " +
                "Включите бесплатный SSL-сертификат в панели хостинга, дождитесь, пока сайт откроется " +
                "по https, и нажмите кнопку снова.";
            return RedirectToPage();
        }

        if (!_telegram.IsAdminBotConfigured)
        {
            TempData["FlashError"] = "Токен админского бота не задан — кнопку ставить некому";
            return RedirectToPage();
        }

        var ok = await _telegram.Bots.Admin.SetChatMenuButtonAsync($"{site}/admin", "Панель", cancellationToken);

        TempData[ok ? "Flash" : "FlashError"] = ok
            ? "Кнопка Mini App поставлена: в админском боте она слева от поля ввода"
            : "Telegram не принял адрес. Проверьте, что сайт открывается по https и сертификат действителен";

        return RedirectToPage();
    }

    /// <summary>
    /// Убирает кнопку «Сайт» у клиентского бота и возвращает ему меню команд.
    /// </summary>
    /// <remarks>
    /// Кнопка-сайт уводит человека из чата, так и не нажав Start, — а без Start Telegram
    /// не даёт боту написать первым, и клиент не получает ни подтверждения записи,
    /// ни напоминания. Кнопка живёт в настройках бота, а не в нашей базе, поэтому снять
    /// её можно только запросом к Telegram. Служба опроса делает то же самое при каждом
    /// запуске сайта — эта кнопка нужна, чтобы не ждать перезапуска.
    /// </remarks>
    public async Task<IActionResult> OnPostClientButtonAsync(CancellationToken cancellationToken)
    {
        if (!_telegram.IsClientBotConfigured)
        {
            TempData["FlashError"] = "Токен клиентского бота не задан — кнопку убирать некому";
            return RedirectToPage();
        }

        var ok = await _telegram.Bots.Client.ResetChatMenuButtonAsync(cancellationToken);

        TempData[ok ? "Flash" : "FlashError"] = ok
            ? "Кнопка «Сайт» убрана: в боте студии снова меню команд — «Записаться», «Мои записи», «Помощь»"
            : "Telegram не принял запрос. Проверьте токен клиентского бота";

        return RedirectToPage();
    }

    /// <summary>
    /// Резервная копия по требованию. Та же операция, что делает фоновая служба раз в сутки;
    /// кнопка нужна перед рискованным изменением — «сделаю копию и попробую».
    /// </summary>
    public IActionResult OnPostBackupNow()
    {
        var created = _backups.CreateToday();
        TempData[created is null ? "FlashError" : "Flash"] = created is null
            ? "Копию сделать не удалось либо копия за сегодня уже есть. Подробности в журнале сайта."
            : $"Резервная копия создана: {created}";

        return RedirectToPage();
    }

    public IActionResult OnPostChangePassword(string currentPassword, string newPassword, string confirmPassword)
    {
        var userIdRaw = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(userIdRaw, out var userId))
        {
            TempData["FlashError"] = "Сессия истекла, войдите заново";
            return RedirectToPage();
        }

        var login = User.Identity?.Name ?? "";
        var user = _users.FindByLogin(login);
        if (user is null || !PasswordHasher.Verify(currentPassword, user.Value.PasswordHash))
        {
            TempData["FlashError"] = "Текущий пароль указан неверно";
            return RedirectToPage();
        }

        // Порядок проверок: сначала совпадение, затем содержимое. Иначе человек
        // ввёл кириллицу и не подтвердил пароль — и видит только про совпадение,
        // хотя дело ещё и в раскладке.
        if (string.IsNullOrWhiteSpace(newPassword))
        {
            TempData["FlashError"] = "Введите новый пароль";
            return RedirectToPage();
        }

        if (newPassword != confirmPassword)
        {
            TempData["FlashError"] = "Пароли не совпадают";
            return RedirectToPage();
        }

        // Проверка содержимого: длина, латинские буквы, отсутствие кириллицы.
        var passwordError = UserInput.ValidatePassword(newPassword);
        if (passwordError is not null)
        {
            TempData["FlashError"] = passwordError;
            return RedirectToPage();
        }

        _users.UpdatePassword(userId, PasswordHasher.Hash(newPassword));
        TempData["Flash"] = "Пароль изменён";
        return RedirectToPage();
    }
}
