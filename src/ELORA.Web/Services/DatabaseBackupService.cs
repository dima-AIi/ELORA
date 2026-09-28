using ELORA.Web.Data;
using Microsoft.Data.Sqlite;

namespace ELORA.Web.Services;

/// <summary>Одна резервная копия базы: имя файла, дата и размер.</summary>
public sealed record DatabaseBackup(string FileName, DateTime CreatedAt, long SizeBytes);

/// <summary>
/// Ежедневная резервная копия базы рядом с самой базой.
/// </summary>
/// <remarks>
/// <para>
/// Копия делается запросом <c>VACUUM INTO</c>, а не копированием файла: сайт пишет в базу
/// в тот же момент, и обычное копирование может поймать половину транзакции — такая копия
/// откроется, но данных в ней не будет. <c>VACUUM INTO</c> отдаёт цельный снимок на момент
/// запроса и заодно пересобирает файл без пустых страниц.
/// </para>
/// <para>
/// Копии остаются на хостинге и наружу не отдаются. В базе лежат телефоны клиентов, а если
/// владелец вписал токены ботов в админке — ещё и токены, поэтому отдавать такой файл
/// по http нельзя: HTTPS на хостинге пока не включён. Когда сертификат включат, сюда можно
/// добавить выгрузку.
/// </para>
/// <para>
/// Служба не имеет права ничего ронять: любая её ошибка логируется и забывается. Иначе
/// необработанное исключение в фоновой службе останавливает весь хост вместе с сайтом.
/// </para>
/// </remarks>
public sealed class DatabaseBackupService : BackgroundService
{
    /// <summary>Сколько копий храним. По одной в день — две недели истории, около 2 МБ.</summary>
    private const int KeepCount = 14;

    private static readonly TimeSpan Interval = TimeSpan.FromHours(24);

    /// <summary>Пауза после старта: не мешаем приложению подниматься и прогреваться.</summary>
    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(90);

    private readonly SqliteConnectionFactory _factory;
    private readonly ILogger<DatabaseBackupService> _logger;

    public DatabaseBackupService(SqliteConnectionFactory factory, ILogger<DatabaseBackupService> logger)
    {
        _factory = factory;
        _logger = logger;
        Directory = Path.Combine(Path.GetDirectoryName(_factory.DatabasePath) ?? ".", "backups");
    }

    /// <summary>Папка с копиями. Лежит рядом с базой — там же, где её ищут при восстановлении.</summary>
    public string Directory { get; }

    /// <summary>Копии от свежей к старой. Для показа в админке.</summary>
    public IReadOnlyList<DatabaseBackup> List()
    {
        try
        {
            if (!System.IO.Directory.Exists(Directory)) return Array.Empty<DatabaseBackup>();

            return System.IO.Directory.EnumerateFiles(Directory, "elora-*.db")
                .Select(path => new FileInfo(path))
                .OrderByDescending(file => file.Name)
                .Select(file => new DatabaseBackup(file.Name, file.LastWriteTime, file.Length))
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Не удалось прочитать список резервных копий");
            return Array.Empty<DatabaseBackup>();
        }
    }

    /// <summary>
    /// Делает копию за сегодня. Возвращает имя файла либо <c>null</c>, если копия уже есть
    /// или сделать её не удалось. Кнопка в админке вызывает это же, поэтому метод открыт.
    /// </summary>
    public string? CreateToday()
    {
        try
        {
            System.IO.Directory.CreateDirectory(Directory);

            var name = $"elora-{DateTime.Now:yyyy-MM-dd}.db";
            var target = Path.Combine(Directory, name);
            if (File.Exists(target)) return null;

            using var connection = _factory.Create();
            using var command = connection.CreateCommand();
            // Путь подставляем параметром: VACUUM INTO не принимает параметры, поэтому
            // собираем запрос сами, но кавычки в пути экранируем удвоением.
            command.CommandText = $"VACUUM INTO '{target.Replace("'", "''")}'";
            command.ExecuteNonQuery();

            Prune();
            _logger.LogInformation("Резервная копия базы: {File}", name);
            return name;
        }
        catch (Exception ex)
        {
            // Копия — вспомогательное дело: её сбой не должен влиять на работу сайта.
            _logger.LogWarning(ex, "Резервную копию базы сделать не удалось");
            return null;
        }
    }

    /// <summary>Удаляет самые старые копии, оставляя <see cref="KeepCount"/> последних.</summary>
    private void Prune()
    {
        var extra = System.IO.Directory.EnumerateFiles(Directory, "elora-*.db")
            .OrderByDescending(path => path)
            .Skip(KeepCount)
            .ToList();

        foreach (var path in extra)
        {
            try
            {
                File.Delete(path);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Старую копию {File} удалить не удалось", Path.GetFileName(path));
            }
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Копия делается и при первом запуске: на новом хостинге её ещё нет, а ждать сутки
        // ради первой копии незачем.
        await Delay(StartupDelay, stoppingToken);
        if (stoppingToken.IsCancellationRequested) return;

        while (!stoppingToken.IsCancellationRequested)
        {
            CreateToday();
            await Delay(Interval, stoppingToken);
        }
    }

    private static async Task Delay(TimeSpan interval, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(interval, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Остановка приложения — обычное дело, шуметь не о чем.
        }
    }
}
