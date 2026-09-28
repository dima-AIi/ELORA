using Microsoft.AspNetCore.Html;

namespace ELORA.Web.Helpers;

/// <summary>Тонкие минималистичные иконки (stroke, currentColor) — единый язык интерфейса ELORA.</summary>
public static class Icons
{
    private static IHtmlContent Svg(string body, int size = 16, double stroke = 1.6) =>
        new HtmlString(
            $"<svg viewBox=\"0 0 24 24\" width=\"{size}\" height=\"{size}\" fill=\"none\" " +
            $"stroke=\"currentColor\" stroke-width=\"{stroke}\" stroke-linecap=\"round\" " +
            $"stroke-linejoin=\"round\" aria-hidden=\"true\">{body}</svg>");

    public static IHtmlContent Arrow(int size = 16) =>
        Svg("<path d=\"M5 12h14\"/><path d=\"m12 5 7 7-7 7\"/>", size);

    public static IHtmlContent ChevronDown(int size = 16) =>
        Svg("<path d=\"m6 9 6 6 6-6\"/>", size);

    /// <summary>Стрелка вверх — кнопка отправки вопроса AI-консультанту.</summary>
    public static IHtmlContent ArrowUp(int size = 18) =>
        Svg("<path d=\"M12 19V5\"/><path d=\"m5 12 7-7 7 7\"/>", size, 1.9);

    public static IHtmlContent Calendar(int size = 16) =>
        Svg("<rect x=\"3\" y=\"5\" width=\"18\" height=\"16\" rx=\"2\"/><path d=\"M8 3v4M16 3v4M3 11h18\"/>", size);

    public static IHtmlContent Check(int size = 16) =>
        Svg("<path d=\"M20 6 9 17l-5-5\"/>", size);

    public static IHtmlContent CheckCircle(int size = 16) =>
        Svg("<circle cx=\"12\" cy=\"12\" r=\"9\"/><path d=\"m8.5 12.5 2.5 2.5 4.5-5\"/>", size);

    public static IHtmlContent User(int size = 16) =>
        Svg("<circle cx=\"12\" cy=\"8\" r=\"3.6\"/><path d=\"M4.5 20a7.5 7.5 0 0 1 15 0\"/>", size);

    public static IHtmlContent UserCheck(int size = 16) =>
        Svg("<circle cx=\"10\" cy=\"8\" r=\"3.6\"/><path d=\"M3 20a7 7 0 0 1 12-4.9\"/><path d=\"m16 18 1.8 1.8L21 16.5\"/>", size);

    public static IHtmlContent Shield(int size = 16) =>
        Svg("<path d=\"M12 3 5 6v5.5c0 4.2 2.9 8 7 9.5 4.1-1.5 7-5.3 7-9.5V6l-7-3Z\"/><path d=\"m9.2 12 1.9 1.9 3.7-4\"/>", size);

    public static IHtmlContent Clock(int size = 16) =>
        Svg("<circle cx=\"12\" cy=\"12\" r=\"9\"/><path d=\"M12 7.5V12l3 1.8\"/>", size);

    public static IHtmlContent Sparkle(int size = 16) =>
        Svg("<path d=\"M12 3.5 13.7 9l5.5 1.7-5.5 1.7L12 18l-1.7-5.6L4.8 10.7 10.3 9 12 3.5Z\"/>", size);

    public static IHtmlContent Scissors(int size = 16) =>
        Svg("<circle cx=\"6\" cy=\"6\" r=\"2.4\"/><circle cx=\"6\" cy=\"18\" r=\"2.4\"/><path d=\"M20 4 8.6 16.4M8.6 7.6 20 20\"/>", size);

    public static IHtmlContent Brush(int size = 16) =>
        Svg("<path d=\"M4 20c2.5.6 4.6-.4 5.4-2.6.5-1.4 1.6-2.3 3-2.6\"/><path d=\"M20 4 11 13\"/>", size);

    public static IHtmlContent Phone(int size = 16) =>
        Svg("<path d=\"M6.2 3.5h2.6l1.4 3.5-2 1.3a12 12 0 0 0 5.5 5.5l1.3-2 3.5 1.4v2.6a2 2 0 0 1-2.2 2A15.5 15.5 0 0 1 4.2 5.7a2 2 0 0 1 2-2.2Z\"/>", size);

    public static IHtmlContent MapPin(int size = 16) =>
        Svg("<path d=\"M12 21s7-5.3 7-11a7 7 0 1 0-14 0c0 5.7 7 11 7 11Z\"/><circle cx=\"12\" cy=\"10\" r=\"2.6\"/>", size);

    public static IHtmlContent Telegram(int size = 16) =>
        new HtmlString(
            $"<svg viewBox=\"0 0 24 24\" width=\"{size}\" height=\"{size}\" fill=\"currentColor\" aria-hidden=\"true\">" +
            "<path d=\"M21.6 4.3 18.8 19c-.2 1-.8 1.2-1.6.8l-4.4-3.3-2.1 2c-.2.3-.5.4-.9.4l.3-4.4 8-7.2c.3-.3 0-.5-.5-.2L7.5 13.3 3.4 12c-.9-.3-.9-.9.2-1.3l17-6.6c.7-.3 1.3.2 1 1.2Z\"/></svg>");

