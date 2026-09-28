using Microsoft.AspNetCore.Html;

namespace ELORA.Web.Helpers;

/// <summary>Иконки бокового меню админ-панели — тот же штриховой стиль, что и на сайте.</summary>
public static class AdminIcons
{
    public static IHtmlContent Render(string name, int size = 16) => name switch
    {
        "grid" => Svg("<rect x=\"3.5\" y=\"3.5\" width=\"7\" height=\"7\" rx=\"1.6\"/><rect x=\"13.5\" y=\"3.5\" width=\"7\" height=\"7\" rx=\"1.6\"/><rect x=\"3.5\" y=\"13.5\" width=\"7\" height=\"7\" rx=\"1.6\"/><rect x=\"13.5\" y=\"13.5\" width=\"7\" height=\"7\" rx=\"1.6\"/>", size),
        "calendar" => Svg("<rect x=\"3\" y=\"5\" width=\"18\" height=\"16\" rx=\"2\"/><path d=\"M8 3v4M16 3v4M3 11h18\"/>", size),
        "scissors" => Svg("<circle cx=\"6\" cy=\"6\" r=\"2.4\"/><circle cx=\"6\" cy=\"18\" r=\"2.4\"/><path d=\"M20 4 8.6 16.4M8.6 7.6 20 20\"/>", size),
        "user" => Svg("<circle cx=\"12\" cy=\"8\" r=\"3.6\"/><path d=\"M4.5 20a7.5 7.5 0 0 1 15 0\"/>", size),
        "users" => Svg("<circle cx=\"9\" cy=\"8\" r=\"3.2\"/><path d=\"M2.5 19.5a6.5 6.5 0 0 1 13 0\"/><path d=\"M16 5.6a3.2 3.2 0 0 1 0 6.3M17.5 14.2a6.5 6.5 0 0 1 4 5.3\"/>", size),
        "clock" => Svg("<circle cx=\"12\" cy=\"12\" r=\"9\"/><path d=\"M12 7.5V12l3 1.8\"/>", size),
        "image" => Svg("<rect x=\"3\" y=\"4\" width=\"18\" height=\"16\" rx=\"2\"/><circle cx=\"9\" cy=\"10\" r=\"2\"/><path d=\"m4 18 5-5 4 4 3-2.5 4 3.5\"/>", size),
        "star" => Svg("<path d=\"m12 3 2.7 5.5 6 .9-4.4 4.2 1 6-5.3-2.8-5.3 2.8 1-6L3.3 9.4l6-.9L12 3Z\"/>", size),
        "chat" => Svg("<path d=\"M20 12a7.5 7.5 0 0 1-11 6.7L5 20l1.4-3.6A7.5 7.5 0 1 1 20 12Z\"/>", size),
        "shield" => Svg("<path d=\"M12 3 5 6v5.5c0 4.2 2.9 8 7 9.5 4.1-1.5 7-5.3 7-9.5V6l-7-3Z\"/>", size),
        "plus" => Svg("<path d=\"M12 5v14M5 12h14\"/>", size),
        "trash" => Svg("<path d=\"M4 7h16M9 7V5h6v2M6 7l1 13h10l1-13\"/>", size),
        _ => Svg("<circle cx=\"12\" cy=\"12\" r=\"9\"/>", size)
    };

    private static IHtmlContent Svg(string body, int size) =>
        new HtmlString(
            $"<svg viewBox=\"0 0 24 24\" width=\"{size}\" height=\"{size}\" fill=\"none\" " +
            $"stroke=\"currentColor\" stroke-width=\"1.6\" stroke-linecap=\"round\" " +
            $"stroke-linejoin=\"round\" aria-hidden=\"true\">{body}</svg>");
}
