using Microsoft.Data.Sqlite;

namespace ELORA.Web.Data;

/// <summary>
/// Создаёт открытые соединения с SQLite и всегда включает поддержку внешних ключей.
/// Путь к БД берётся из конфигурации (Database:Path) — на проде задаётся через
/// переменную окружения Database__Path, чтобы файл лежал на persistent storage.
/// </summary>
public sealed class SqliteConnectionFactory
{
    private readonly string _connectionString;

    public SqliteConnectionFactory(IConfiguration configuration, IWebHostEnvironment environment)
    {
        var configured = configuration["Database:Path"];
        var path = string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(environment.ContentRootPath, "App_Data", "elora.db")
            : configured;

        if (!Path.IsPathRooted(path))
            path = Path.Combine(environment.ContentRootPath, path);

        DatabasePath = Path.GetFullPath(path);

        var directory = Path.GetDirectoryName(DatabasePath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            ForeignKeys = true,
            DefaultTimeout = 30
        }.ToString();
    }

    public string DatabasePath { get; }

    public SqliteConnection Create()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var pragma = connection.CreateCommand();
        pragma.CommandText = "PRAGMA foreign_keys = ON; PRAGMA busy_timeout = 10000;";
        pragma.ExecuteNonQuery();
        return connection;
    }
}