    public static IHtmlContent Instagram(int size = 16) =>
        Svg("<rect x=\"3.5\" y=\"3.5\" width=\"17\" height=\"17\" rx=\"5\"/><circle cx=\"12\" cy=\"12\" r=\"4\"/><circle cx=\"17\" cy=\"7\" r=\"1\" fill=\"currentColor\" stroke=\"none\"/>", size);

    public static IHtmlContent Vk(int size = 16) =>
        new HtmlString(
            $"<svg viewBox=\"0 0 24 24\" width=\"{size}\" height=\"{size}\" fill=\"currentColor\" aria-hidden=\"true\">" +
            "<path d=\"M12.8 17.3c-5.5 0-8.7-3.8-8.8-10.1h2.8c.1 4.6 2.1 6.6 3.7 7V7.2h2.6v4c1.6-.2 3.3-2 3.8-4h2.6a7.7 7.7 0 0 1-3.5 5c1.8.9 3.3 2.5 3.9 5.1h-2.9c-.5-1.9-2-3.3-3.9-3.5v3.5h-.3Z\"/></svg>");

    public static IHtmlContent Star(int size = 11) =>
        new HtmlString(
            $"<svg viewBox=\"0 0 24 24\" width=\"{size}\" height=\"{size}\" fill=\"currentColor\" aria-hidden=\"true\">" +
            "<path d=\"m12 2.6 2.9 5.9 6.5.9-4.7 4.6 1.1 6.4-5.8-3-5.8 3 1.1-6.4L2.6 9.4l6.5-.9L12 2.6Z\"/></svg>");

    public static IHtmlContent Menu(int size = 18) =>
        Svg("<path d=\"M4 7h16M4 12h16M4 17h16\"/>", size);

    public static IHtmlContent Close(int size = 18) =>
        Svg("<path d=\"M6 6l12 12M18 6 6 18\"/>", size);

    public static IHtmlContent Image(int size = 16) =>
        Svg("<rect x=\"3\" y=\"4\" width=\"18\" height=\"16\" rx=\"2\"/><circle cx=\"9\" cy=\"10\" r=\"2\"/><path d=\"m4 18 5-5 4 4 3-2.5 4 3.5\"/>", size);

    public static IHtmlContent Chat(int size = 16) =>
        Svg("<path d=\"M20 12a7.5 7.5 0 0 1-11 6.7L5 20l1.4-3.6A7.5 7.5 0 1 1 20 12Z\"/>", size);

    /* ---------- Иконки шагов «Как записаться» (по референсу 2) ---------- */

    /* Иконки шагов нарисованы штрихом 1.9 (а не базовым 1.6): в референсе 2
       глифы внутри кружка заметно плотнее остальной линейной графики. */

    /* Глифы занимают ~78% поля 24×24: в референсе 2 рисунок внутри кружка
       диаметром 50px занимает ≈23px, то есть заметно больше обычной иконки. */

    /// <summary>Лупа с меткой внутри — «Выберите услугу и мастера».</summary>
    public static IHtmlContent SearchUser(int size = 16) =>
        Svg("<circle cx=\"10.4\" cy=\"10.4\" r=\"7.4\"/><circle cx=\"10.4\" cy=\"10.4\" r=\"1.7\"/>" +
            "<path d=\"m15.7 15.7 5.5 5.5\"/>", size, 1.9);

    /// <summary>Календарь с ручкой — «Укажите дату и время».</summary>
    public static IHtmlContent CalendarEdit(int size = 16) =>
        Svg("<rect x=\"4.4\" y=\"5.4\" width=\"15.2\" height=\"15.2\" rx=\"2.8\"/>" +
            "<path d=\"M8.8 3.6v3.6M15.2 3.6v3.6\"/>" +
            "<path d=\"m10.6 16.6 3.6-3.6a1.4 1.4 0 0 1 2 2l-3.6 3.6-2.5.6.5-2.6Z\"/>", size, 1.9);

    /// <summary>Сердце с галочкой — «Подтвердите запись».</summary>
    public static IHtmlContent HeartCheck(int size = 16) =>
        Svg("<path d=\"M12 21.2C7 17.9 2.7 14.2 2.7 10.1a5.2 5.2 0 0 1 9.3-3.3 5.2 5.2 0 0 1 9.3 3.3c0 4.1-4.3 7.8-9.3 11.1Z\"/>" +
            "<path d=\"m9 12.2 2.2 2.2 4.2-4.5\"/>", size, 1.9);

    /// <summary>Пакет — «Получите уведомление».</summary>
    public static IHtmlContent Bag(int size = 16) =>
        Svg("<rect x=\"3.6\" y=\"7.4\" width=\"16.8\" height=\"14.2\" rx=\"3.4\"/>" +
            "<path d=\"M8.8 7.4V6.2a3.2 3.2 0 0 1 6.4 0v1.2\"/>" +
            "<path d=\"M12 11.4v6.6\"/>", size, 1.9);
}
