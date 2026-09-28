using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Security.Claims;
using ELORA.Web.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ELORA.Web.Pages.Admin;

[AllowAnonymous]
public class LoginModel : PageModel
{
    /// <summary>
    /// Сколько живут присланные Telegram данные входа. Страница Mini App получает их при
    /// каждом открытии, поэтому сутки — с запасом: и на «свернул и вернулся», и на разницу часов.
    /// Дольше держать нельзя: перехваченная строка становится пропуском.
    /// </summary>
    private static readonly TimeSpan WebAppDataMaxAge = TimeSpan.FromHours(24);

    private readonly AdminService _admin;
    private readonly TelegramService _telegram;
    private readonly ILogger<LoginModel> _logger;

    public LoginModel(AdminService admin, TelegramService telegram, ILogger<LoginModel> logger)
    {
        _admin = admin;
        _telegram = telegram;
        _logger = logger;
    }

    /// <summary>
    /// Поля с русскими сообщениями об ошибке. Атрибуты нужны не для проверки на сервере —
    /// её делает <c>OnPostAsync</c> ниже, — а для разметки: <c>asp-for</c> переносит текст
    /// сообщения в атрибут <c>data-val-required</c>, и если его не задать, туда попадает
    /// английская заготовка платформы («The Login field is required.»). Именно так и было
    /// на боевом сайте до 26.09.2026 — атрибуты с английским текстом видел каждый, кто
    /// открывал страницу входа.
    /// </summary>
    [BindProperty]
    [Required(ErrorMessage = "Укажите логин")]
    public string Login { get; set; } = "";

    [BindProperty]
    [Required(ErrorMessage = "Укажите пароль")]
    public string Password { get; set; } = "";

    public string? Error { get; private set; }

    public IActionResult OnGet(string? returnUrl = null)
    {
        if (User.Identity?.IsAuthenticated == true)
            return RedirectToPage("/Admin/Index");

        ViewData["ReturnUrl"] = returnUrl;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(string? returnUrl = null)
    {
        ViewData["ReturnUrl"] = returnUrl;

        if (string.IsNullOrWhiteSpace(Login) || string.IsNullOrWhiteSpace(Password))
        {
            Error = "Укажите логин и пароль";
            return Page();
        }

        if (!_admin.VerifyLogin(Login.Trim(), Password, out var userId))
        {
            _logger.LogWarning("Неудачная попытка входа в админку: {Login}", Login);
            Error = "Неверный логин или пароль";
            return Page();
        }

        await SignInAsync(Login.Trim(), userId.ToString(CultureInfo.InvariantCulture), "пароль");

        if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl))
            return Redirect(returnUrl);

        return RedirectToPage("/Admin/Index");
    }

    /// <summary>
    /// Вход из Mini App: Telegram передаёт подписанные данные, сервер проверяет подпись ключом
    /// админского бота и сверяет идентификатор пользователя с Chat ID администратора.
    /// Пароль при этом остаётся рабочим — на случай, если бот ещё не настроен или Telegram недоступен.
    /// </summary>
    public async Task<IActionResult> OnPostTelegramAsync(string? initData, string? returnUrl = null)
    {
        ViewData["ReturnUrl"] = returnUrl;

        var bot = _telegram.Bots.Admin;
        if (!bot.IsConfigured)
        {
            Error = "Вход через Telegram недоступен: не задан токен админского бота. Войдите по логину и паролю.";
            return Page();
        }

        if (!bot.TryValidateWebAppData(initData, WebAppDataMaxAge, out var user, out var error) || user is null)
        {
            _logger.LogWarning("Вход из Mini App отклонён: {Reason}", error);
            Error = $"Вход через Telegram отклонён: {error}. Войдите по логину и паролю.";
            return Page();
        }

        var adminChatId = _telegram.AdminChatId;
        if (string.IsNullOrWhiteSpace(adminChatId) ||
            !string.Equals(adminChatId, user.Id.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal))
        {
            // Подпись верна, но человек не администратор: у админского бота есть и другие читатели.
            _logger.LogWarning("Вход из Mini App отклонён: Telegram id {UserId} не администратор", user.Id);
            Error = "Этот Telegram-аккаунт не привязан к админ-панели.";
            return Page();
        }

        await SignInAsync(user.Name, user.Id.ToString(CultureInfo.InvariantCulture), "telegram");

        if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl))
            return Redirect(returnUrl);

        return RedirectToPage("/Admin/Index");
    }

    private async Task SignInAsync(string name, string identifier, string via)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, identifier),
            new(ClaimTypes.Name, name),
            new(ClaimTypes.Role, "Admin"),
            new("elora:via", via)
        };

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(identity),
            new AuthenticationProperties { IsPersistent = true });
    }
}
