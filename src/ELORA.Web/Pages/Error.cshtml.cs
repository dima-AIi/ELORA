using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ELORA.Web.Pages;

/// <summary>
/// Страница ошибки на русском: сюда посетитель попадает и по несуществующему адресу,
/// и при сбое на сервере.
/// </summary>
/// <remarks>
/// <para>
/// Код приходит двумя путями. Обычные ошибки статуса переписываются сюда middleware
/// с параметром <c>?code=</c>. Исключения обрабатывает <c>UseExceptionHandler("/Error")</c>,
/// и там параметра нет — поэтому код по умолчанию 500.
/// </para>
/// <para>
/// Наружу не отдаём ни трассировку, ни идентификатор запроса: посетителю они ничего не
/// объясняют, а устройство сервера выдают. Текст ошибки для разработчика остаётся в логе.
/// </para>
/// </remarks>
[ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
[IgnoreAntiforgeryToken]
public class ErrorModel : PageModel
{
    public int Code { get; private set; } = StatusCodes.Status500InternalServerError;

    public string Heading { get; private set; } = "";

    public string Explanation { get; private set; } = "";

    /// <summary>Стоит ли предлагать вернуться на главную и открыть другие разделы.</summary>
    public bool IsNotFound => Code == StatusCodes.Status404NotFound;

    public IActionResult OnGet(int? code = null)
    {
        Code = Normalize(code);

        // Запрос к API, на котором упало исключение, должен получить JSON: клиент записи
        // разбирает ответ как JSON, и HTML-страница для него — «ошибка сети» без причины.
        // Проверяем исходный адрес: к этому моменту он уже переписан на /Error.
        var originalPath = HttpContext.Features.Get<IStatusCodeReExecuteFeature>()?.OriginalPath;
        if (originalPath is not null && originalPath.StartsWith("/api", StringComparison.OrdinalIgnoreCase))
        {
            return new JsonResult(new
            {
                ok = false,
                error = Code == StatusCodes.Status404NotFound ? "not_found" : "error",
                status = Code
            })
            {
                StatusCode = Code
            };
        }

        (Heading, Explanation) = Describe(Code);

        // Код выставляем сами: страница не должна отвечать «200 всё хорошо» на ошибку.
        Response.StatusCode = Code;
        return Page();
    }

    /// <summary>
    /// Приводим код к тем, для которых у нас есть понятный текст. Остальные (405, 408, 502…)
    /// показываем как «что-то пошло не так» — выдумывать для каждого свой рассказ незачем.
    /// </summary>
    private static int Normalize(int? code)
    {
        var value = code ?? StatusCodes.Status500InternalServerError;

        return value switch
        {
            StatusCodes.Status400BadRequest => StatusCodes.Status400BadRequest,
            StatusCodes.Status403Forbidden => StatusCodes.Status403Forbidden,
            StatusCodes.Status404NotFound => StatusCodes.Status404NotFound,
            _ when value >= 400 && value <= 599 => StatusCodes.Status500InternalServerError,
            _ => StatusCodes.Status500InternalServerError
        };
    }

    private static (string Heading, string Explanation) Describe(int code) => code switch
    {
        StatusCodes.Status404NotFound => (
            "Страница не найдена",
            "Такого адреса на сайте нет. Возможно, ссылка устарела или в адресе опечатка."),

        StatusCodes.Status403Forbidden => (
            "Доступ закрыт",
            "Эта страница только для сотрудников студии. Если вы искали запись или прайс — они открыты всем."),

        StatusCodes.Status400BadRequest => (
            "Запрос не понят",
            "Сервер не смог разобрать запрос. Обновите страницу и попробуйте ещё раз."),

        _ => (
            "Что-то пошло не так",
            "На сервере произошёл сбой. Мы уже записали его в журнал — попробуйте обновить страницу через минуту.")
    };
}
