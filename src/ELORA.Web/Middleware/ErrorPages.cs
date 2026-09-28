using System.Text.Json;

namespace ELORA.Web.Middleware;

/// <summary>
/// Ошибки статуса на русском: несуществующий адрес, запрет доступа и прочее.
/// </summary>
/// <remarks>
/// <para>
/// Раньше здесь стоял <c>UseStatusCodePagesWithReExecute("/Error", "?code={0}")</c>, но он
/// одинаково отвечает всем — а у <c>/api</c> язык ответа другой. Клиент записи разбирает
/// ответ как JSON: получив HTML, он покажет «ошибка сети» вместо причины. Поэтому для API
/// отдаём JSON, а для страниц — переписываем адрес на <c>/Error</c>.
/// </para>
/// <para>
/// Код ответа при переписывании сохраняется: Razor-страница не выставляет 200, если
/// его не выставлять явно, и middleware отдаёт посетителю тот же 404, что и был. Это важно —
/// поисковик иначе занесёт несуществующий адрес в индекс как рабочую страницу.
/// </para>
/// </remarks>
public static class ErrorPages
{
    public static IApplicationBuilder UseRussianErrorPages(this IApplicationBuilder app)
    {
        return app.UseStatusCodePages(async statusCodeContext =>
        {
            var context = statusCodeContext.HttpContext;
            var status = context.Response.StatusCode;

            if (context.Request.Path.StartsWithSegments("/api"))
            {
                context.Response.ContentType = "application/json; charset=utf-8";
                await context.Response.WriteAsync(JsonSerializer.Serialize(new
                {
                    ok = false,
                    error = status == StatusCodes.Status404NotFound ? "not_found" : "error",
                    status
                }));
                return;
            }

            // Переписываем адрес и заново прогоняем остаток конвейера: так отдаётся
            // страница ошибки в общем оформлении сайта.
            context.Request.Path = "/Error";
            context.Request.QueryString = new QueryString($"?code={status}");
            await statusCodeContext.Next(context);
        });
    }
}
