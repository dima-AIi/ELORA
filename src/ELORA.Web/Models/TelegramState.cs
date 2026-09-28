namespace ELORA.Web.Models;

/// <summary>
/// Состояние шага диалога в боте: на каком шаге застрял чат и что уже выбрано.
/// </summary>
/// <remarks>
/// Хранить это в <c>callback_data</c> нельзя — Telegram ограничивает его 64 байтами, а туда
/// надо уложить и мастера, и дату, и время. Поэтому состояние живёт на сервере, а в кнопке
/// едет только идентификатор или дата.
/// </remarks>
public sealed class TelegramState
{
    public long ChatId { get; init; }

    /// <summary>Значение из <see cref="Services.Telegram.TelegramSteps"/>.</summary>
    public string Step { get; init; } = "";

    public int? BookingId { get; init; }

    public int? MasterId { get; init; }

    /// <summary>Промежуточные данные шага: выбранная дата, услуга, имя клиента и так далее.</summary>
    public Dictionary<string, string> Data { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    public int? GetInt(string key) =>
        Data.TryGetValue(key, out var value) && int.TryParse(value, out var parsed) ? parsed : null;

    public string? Get(string key) => Data.TryGetValue(key, out var value) ? value : null;

    public TelegramState With(string key, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) Data.Remove(key);
        else Data[key] = value;
        return this;
    }
}
