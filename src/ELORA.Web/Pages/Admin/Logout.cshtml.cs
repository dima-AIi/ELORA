using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ELORA.Web.Pages.Admin;

/// <summary>
/// Выход из админки.
///
/// Важно: раньше рядом с этой моделью не было файла Logout.cshtml, а Razor Pages
/// находит страницы именно по .cshtml. Поэтому модель не попадала в маршрутизацию:
/// запрос к /admin/logout отдавал 404, выход не выполнялся и сессия оставалась
/// живой (после «выхода» /admin по-прежнему открывался без логина).
/// Теперь страница существует, и выход работает и по кнопке в сайдбаре (POST),
/// и по прямой ссылке (GET).
/// </summary>
public class LogoutModel : PageModel
{
    public Task<IActionResult> OnGetAsync() => SignOutAndRedirectAsync();

    public Task<IActionResult> OnPostAsync() => SignOutAndRedirectAsync();

    private async Task<IActionResult> SignOutAndRedirectAsync()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

        // Страховка: если cookie почему-то осталась бы в ответе — гасим её вручную.
        Response.Cookies.Delete("elora.admin");

        return RedirectToPage("/Admin/Login");
    }
}
