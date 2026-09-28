using Microsoft.Data.Sqlite;

namespace ELORA.Web.Data;

/// <summary>Применяет schema.sql и заполняет базу стартовыми данными ELORA.</summary>
public sealed class DatabaseInitializer
{
    private readonly SqliteConnectionFactory _factory;
    private readonly IWebHostEnvironment _environment;
    private readonly ILogger<DatabaseInitializer> _logger;

    public DatabaseInitializer(
        SqliteConnectionFactory factory,
        IWebHostEnvironment environment,
        ILogger<DatabaseInitializer> logger)
    {
        _factory = factory;
        _environment = environment;
        _logger = logger;
    }

    public void Initialize()
    {
        using var connection = _factory.Create();

        var schemaPath = Path.Combine(_environment.ContentRootPath, "Data", "Sql", "schema.sql");
        if (!File.Exists(schemaPath))
            schemaPath = Path.Combine(AppContext.BaseDirectory, "Data", "Sql", "schema.sql");

        if (File.Exists(schemaPath))
        {
            using var command = connection.CreateCommand();
            command.CommandText = File.ReadAllText(schemaPath);
            command.ExecuteNonQuery();
        }
        else
        {
            _logger.LogWarning("schema.sql не найден по пути {Path}", schemaPath);
        }

        Migrate(connection);
        Seed.Run(connection, _logger);
        _logger.LogInformation("База данных ELORA готова: {Path}", _factory.DatabasePath);
    }

    /// <summary>
    /// Досыпает колонки, появившиеся после первой версии базы.
    /// </summary>
    /// <remarks>
    /// <c>CREATE TABLE IF NOT EXISTS</c> не трогает уже существующую таблицу, поэтому новые
    /// колонки в неё так не попадут — нужен отдельный шаг. Отдельного инструмента миграций
    /// здесь нет намеренно: колонок мало, и лишняя зависимость не окупается.
    /// </remarks>
    private void Migrate(SqliteConnection connection)
    {
        AddColumnIfMissing(connection, "Clients", "Notes", "TEXT");
        AddColumnIfMissing(connection, "Clients", "LastRemindedAt", "TEXT");
        AddColumnIfMissing(connection, "Clients", "TelegramNick", "TEXT");
    }

    private void AddColumnIfMissing(SqliteConnection connection, string table, string column, string type)
    {
        if (ColumnExists(connection, table, column)) return;

        using var alter = connection.CreateCommand();
        alter.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {type}";
        alter.ExecuteNonQuery();

        _logger.LogInformation("Миграция: добавлена колонка {Table}.{Column}", table, column);
    }

    private static bool ColumnExists(SqliteConnection connection, string table, string column)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info({table})";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }
}
