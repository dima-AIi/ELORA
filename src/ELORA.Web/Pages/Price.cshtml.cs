using System.Globalization;
using System.Text;
using ELORA.Web.Data.Repositories;
using ELORA.Web.Helpers;
using ELORA.Web.Models;
using ELORA.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ELORA.Web.Pages;

public class PriceModel : PageModel
{
    private readonly CatalogRepository _catalog;

    public PriceModel(CatalogRepository catalog) => _catalog = catalog;

    public List<PriceGroup> Groups { get; private set; } = new();
    public List<Service> Services { get; private set; } = new();

    public void OnGet()
    {
        Services = _catalog.GetServices();
        Groups = PriceListBuilder.Build(Services);
    }

    /// <summary>
    /// Скачивание прайса файлом — открывается по <c>/price?handler=Download</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Раньше кнопка «Скачать прайс» вызывала <c>window.print()</c>: она открывала окно
    /// печати, а не сохраняла файл. Название обещало одно, кнопка делала другое — и на
    /// телефоне это выглядело просто как поломка.
    /// </para>
    /// <para>
    /// Формат — CSV, а не XLSX: он открывается и в Excel, и в «Google Таблицах», и в
    /// «МойОфис», а для XLSX понадобилась бы отдельная библиотека. Две вещи в файле
    /// сделаны ради русского Excel: разделитель «;» (с запятой всё съезжает в одну колонку)
    /// и метка BOM в начале (без неё UTF-8 читается как ANSI и кириллица превращается
    /// в мусор).
    /// </para>
    /// </remarks>
    public IActionResult OnGetDownload()
    {
        var services = _catalog.GetServices();
        var groups = PriceListBuilder.Build(services);

        var csv = new StringBuilder();
        csv.Append("Категория;Услуга;Длительность;Цена, ₽\r\n");

        foreach (var group in groups)
        {
            foreach (var item in group.Items)
            {
                var service = services.FirstOrDefault(s => s.Name == item.FullName);
                var duration = service is null ? "" : Money.Duration(service.DurationMinutes);

                csv.Append(Field(group.Title)).Append(';')
                   .Append(Field(item.FullName)).Append(';')
                   .Append(Field(duration)).Append(';')
                   .Append(item.Price.ToString("0", CultureInfo.InvariantCulture))
                   .Append("\r\n");
            }
        }

        // Пометка прямо в файле: прайс уходит в переписку, и там о демонстрационном
        // характере цен уже ничто не напоминает.
        csv.Append("\r\n");
        csv.Append("Прайс-лист от ").Append(DateTime.Now.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture)).Append("\r\n");
        csv.Append("ELORA — учебный проект. Цены демонстрационные.\r\n");

        // Метку BOM дописываем вручную: GetBytes её не отдаёт, преамбулу добавляют только
        // потоки и StreamWriter. Без этих трёх байт Excel открывает файл как ANSI,
        // и вместо «Маникюр» в первой колонке стоит «ÐœÐ°Ð½Ð¸ÐºÑŽÑ€».
        var bytes = Encoding.UTF8.GetPreamble()
            .Concat(new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(csv.ToString()))
            .ToArray();

        return new FileContentResult(bytes, "text/csv; charset=utf-8")
        {
            FileDownloadName = "elora-price.csv"
        };
    }

    /// <summary>Экранирование поля CSV: разделитель, кавычка и перенос строки ломают строку.</summary>
    private static string Field(string value) =>
        value.Contains(';') || value.Contains('"') || value.Contains('\n') || value.Contains('\r')
            ? "\"" + value.Replace("\"", "\"\"") + "\""
            : value;
}
