namespace ELORA.Web.Helpers;

/// <summary>
/// Инициалы для аватара без фотографии.
/// </summary>
/// <remarks>
/// Нужны не для красоты. Фотографии мастеров и клиентов взяты с Pexels, а её лицензия
/// прямо запрещает «создавать впечатление, что люди на изображении поддерживают ваш
/// продукт» — то есть стоковое лицо нельзя выдавать за своего мастера или за клиента
/// студии. Поэтому фото можно убрать одним полем, а карточка обязана остаться целой:
/// на месте снимка появляются инициалы.
/// </remarks>
public static class Initials
{
    public static string From(string? name)
    {
        var parts = (name ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length switch
        {
            0 => "?",
            1 => parts[0][..1].ToUpperInvariant(),
            _ => (parts[0][..1] + parts[1][..1]).ToUpperInvariant()
        };
    }
}
