namespace ELORA.Web.Services.Ai;

/// <summary>
/// Настройки AI-консультанта. Ключ живёт только здесь — в конфигурации сервера
/// (User Secrets, переменные окружения хостинга), в браузер он не попадает никогда.
/// </summary>
public sealed class AiOptions
{
    public const string SectionName = "Ai";

    /// <summary>Ключ провайдера. Имя переменной окружения — <c>NVIDIA_API_KEY</c>.</summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// Модель-одиночка. Значения по умолчанию здесь совпадают с <c>appsettings.json</c>
    /// и с реально проверенной на этом ключе моделью: если из настроек убрать цепочку,
    /// приложение не должно уехать на модель, которую ключ не обслуживает.
    /// Используется, только если <see cref="Models"/> пуст.
    /// </summary>
    public string Model { get; set; } = "nvidia/nemotron-3-super-120b-a12b";

    /// <summary>
    /// Цепочка моделей по порядку. Первая рабочая и отвечает; если она недоступна
    /// у аккаунта, отдала ошибку, лимит или пустой ответ — пробуем следующую.
    /// Набор моделей у каждого аккаунта свой, поэтому одной модели мало.
    /// </summary>
    public List<string> Models { get; set; } = new();

    /// <summary>Адрес API. OpenAI-совместимый — провайдера можно заменить, не трогая сайт.</summary>
    public string BaseUrl { get; set; } = "https://integrate.api.nvidia.com/v1";

    /// <summary>Сколько ждём ответа одной модели. Дольше держать посетителя на «печатает…» нельзя.</summary>
    public int TimeoutSeconds { get; set; } = 45;

    /// <summary>
    /// Сколько раз пробовать одну модель при временном сбое (503 «перегружено», 429, 5xx).
    /// Бесплатный тариф иногда отвечает «Service temporarily overloaded»: быстрый повтор
    /// той же быстрой модели дешевле, чем ждать медленную запасную. Основная модель
    /// отвечает за ~2 с, поэтому три попытки стоят нескольких секунд и часто спасают.
    /// </summary>
    public int AttemptsPerModel { get; set; } = 3;

    /// <summary>
    /// Общий бюджет на все попытки. Без него цепочка из четырёх моделей держала бы
    /// посетителя у экрана почти минуту.
    /// </summary>
    public int TotalTimeoutSeconds { get; set; } = 80;

    /// <summary>Сколько последних сообщений диалога отправляем модели (без system).</summary>
    public int HistoryMessages { get; set; } = 6;

    /// <summary>Предел длины вопроса: защита от простыней в промпт.</summary>
    public int MaxMessageLength { get; set; } = 800;

    /// <summary>
    /// Предел ответа модели. Меньше ~1000 токенов опасно: у рассуждающих моделей
    /// размышления не помещаются и вытекают в поле ответа — посетитель видит
    /// «The user asks…» вместо ответа.
    /// </summary>
    public int MaxAnswerTokens { get; set; } = 1200;

    /// <summary>Запросов в минуту с одного адреса. Публичный endpoint тратит ключ владельца.</summary>
    public int RequestsPerMinute { get; set; } = 12;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey);

    /// <summary>Порядок перебора: сначала цепочка, иначе одна модель.</summary>
    public IReadOnlyList<string> EffectiveModels()
    {
        var chain = Models
            .Where(model => !string.IsNullOrWhiteSpace(model))
            .Select(model => model.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (chain.Count == 0 && !string.IsNullOrWhiteSpace(Model))
            chain.Add(Model.Trim());

        return chain;
    }
}
